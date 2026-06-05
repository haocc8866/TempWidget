using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using TempWidget.Services;
using TempWidget.ViewModels;
using Point = System.Windows.Point;
using Color = System.Windows.Media.Color;
using FontFamily = System.Windows.Media.FontFamily;
using FontStyles = System.Windows.FontStyles;
using FontWeights = System.Windows.FontWeights;
using FlowDirection = System.Windows.FlowDirection;
using Brushes = System.Windows.Media.Brushes;
using MessageBox = System.Windows.MessageBox;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxImage = System.Windows.MessageBoxImage;

namespace TempWidget;

public partial class MainWindow : Window
{
    // ===== Win32 P/Invoke: 多显示器工作区查询 =====
    private const uint MONITOR_DEFAULTTONEAREST = 0x00000002;

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

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr MonitorFromWindow(IntPtr hWnd, uint dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfoW(IntPtr hMonitor, ref MONITORINFO lpmi);

    // ===== Win32 P/Invoke: 窗口置顶 (无闪烁) =====
    private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_SHOWWINDOW = 0x0040;

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
        int X, int Y, int cx, int cy, uint uFlags);

    // ===== 字段 =====
    private MainViewModel? _vm;
    private bool _reallyClose;

    // 横竖版尺寸常量
    private const double VerticalWidth = 50;
    private const double VerticalHeight = 90;
    private const double HorizontalWidth = 160;
    private const double HorizontalHeight = 50;

    public MainWindow()
    {
        InitializeComponent();
        Tray.IconSource = CreateTrayIcon();
    }

    /// <summary>
    /// 生成托盘图标: 16x16 圆角背景 + 中央 T 字
    /// </summary>
    private static ImageSource CreateTrayIcon()
    {
        const int size = 16;
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            // 圆角青蓝色背景
            dc.DrawRoundedRectangle(
                new SolidColorBrush(Color.FromRgb(96, 196, 230)),
                null,
                new Rect(0, 0, size, size), 3, 3);

            // 中央 "T" 字 (代表 Temperature)
            var ft = new FormattedText("T",
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                new Typeface(new FontFamily("Arial"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal),
                11,
                Brushes.White,
                pixelsPerDip: 1.0);
            dc.DrawText(ft, new Point(((size - ft.Width) / 2), ((size - ft.Height) / 2)));
        }

        var rtb = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(visual);
        rtb.Freeze();
        return rtb;
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        _vm = new MainViewModel();
        DataContext = _vm;

        // 首次显示放在屏幕右上角 (用 ActualWidth 保证拿到真实尺寸)
        var work = SystemParameters.WorkArea;
        Left = work.Right - ActualWidth - 16;
        Top = work.Top + 16;

        // 同步托盘菜单的开机启动状态
        try { AutoStartMenu.IsChecked = AutoStartService.IsEnabled; }
        catch { /* 注册表读不到, 默认 false */ }
    }

    private void Window_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            // 双击关闭 (因为没有标题栏按钮, 双击作为退出)
            Close();
            return;
        }

        // 关键: 拖动前清掉上次磁吸动画, 否则动画仍在持续改写 Left/Top,
        // 会跟 DragMove 抢窗口控制权, 表现就是"第二次拖动不吸附 / 拖不动"
        BeginAnimation(LeftProperty, null);
        BeginAnimation(TopProperty, null);

        // 单击拖动窗口
        // DragMove 内部是同步阻塞的, 鼠标松开才返回, 之后立刻触发吸附
        try { DragMove(); } catch { /* 拖动中鼠标释放异常时忽略 */ }
        SnapToEdge();
    }

    private void Window_MouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        // 备选触发点: 如果未来 DragMove 被换掉, 这里兜底
        // 当前实际触发点在 MouseLeftButtonDown 的 DragMove 之后
    }

    /// <summary>
    /// 窗口边缘磁吸: 距屏幕任一边 < 25 DIP 时贴边, 带 200ms 缓动动画
    /// </summary>
    private void SnapToEdge()
    {
        const double threshold = 100;
        var area = GetWorkAreaInDip();
        double newLeft = Left;
        double newTop = Top;
        bool snapped = false;

        // 水平: 左/右
        double distLeft = Left - area.Left;
        double distRight = area.Right - (Left + ActualWidth);

        if (Math.Abs(distLeft) < threshold)
        {
            newLeft = area.Left;
            snapped = true;
        }
        else if (Math.Abs(distRight) < threshold)
        {
            newLeft = area.Right - ActualWidth;
            snapped = true;
        }

        // 垂直: 上/下
        double distTop = Top - area.Top;
        double distBottom = area.Bottom - (Top + ActualHeight);

        if (Math.Abs(distTop) < threshold)
        {
            newTop = area.Top;
            snapped = true;
        }
        else if (Math.Abs(distBottom) < threshold)
        {
            newTop = area.Bottom - ActualHeight;
            snapped = true;
        }

        if (snapped) AnimateTo(newLeft, newTop);
    }

    /// <summary>
    /// 获取当前窗口所在显示器的工作区, 像素转 DIP
    /// 纯 Win32 API (MonitorFromWindow + GetMonitorInfoW), 不依赖 System.Windows.Forms
    /// </summary>
    private Rect GetWorkAreaInDip()
    {
        var helper = new WindowInteropHelper(this);
        if (helper.Handle == IntPtr.Zero)
        {
            return new Rect(0, 0, SystemParameters.PrimaryScreenWidth, SystemParameters.PrimaryScreenHeight);
        }

        // 1. 找到当前窗口最近的那个显示器
        var hMonitor = MonitorFromWindow(helper.Handle, MONITOR_DEFAULTTONEAREST);
        if (hMonitor == IntPtr.Zero)
        {
            return new Rect(0, 0, SystemParameters.PrimaryScreenWidth, SystemParameters.PrimaryScreenHeight);
        }

        // 2. 读该显示器的工作区 (像素)
        var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfoW(hMonitor, ref mi))
        {
            return new Rect(0, 0, SystemParameters.PrimaryScreenWidth, SystemParameters.PrimaryScreenHeight);
        }

        int w = mi.rcWork.Right - mi.rcWork.Left;
        int h = mi.rcWork.Bottom - mi.rcWork.Top;

        // 3. 像素 → DIP (DPI 缩放)
        var source = PresentationSource.FromVisual(this);
        if (source?.CompositionTarget == null)
            return new Rect(mi.rcWork.Left, mi.rcWork.Top, w, h);

        double dx = source.CompositionTarget.TransformToDevice.M11;
        double dy = source.CompositionTarget.TransformToDevice.M22;
        return new Rect(mi.rcWork.Left / dx, mi.rcWork.Top / dy, w / dx, h / dy);
    }

    /// <summary>
    /// 平滑移动窗口到目标位置, 200ms Quadratic EaseOut
    /// </summary>
    private void AnimateTo(double targetLeft, double targetTop)
    {
        const double durationMs = 200;
        var easing = new QuadraticEase { EasingMode = EasingMode.EaseOut };

        BeginAnimation(LeftProperty, new DoubleAnimation(Left, targetLeft, TimeSpan.FromMilliseconds(durationMs)) { EasingFunction = easing });
        BeginAnimation(TopProperty, new DoubleAnimation(Top, targetTop, TimeSpan.FromMilliseconds(durationMs)) { EasingFunction = easing });
    }

    /// <summary>
    /// 平滑调整窗口大小 (横竖版旋转动画), 300ms Quadratic EaseInOut
    /// </summary>
    private void AnimateSize(double targetW, double targetH)
    {
        // 关键: 拖动前先停掉之前的尺寸动画, 避免跟磁吸动画冲突
        BeginAnimation(WidthProperty, null);
        BeginAnimation(HeightProperty, null);

        const double durationMs = 300;
        var easing = new QuadraticEase { EasingMode = EasingMode.EaseInOut };

        BeginAnimation(WidthProperty, new DoubleAnimation(ActualWidth, targetW, TimeSpan.FromMilliseconds(durationMs)) { EasingFunction = easing });
        BeginAnimation(HeightProperty, new DoubleAnimation(ActualHeight, targetH, TimeSpan.FromMilliseconds(durationMs)) { EasingFunction = easing });
    }

    /// <summary>
    /// 托盘菜单: 切换横/竖版
    /// </summary>
    private void OrientationMenu_Click(object sender, RoutedEventArgs e)
    {
        if (_vm is null) return;

        bool toHorizontal = !_vm.IsHorizontal;
        _vm.IsHorizontal = toHorizontal;

        if (toHorizontal)
        {
            OrientationMenu.Header = "切换为竖版样式";
            AnimateSize(HorizontalWidth, HorizontalHeight);
        }
        else
        {
            OrientationMenu.Header = "切换为横版样式";
            AnimateSize(VerticalWidth, VerticalHeight);
        }

        // 旋转后立即吸一次边 (旋转过程结束后窗口可能不正贴边)
        // 延迟等动画结束再吸附
        Dispatcher.BeginInvoke(new Action(() => SnapToEdge()),
            System.Windows.Threading.DispatcherPriority.ApplicationIdle);
    }

    private void Window_StateChanged(object sender, System.EventArgs e)
    {
        if (WindowState == WindowState.Minimized)
            HideToTray();
    }

    private void Window_Closing(object sender, CancelEventArgs e)
    {
        if (!_reallyClose)
        {
            // 关闭按钮: 最小化到托盘, 而不是真的退出
            e.Cancel = true;
            HideToTray();
            return;
        }

        // 真的退出: 释放资源
        Tray.Dispose();
        _vm?.Dispose();
    }

    private void HideToTray()
    {
        Hide();
    }

    private void Tray_TrayMouseDoubleClick(object sender, System.Windows.RoutedEventArgs e)
    {
        ShowFromTray();
    }

    private void Show_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        ShowFromTray();
    }

    private void Exit_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        _reallyClose = true;
        Close();
    }

    /// <summary>
    /// 从托盘唤醒窗口: Win32 SetWindowPos 一次性置顶, 避免 WPF 属性 setter 来回切换造成的闪烁
    /// </summary>
    private void ShowFromTray()
    {
        if (WindowState == WindowState.Minimized)
            WindowState = WindowState.Normal;
        Show();

        // 1. 一次性 Win32 置顶 (不改变位置/大小, 同时触发显示)
        var hwnd = new WindowInteropHelper(this).Handle;
        SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0,
            SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW);

        // 2. 同步 WPF 的 Topmost 属性, 后续逻辑一致
        Topmost = true;

        // 3. 激活窗口
        Activate();
        Focus();
    }

    private void AutoStart_Click(object sender, RoutedEventArgs e)
    {
        // MenuItem 的 Click 在 IsChecked 变化之后触发, 此时 IsChecked 已经是新值
        bool newState = AutoStartMenu.IsChecked;
        try
        {
            AutoStartService.SetEnabled(newState);
        }
        catch (Exception ex)
        {
            // 写注册表失败, 回滚 UI 状态
            AutoStartMenu.IsChecked = !newState;
            MessageBox.Show($"设置开机启动失败: {ex.Message}", "错误",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
