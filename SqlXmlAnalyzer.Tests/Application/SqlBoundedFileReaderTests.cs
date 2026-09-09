using System.IO;
using System.Text;
using FluentAssertions;
using SqlXmlAnalyzer.Application.Services;

namespace SqlXmlAnalyzer.Tests.Application;

public sealed class SqlBoundedFileReaderTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "SqlXmlAnalyzer-IMP19Bounded-" + Guid.NewGuid().ToString("N"));
    public SqlBoundedFileReaderTests() => Directory.CreateDirectory(_directory);

    [Fact]
    public void Read_OversizedKnownLengthRejectsBeforeReading()
    {
        using var stream = new Input(1_000_000, seekable: true);
        Action read = () => SqlBoundedFileReader.Read(stream, 8, default);
        read.Should().Throw<InvalidDataException>().WithMessage("SqlInputBudget:*");
        stream.ReadBytes.Should().Be(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Read_UnknownOrGrowingLengthConsumesAtMostLimitPlusOne(bool misleadingLength)
    {
        using var stream = new Input(1000, misleadingLength, reportedLength: 2);
        Action read = () => SqlBoundedFileReader.Read(stream, 8, default);
        read.Should().Throw<InvalidDataException>();
        stream.ReadBytes.Should().Be(9);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(7)]
    [InlineData(8)]
    public void Read_ShortReadsPreserveAllBytesAtBoundary(int count)
    {
        using var stream = new Input(count, false);
        SqlBoundedFileReader.Read(stream, 8, default).Should().Equal(Enumerable.Repeat((byte)'x', count));
    }

    [Fact]
    public void Read_CancellationDuringIoStopsBeforeAnotherRead()
    {
        using var cancellation = new CancellationTokenSource();
        using var stream = new Input(100, false) { AfterRead = cancellation.Cancel };
        Action read = () => SqlBoundedFileReader.Read(stream, 100, cancellation.Token);
        read.Should().Throw<OperationCanceledException>();
        stream.ReadBytes.Should().Be(3);
    }

    [Theory]
    [InlineData("utf8")]
    [InlineData("utf16-le")]
    [InlineData("utf16-be")]
    [InlineData("utf32-le")]
    [InlineData("utf32-be")]
    public void Snapshot_EncodingAndBomSurviveExactCharacterLimit(string kind)
    {
        Encoding encoding = kind switch
        {
            "utf8" => new UTF8Encoding(true, true),
            "utf16-le" => new UnicodeEncoding(false, true, true),
            "utf16-be" => new UnicodeEncoding(true, true, true),
            "utf32-le" => new UTF32Encoding(false, true, true),
            _ => new UTF32Encoding(true, true, true)
        };
        string path = Path.Combine(_directory, kind + ".sql");
        const string sql = "SELECT N'漢😀';";
        File.WriteAllText(path, sql, encoding);
        var snapshot = new SqlWritebackService(new PhysicalSqlWritebackFileSystem()).ReadSnapshot(path, sql.Length);
        snapshot.Text.Should().Be(sql);
        snapshot.Encode(sql).Should().Equal(File.ReadAllBytes(path));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Snapshot_ByteAndCharacterLimitsRejectOversizedInput(bool exceedBytes)
    {
        string path = Path.Combine(_directory, "oversized.sql");
        File.WriteAllText(path, new string('x', exceedBytes ? 100 : 9), new UTF8Encoding(false));
        Action read = () => new SqlWritebackService(new PhysicalSqlWritebackFileSystem()).ReadSnapshot(path, 8);
        read.Should().Throw<InvalidDataException>().WithMessage("SqlInputBudget:*");
    }

    private sealed class Input(long length, bool seekable, long? reportedLength = null) : Stream
    {
        public long ReadBytes { get; private set; }
        public Action? AfterRead;
        public override bool CanRead => true;
        public override bool CanSeek => seekable;
        public override bool CanWrite => false;
        public override long Length => reportedLength ?? length;
        public override long Position { get => ReadBytes; set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count)
        {
            int read = (int)Math.Min(Math.Min(count, 3), length - ReadBytes);
            Array.Fill(buffer, (byte)'x', offset, read);
            ReadBytes += read;
            AfterRead?.Invoke();
            return read;
        }
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    public void Dispose()
    {
        string full = Path.GetFullPath(_directory);
        if (!full.StartsWith(Path.Combine(Path.GetFullPath(Path.GetTempPath()), "SqlXmlAnalyzer-IMP19Bounded-"), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Unexpected cleanup path");
        Directory.Delete(full, true);
    }
}
