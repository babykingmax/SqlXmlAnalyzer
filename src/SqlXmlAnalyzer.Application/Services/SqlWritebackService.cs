using SqlXmlAnalyzer.Application.Models;
using SqlXmlAnalyzer.Core.Diagnostics;

namespace SqlXmlAnalyzer.Application.Services;

public sealed class SqlWritebackService : ISqlWritebackService
{
    private readonly ISqlWritebackFileSystem _files;
    private readonly Func<Guid> _createId;
    private readonly IUnexpectedErrorReporter _unexpectedErrors;

    public SqlWritebackService(ISqlWritebackFileSystem files, Func<Guid>? createId = null,
        IUnexpectedErrorReporter? unexpectedErrors = null)
    {
        _files = files;
        _createId = createId ?? Guid.NewGuid;
        _unexpectedErrors = unexpectedErrors ?? UnexpectedErrorReporter.Shared;
    }

    public SqlFileSnapshot ReadSnapshot(string path, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var snapshot = SqlFileSnapshot.FromBytes(path, _files.ReadAllBytes(Path.GetFullPath(path)));
        cancellationToken.ThrowIfCancellationRequested();
        return snapshot;
    }

    public SqlFileSnapshot ReadSnapshot(string path, int maxCharacters, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxCharacters);
        cancellationToken.ThrowIfCancellationRequested();
        // UTF-32 is the largest supported encoding per UTF-16 code unit, plus its BOM.
        int maxBytes = checked(maxCharacters * 4 + 4);
        byte[] bytes = _files.ReadAllBytes(Path.GetFullPath(path), maxBytes, cancellationToken);
        if (bytes.Length > maxBytes) throw SqlBoundedFileReader.Exceeded();
        var snapshot = SqlFileSnapshot.FromBytes(path, bytes);
        if (snapshot.Text.Length > maxCharacters) throw SqlBoundedFileReader.Exceeded();
        cancellationToken.ThrowIfCancellationRequested();
        return snapshot;
    }

    public SqlWritebackResult WriteBack(SqlFileSnapshot snapshot, string sql, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(sql);
        var stage = SqlWritebackStage.ValidateSource;
        var warnings = new List<string>();
        var diagnostics = new List<UnexpectedErrorReport>();
        string DescribeFailure(Exception failure, string operation)
        {
            if (ExceptionPolicy.IsExpected(failure) || failure is NotSupportedException)
            {
                if (failure is OperationCanceledException) Logger.Warning($"SQL 写回取消：{operation}");
                else Logger.Error($"SQL 写回失败：{operation}", failure);
                return ExceptionPolicy.Message(failure);
            }
            var diagnostic = ExceptionPolicy.Capture(failure, operation, _unexpectedErrors);
            diagnostics.Add(diagnostic);
            return $"{ExceptionPolicy.Message(failure)}；{diagnostic.Summary}";
        }
        string? backup = null, temporary = null, outputHash = null;
        bool backupVerified = false, temporaryOwned = false;
        int outputLength = snapshot.ByteLength;
        try
        {
            Logger.Debug("SQL 写回开始：校验源文件及候选字节。");
            cancellationToken.ThrowIfCancellationRequested();
            byte[] replacement = snapshot.Encode(sql);
            outputLength = replacement.Length;
            outputHash = SqlFileSnapshot.Hash(replacement);
            VerifySource(snapshot, cancellationToken);
            if (outputHash == snapshot.Sha256)
                return new(true, false, false, false, SqlWritebackStage.Completed, false, null, null,
                    snapshot.Sha256, outputHash, null, null, warnings);

            stage = SqlWritebackStage.CreateBackup;
            using (Stream stream = CreateOwnedFile(snapshot.Path, ".bak", cancellationToken, out backup))
                WriteAndFlush(stream, snapshot.CopyOriginalBytes(), cancellationToken);
            stage = SqlWritebackStage.ValidateBackup;
            if (SqlFileSnapshot.Hash(_files.ReadAllBytes(backup!, snapshot.ByteLength, cancellationToken)) != snapshot.Sha256)
                throw new IOException("备份字节校验失败，已停止写回。");
            backupVerified = true;
            Logger.Debug("SQL 写回：备份校验通过。");

            stage = SqlWritebackStage.WriteTemporary;
            using (Stream stream = CreateOwnedFile(snapshot.Path, ".tmp", cancellationToken, out temporary))
            {
                temporaryOwned = true;
                WriteAndFlush(stream, replacement, cancellationToken);
            }
            stage = SqlWritebackStage.ValidateTemporary;
            if (SqlFileSnapshot.Hash(_files.ReadAllBytes(temporary!, outputLength, cancellationToken)) != outputHash)
                throw new IOException("临时 SQL 字节校验失败，已停止写回。");

            stage = SqlWritebackStage.ValidateSource;
            VerifySource(snapshot, cancellationToken); // Recheck after backup/temp I/O and immediately before commit.
            cancellationToken.ThrowIfCancellationRequested();
            stage = SqlWritebackStage.Replace;
            _files.Replace(temporary!, snapshot.Path);
            temporaryOwned = false;
            Logger.Debug("SQL 写回：提交完成。");
            // Cancellation after this commit must not be reported as an unwritten/canceled file.
            return new(true, true, false, false, SqlWritebackStage.Completed, true, backup, null,
                snapshot.Sha256, outputHash, null, null, warnings);
        }
        catch (Exception ex)
        {
            bool sourceWritten = false, outcomeUnknown = false;
            if (stage == SqlWritebackStage.Replace)
            {
                // A remote/storage failure may lose the acknowledgement after a rename.
                // Never infer "unchanged" merely from the exception, or restore over a
                // third party's newer data. Preserve evidence when the outcome is uncertain.
                try
                {
                    string currentHash = SqlFileSnapshot.Hash(_files.ReadAllBytes(snapshot.Path,
                        Math.Max(snapshot.ByteLength, outputLength), CancellationToken.None));
                    sourceWritten = currentHash == outputHash;
                    outcomeUnknown = !sourceWritten && currentHash != snapshot.Sha256;
                }
                catch (Exception check)
                {
                    outcomeUnknown = true;
                    warnings.Add($"替换后无法核对源文件：{DescribeFailure(check, "SqlWriteback.VerifyCommit")}");
                }
                if (sourceWritten) { temporaryOwned = false; temporary = null; }
                if (outcomeUnknown) warnings.Add("提交结果未知，保留备份和临时路径供人工核对，不自动回滚。");
            }
            if (!outcomeUnknown && temporaryOwned && temporary != null)
            {
                try { _files.Delete(temporary); temporary = null; }
                catch (Exception cleanup)
                {
                    warnings.Add($"临时文件清理失败，请在核对后移除：{temporary}；{DescribeFailure(cleanup, "SqlWriteback.CleanupTemporary")}");
                }
            }
            if (backup != null && !backupVerified)
            {
                try { _files.Delete(backup); backup = null; }
                catch (Exception cleanup)
                {
                    warnings.Add($"未验证的备份残留不可用于恢复：{backup}；{DescribeFailure(cleanup, "SqlWriteback.CleanupBackup")}");
                }
            }
            string recovery = backupVerified
                ? $" 已验证备份：{backup}；源 SHA-256：{snapshot.Sha256}。恢复前先另存当前文件，再校验备份并复制恢复。"
                : " 未生成可验证备份。";
            string state = outcomeUnknown ? "无法确认提交结果，请先核对当前源文件" : sourceWritten
                ? "当前源文件已包含候选 SQL，请勿按未写入处理" : "未覆盖当前源文件";
            string detail = DescribeFailure(ex, $"SqlWriteback.{stage}");
            string message = $"SQL 写回{(ex is OperationCanceledException ? "已取消" : "失败")}（{stage}），{state}：{detail}{recovery}";
            return new(false, sourceWritten, outcomeUnknown, ex is OperationCanceledException, stage, backupVerified, backup, temporary,
                snapshot.Sha256, outputHash, message, ex, warnings) { Diagnostics = diagnostics };
        }
    }

    private Stream CreateOwnedFile(string sourcePath, string extension, CancellationToken token, out string? ownedPath)
    {
        ownedPath = null;
        for (int attempt = 0; attempt < 8; attempt++)
        {
            token.ThrowIfCancellationRequested();
            string path = sourcePath + $".SqlXmlAnalyzer-{_createId():N}" + extension;
            try
            {
                Stream stream = _files.CreateNew(path, sourcePath);
                ownedPath = path; // Ownership begins only after CreateNew succeeds.
                return stream;
            }
            catch (IOException ex) when ((ex.HResult & 0xFFFF) is 80 or 183 ||
                (!OperatingSystem.IsWindows() && (ex.HResult & 0xFFFF) == 17))
            {
                // Only an actual create-new name collision is retryable. Disk/permission
                // failures must not be mistaken for collisions just because a path exists.
            }
        }
        throw new IOException("无法分配不冲突的备份或临时文件名。");
    }

    private void VerifySource(SqlFileSnapshot snapshot, CancellationToken token)
    {
        if (SqlFileSnapshot.Hash(_files.ReadAllBytes(snapshot.Path, snapshot.ByteLength, token)) != snapshot.Sha256)
            throw new IOException("源 SQL 自读取后已被外部修改，请重新读取并审核候选 SQL。");
    }

    private void WriteAndFlush(Stream stream, byte[] bytes, CancellationToken token)
    {
        const int blockSize = 64 * 1024;
        for (int offset = 0; offset < bytes.Length; offset += blockSize)
        {
            token.ThrowIfCancellationRequested();
            stream.Write(bytes, offset, Math.Min(blockSize, bytes.Length - offset));
        }
        token.ThrowIfCancellationRequested();
        _files.FlushToDisk(stream);
    }
}
