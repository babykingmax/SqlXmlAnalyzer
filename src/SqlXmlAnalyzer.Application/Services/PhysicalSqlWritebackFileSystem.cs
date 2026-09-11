using System.Security.AccessControl;

namespace SqlXmlAnalyzer.Application.Services;

public sealed class PhysicalSqlWritebackFileSystem : ISqlWritebackFileSystem
{
    public byte[] ReadAllBytes(string path, int maxBytes, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        RejectLink(path);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return SqlBoundedFileReader.Read(stream, maxBytes, cancellationToken);
    }

    public byte[] ReadAllBytes(string path)
    {
        RejectLink(path);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    public Stream CreateNew(string path, string sourcePath)
    {
        // Apply the source DACL at creation. Backup and temporary SQL must not acquire
        // broader access from the parent directory, even before their first write.
        if (OperatingSystem.IsWindows())
        {
            var sections = AccessControlSections.Access | AccessControlSections.Owner;
            var original = new FileInfo(sourcePath).GetAccessControl(sections);
            var security = new FileSecurity();
            security.SetSecurityDescriptorBinaryForm(original.GetSecurityDescriptorBinaryForm(), sections);
            return new FileInfo(path).Create(FileMode.CreateNew, FileSystemRights.Write,
                FileShare.None, 64 * 1024, FileOptions.None, security);
        }
        return new FileStream(path, new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.Write,
            Share = FileShare.None,
            UnixCreateMode = File.GetUnixFileMode(sourcePath) & (UnixFileMode)0x1FF
        });
    }

    public void FlushToDisk(Stream stream) => ((FileStream)stream).Flush(flushToDisk: true);

    public void Replace(string temporaryPath, string sourcePath)
    {
        if (!string.Equals(Path.GetDirectoryName(Path.GetFullPath(temporaryPath)),
            Path.GetDirectoryName(Path.GetFullPath(sourcePath)),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new IOException("SQL replacement must remain in the source directory.");
        RejectLink(sourcePath);
        // A same-directory rename commits complete bytes. Do not fall back to WriteAllText,
        // delete-then-move, or copy-overwrite if the OS refuses the operation.
        File.Move(temporaryPath, sourcePath, overwrite: true);
    }

    public void Delete(string path) => File.Delete(path);

    private static void RejectLink(string path)
    {
        FileAttributes attributes = File.GetAttributes(path);
        if ((attributes & FileAttributes.ReparsePoint) != 0)
            throw new IOException("SQL writeback does not support a symbolic-link source file.");
        if ((attributes & FileAttributes.Encrypted) != 0)
            throw new IOException("SQL writeback does not support EFS-encrypted source files; no plaintext backup will be created.");
    }
}
