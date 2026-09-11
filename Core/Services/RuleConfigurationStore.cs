using System.IO;
using System.Text;
using SqlXmlAnalyzer.Application.Models;
using SqlXmlAnalyzer.Application.Services;
using SqlXmlAnalyzer.Core.Configuration;
using SqlXmlAnalyzer.Core.Diagnostics;

namespace SqlXmlAnalyzer.Core.Services;

public sealed record RuleConfigurationFile(string Path, SqlFileSnapshot? Snapshot, RuleConfigurationDocument Document);
public sealed record RuleConfigurationSaveOutcome(bool Succeeded, RuleConfigurationFile? File, string Message,
    bool SourceWritten = false, bool CommitOutcomeUnknown = false);
public interface IRuleConfigurationStore
{
    Task<RuleConfigurationFile> LoadAsync(string? path, CancellationToken token);
    Task<RuleConfigurationSaveOutcome> SaveAsync(RuleConfigurationFile file, RuleConfigurationDocument document, CancellationToken token);
    Task<RuleConfigurationSaveOutcome> SaveAsAsync(string path, RuleConfigurationDocument document, CancellationToken token);
}

public sealed class RuleConfigurationStore : IRuleConfigurationStore
{
    private readonly ISqlWritebackFileSystem _files;
    private readonly ISqlWritebackService _writeback;
    private readonly IUnexpectedErrorReporter _reporter;
    public RuleConfigurationStore(ISqlWritebackFileSystem? files = null, IUnexpectedErrorReporter? reporter = null)
    {
        _files = files ?? new PhysicalSqlWritebackFileSystem();
        _reporter = reporter ?? UnexpectedErrorReporter.Shared;
        _writeback = new SqlWritebackService(_files, unexpectedErrors: _reporter);
    }
    public Task<RuleConfigurationFile> LoadAsync(string? path, CancellationToken token) => Task.Run(() =>
    {
        token.ThrowIfCancellationRequested();
        string resolved = Resolve(path);
        try
        {
            byte[] bytes = _files.ReadAllBytes(resolved, RuleConfigurationDocument.MaxBytes, token);
            if (bytes.Length > RuleConfigurationDocument.MaxBytes) throw new InvalidDataException("规则配置超过 1 MiB 文件预算。");
            var snapshot = SqlFileSnapshot.FromBytes(resolved, bytes);
            var document = RuleConfigurationDocument.Parse(snapshot.Text, token);
            token.ThrowIfCancellationRequested();
            Logger.Debug($"IMP24 editor loaded: known={document.ToLegacy().Rules.Count}, unknown={document.UnknownRules.Count}.");
            if (document.Warnings.Count > 0) Logger.Warning($"IMP24 preserved configuration notices: {document.Warnings.Count}.");
            return new RuleConfigurationFile(resolved, snapshot, document);
        }
        catch (FileNotFoundException) when (path == null)
        {
            Logger.Warning("IMP24 default file missing; editor starts with built-in defaults.");
            return new RuleConfigurationFile(resolved, null, RuleConfigurationDocument.Defaults);
        }
    }, token);

    public Task<RuleConfigurationSaveOutcome> SaveAsync(RuleConfigurationFile file, RuleConfigurationDocument document, CancellationToken token)
    {
        if (file.Snapshot == null) return SaveAsAsync(file.Path, document, token);
        return Task.Run(() =>
        {
            token.ThrowIfCancellationRequested();
            byte[] bytes = file.Snapshot.Encode(document.Json);
            if (bytes.Length > RuleConfigurationDocument.MaxBytes) throw new InvalidDataException("保存后的配置超过 1 MiB 文件预算。");
            var saved = new RuleConfigurationFile(file.Path, SqlFileSnapshot.FromBytes(file.Path, bytes), document);
            Logger.Debug("IMP24 configuration save started.");
            var result = _writeback.WriteBack(file.Snapshot, document.Json, token);
            if (!result.IsSuccess)
                return new RuleConfigurationSaveOutcome(false, null,
                    (result.SourceWritten ? "写入已发生，请重新载入核对。" : result.CommitOutcomeUnknown ? "提交结果未知，请重新载入核对。" : "配置未保存。")
                    + ConfigurationMessage(result.ErrorMessage) + Environment.NewLine + string.Join(Environment.NewLine, result.Warnings.Select(ConfigurationMessage)),
                    result.SourceWritten, result.CommitOutcomeUnknown);
            // The commit has happened; a late cancellation must not be reported as an unwritten file.
            Logger.Debug("IMP24 configuration save completed.");
            return new RuleConfigurationSaveOutcome(true, saved, "配置已保存。"
                + (result.BackupPath == null ? "" : "原配置备份：" + result.BackupPath), result.SourceWritten);
        }, token);
    }

    public Task<RuleConfigurationSaveOutcome> SaveAsAsync(string path, RuleConfigurationDocument document, CancellationToken token) => Task.Run(() =>
    {
        string destination = Resolve(path);
        if (string.IsNullOrWhiteSpace(path) || !string.Equals(Path.GetExtension(destination), ".json", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("另存配置请使用新的 .json 文件名。");
        if (File.Exists(destination) || Directory.Exists(destination)) throw new IOException("另存配置不会覆盖既有文件；如需更新，请先载入该文件再保存。");
        byte[] bytes = new UTF8Encoding(false, true).GetBytes(document.Json);
        string temporary = Path.Combine(Path.GetDirectoryName(destination)!, ".tmp.rule-config-" + Guid.NewGuid().ToString("N"));
        bool owned = false, committing = false;
        try
        {
            token.ThrowIfCancellationRequested();
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                owned = true; stream.Write(bytes); stream.Flush(true);
            }
            token.ThrowIfCancellationRequested();
            committing = true;
            File.Move(temporary, destination, overwrite: false); owned = false;
            Logger.Debug("IMP24 new configuration file saved.");
            return new RuleConfigurationSaveOutcome(true, new(destination, SqlFileSnapshot.FromBytes(destination, bytes), document), "新配置文件已保存。", true);
        }
        catch (Exception exception)
        {
            bool written = false, unknown = false;
            if (committing)
            {
                try { written = SqlFileSnapshot.Hash(_files.ReadAllBytes(destination, RuleConfigurationDocument.MaxBytes, CancellationToken.None)) == SqlFileSnapshot.Hash(bytes); }
                catch (FileNotFoundException) { }
                catch (Exception check) { unknown = true; ExceptionPolicy.Describe(check, "IMP24.Configuration.VerifySaveAs", _reporter); }
            }
            string detail = ExceptionPolicy.Describe(exception, "IMP24.Configuration.SaveAs", _reporter);
            return new RuleConfigurationSaveOutcome(false, null,
                (written || unknown ? "写入确认失败，请重新载入核对。" : "另存失败。") + detail, written, unknown);
        }
        finally
        {
            if (owned) try { File.Delete(temporary); }
                catch (Exception exception) { ExceptionPolicy.Describe(exception, "IMP24.Configuration.Cleanup", _reporter); }
        }
    }, token);

    private static string ConfigurationMessage(string? message) => (message ?? "")
        .Replace("源 SQL", "源配置", StringComparison.Ordinal).Replace("候选 SQL", "配置草稿", StringComparison.Ordinal)
        .Replace("SQL 写回", "配置保存", StringComparison.Ordinal).Replace("原 SQL", "原配置", StringComparison.Ordinal);

    private static string Resolve(string? path)
    {
        try { return RuleConfigurationPathResolver.Resolve(path); }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        { throw new InvalidDataException("规则配置文件路径无效。", exception); }
    }
}
