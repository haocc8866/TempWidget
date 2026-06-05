using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace TempWidget.Services;

/// <summary>
/// 多显示器辅助: 用纯 Win32 API (EnumDisplayMonitors + GetMonitorInfo) 列出所有显示器工作区
/// 不依赖 System.Windows.Forms.Screen
/// </summary>
public static class ScreenHelper
{
    private const uint MONITORINFOF_PRIMARY = 0x00000001;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip,
        MonitorEnumProc lpfnEnum, IntPtr dwData);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfoW(IntPtr hMonitor, ref MONITORINFO lpmi);

    private delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr dwData);

    /// <summary>
    /// 枚举所有显示器工作区, 转 DIP
    /// </summary>
    public static IEnumerable<Rect> EnumerateWorkAreasInDip(Visual? visual)
    {
        double dx = 1.0, dy = 1.0;
        if (visual != null)
        {
            var source = PresentationSource.FromVisual(visual);
            if (source?.CompositionTarget != null)
            {
                dx = source.CompositionTarget.TransformToDevice.M11;
                dy = source.CompositionTarget.TransformToDevice.M22;
            }
        }

        var results = new List<Rect>();
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr h, IntPtr hdc, ref RECT r, IntPtr dw) =>
        {
            var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
            if (GetMonitorInfoW(h, ref mi))
            {
                int w = mi.rcWork.Right - mi.rcWork.Left;
                int h2 = mi.rcWork.Bottom - mi.rcWork.Top;
                results.Add(new Rect(
                    mi.rcWork.Left / dx,
                    mi.rcWork.Top / dy,
                    w / dx,
                    h2 / dy));
            }
            return true;  // 继续枚举
        }, IntPtr.Zero);
        return results;
    }

    /// <summary>
    /// 校验 (left, top, w, h) 矩形是否在任意显示器工作区内有足够可见面积
    /// 防止上次在双屏关闭, 这次变单屏导致窗口"隐形"
    /// </summary>
    public static bool IsRectVisible(double left, double top, double w, double h, Visual? visual,
        double minVisibleW = 80, double minVisibleH = 30)
    {
        var windowRect = new Rect(left, top, w, h);
        foreach (var monitor in EnumerateWorkAreasInDip(visual))
        {
            var intersection = Rect.Intersect(windowRect, monitor);
            if (intersection.Width >= minVisibleW && intersection.Height >= minVisibleH)
                return true;
        }
        return false;
    }
}
