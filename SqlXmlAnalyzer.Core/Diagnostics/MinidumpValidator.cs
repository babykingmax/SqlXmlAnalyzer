namespace SqlXmlAnalyzer.Core.Diagnostics;

/// <summary>Checks the public header and stream directory, not debugger-level completeness.</summary>
internal static class MinidumpValidator
{
    internal static void Validate(string path)
    {
        using var stream = File.OpenRead(path);
        Validate(stream);
    }

    internal static void Validate(Stream stream, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        stream.Position = 0;
        using var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, leaveOpen: true);
        if (stream.Length < 32 || reader.ReadUInt32() != 0x504D444D || (reader.ReadUInt32() & 0xffff) != 0xa793)
            throw new IOException("Invalid minidump signature or version.");
        uint count = reader.ReadUInt32(), directory = reader.ReadUInt32();
        if (count == 0 || directory < 32 || (ulong)directory + (ulong)count * 12 > (ulong)stream.Length)
            throw new IOException("Invalid or truncated minidump stream directory.");
        for (uint index = 0; index < count; index++)
        {
            token.ThrowIfCancellationRequested();
            stream.Position = directory + (long)index * 12;
            reader.ReadUInt32(); // Stream types may be extended by future Windows versions.
            uint length = reader.ReadUInt32(), offset = reader.ReadUInt32();
            if ((ulong)offset + length > (ulong)stream.Length)
                throw new IOException("Truncated minidump stream data.");
        }
    }
}
