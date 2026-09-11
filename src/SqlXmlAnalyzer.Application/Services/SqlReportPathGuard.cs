using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace SqlXmlAnalyzer.Application.Services;

public static class SqlReportPathGuard
{
    public static void ValidateInputs(IEnumerable<string?> inputPaths, string? outputPath)
    {
        foreach (string? inputPath in inputPaths)
            if (!string.IsNullOrWhiteSpace(inputPath)) Validate(inputPath, outputPath);
    }

    public static void Validate(string sqlPath, string? outputPath)
    {
        if (string.IsNullOrWhiteSpace(outputPath)) return;
        string source = Path.GetFullPath(sqlPath), output = Path.GetFullPath(outputPath);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (string.Equals(source, output, comparison))
            throw new IOException("报告输出不能覆盖输入文件。");
        if (!File.Exists(source) || !File.Exists(output)) return;

        string resolvedSource = new FileInfo(source).ResolveLinkTarget(true)?.FullName ?? source;
        string resolvedOutput = new FileInfo(output).ResolveLinkTarget(true)?.FullName ?? output;
        if (string.Equals(resolvedSource, resolvedOutput, comparison))
            throw new IOException("报告输出链接指向输入文件。");
        if (OperatingSystem.IsWindows())
        {
            using SafeFileHandle sourceHandle = File.OpenHandle(source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using SafeFileHandle outputHandle = File.OpenHandle(output, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (!GetFileInformationByHandle(sourceHandle, out var sourceInfo) ||
                !GetFileInformationByHandle(outputHandle, out var outputInfo))
                throw new IOException("无法验证报告输出与输入文件的身份。", new Win32Exception(Marshal.GetLastWin32Error()));
            if (sourceInfo.VolumeSerialNumber == outputInfo.VolumeSerialNumber &&
                sourceInfo.FileIndexHigh == outputInfo.FileIndexHigh && sourceInfo.FileIndexLow == outputInfo.FileIndexLow)
                throw new IOException("报告输出与输入是同一个文件（硬链接或路径别名）。");
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle file, out FileInformation information);

    [StructLayout(LayoutKind.Sequential)]
    private struct FileInformation
    {
        public uint Attributes;
        public System.Runtime.InteropServices.ComTypes.FILETIME CreationTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastAccessTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWriteTime;
        public uint VolumeSerialNumber;
        public uint FileSizeHigh;
        public uint FileSizeLow;
        public uint NumberOfLinks;
        public uint FileIndexHigh;
        public uint FileIndexLow;
    }
}
