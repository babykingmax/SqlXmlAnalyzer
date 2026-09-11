using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace SqlXmlAnalyzer.Core.Diagnostics;

public sealed class WindowsMiniDumpWriter : ICrashDumpWriter
{
    // DbgHelp is process-wide and single threaded. Never queue workers behind a stuck capture.
    private static readonly SemaphoreSlim NativeGate = new(1, 1);
    private readonly Action<FileStream> _capture;
    private readonly TimeSpan _timeout;

    public WindowsMiniDumpWriter() : this(CaptureNative, TimeSpan.FromSeconds(30)) { }

    internal WindowsMiniDumpWriter(Action<FileStream> capture, TimeSpan timeout)
    {
        if (timeout <= TimeSpan.Zero || timeout.TotalMilliseconds > int.MaxValue) throw new ArgumentOutOfRangeException(nameof(timeout));
        _capture = capture;
        _timeout = timeout;
    }

    public void Write(string path)
    {
        if (!NativeGate.Wait(0)) throw new IOException("A DUMP capture is still running; no additional worker was started.");
        var stateGate = new object();
        bool canceled = false, completed = false, published = false;
        Exception? failure = null;
        var worker = new Thread(() =>
        {
            string temporary = path + ".partial";
            bool owned = false;
            try
            {
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    owned = true;
                    _capture(stream);
                    stream.Flush(flushToDisk: true);
                }
                lock (stateGate)
                {
                    if (!canceled)
                    {
                        File.Move(temporary, path);
                        owned = false;
                        published = true;
                    }
                }
            }
            catch (Exception ex) { failure = ex; }
            finally
            {
                if (owned)
                {
                    try { File.Delete(temporary); }
                    catch (Exception cleanup) { Logger.Error("DUMP 临时文件清理失败。", cleanup); }
                }
                lock (stateGate) { completed = true; }
                NativeGate.Release();
            }
        }) { IsBackground = true, Name = "SqlXmlAnalyzer dump writer" };
        try { worker.Start(); }
        catch { NativeGate.Release(); throw; }
        if (!worker.Join(_timeout))
        {
            lock (stateGate)
            {
                if (!completed)
                {
                    // Publication and timeout share a lock. A timed-out worker cannot publish later.
                    if (published) return;
                    canceled = true;
                    throw new TimeoutException("DUMP capture timed out; a .partial file may remain until the worker returns. No completed dump will be published later.");
                }
            }
        }
        if (failure != null) throw new IOException("Windows minidump capture failed.", failure);
    }

    private static void CaptureNative(FileStream stream)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Windows minidumps require Windows.");
        using var process = Process.GetCurrentProcess();
        // Managed exception stack is saved in the sidecar; no invented native exception pointers.
        if (!MiniDumpWriteDump(process.Handle, (uint)process.Id, stream.SafeFileHandle,
                0x00001000, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero))
            throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    [DllImport("Dbghelp.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool MiniDumpWriteDump(IntPtr process, uint processId, SafeFileHandle file,
        uint dumpType, IntPtr exceptionParam, IntPtr userStreamParam, IntPtr callbackParam);
}
