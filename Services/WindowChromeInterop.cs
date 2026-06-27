using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace SqlXmlAnalyzer.Services
{
    internal static class WindowChromeInterop
    {
        private const int WmGetMinMaxInfoMessage = 0x0024;
        private const uint MonitorDefaultToNearest = 0x00000002;

        public static void Attach(Window window)
        {
            ArgumentNullException.ThrowIfNull(window);

            IntPtr handle = new WindowInteropHelper(window).Handle;
            HwndSource.FromHwnd(handle)?.AddHook(WindowProc);
        }

        private static IntPtr WindowProc(
            IntPtr hwnd,
            int msg,
            IntPtr wParam,
            IntPtr lParam,
            ref bool handled)
        {
            if (msg == WmGetMinMaxInfoMessage)
            {
                WmGetMinMaxInfo(hwnd, lParam);
                handled = true;
            }

            return IntPtr.Zero;
        }

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr handle, uint flags);

        [DllImport("user32.dll")]
        private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MonitorInfo lpmi);

        private static void WmGetMinMaxInfo(IntPtr hwnd, IntPtr lParam)
        {
            MinMaxInfo mmi = Marshal.PtrToStructure<MinMaxInfo>(lParam);
            IntPtr monitor = MonitorFromWindow(hwnd, MonitorDefaultToNearest);

            if (monitor != IntPtr.Zero)
            {
                MonitorInfo monitorInfo = new()
                {
                    Size = Marshal.SizeOf(typeof(MonitorInfo))
                };
                GetMonitorInfo(monitor, ref monitorInfo);

                Rect rcWorkArea = monitorInfo.Work;
                Rect rcMonitorArea = monitorInfo.Monitor;

                mmi.MaxSize.X = Math.Abs(rcWorkArea.Right - rcWorkArea.Left);
                mmi.MaxSize.Y = Math.Abs(rcWorkArea.Bottom - rcWorkArea.Top);
                mmi.MaxPosition.X = Math.Abs(rcWorkArea.Left - rcMonitorArea.Left);
                mmi.MaxPosition.Y = Math.Abs(rcWorkArea.Top - rcMonitorArea.Top);
                mmi.MaxTrackSize.X = mmi.MaxSize.X;
                mmi.MaxTrackSize.Y = mmi.MaxSize.Y;
            }

            Marshal.StructureToPtr(mmi, lParam, true);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct Point
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MinMaxInfo
        {
            public Point Reserved;
            public Point MaxSize;
            public Point MaxPosition;
            public Point MinTrackSize;
            public Point MaxTrackSize;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct MonitorInfo
        {
            public int Size;
            public Rect Monitor;
            public Rect Work;
            public uint Flags;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct Rect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }
    }
}
