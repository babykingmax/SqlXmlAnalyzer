using SqlXmlAnalyzer.Application.Models;

namespace SqlXmlAnalyzer.Application.Services;

public interface ISqlWritebackService
{
    SqlFileSnapshot ReadSnapshot(string path, CancellationToken cancellationToken = default);
    SqlFileSnapshot ReadSnapshot(string path, int maxCharacters, CancellationToken cancellationToken = default);
    SqlWritebackResult WriteBack(SqlFileSnapshot snapshot, string sql, CancellationToken cancellationToken = default);
}

// Replace must perform a same-directory rename, never truncate/copy over the source.
public interface ISqlWritebackFileSystem
{
    byte[] ReadAllBytes(string path);
    byte[] ReadAllBytes(string path, int maxBytes, CancellationToken cancellationToken);
    Stream CreateNew(string path, string sourcePath);
    void FlushToDisk(Stream stream);
    void Replace(string temporaryPath, string sourcePath);
    void Delete(string path);
}
