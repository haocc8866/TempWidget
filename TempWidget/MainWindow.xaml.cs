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
using MenuItem = System.Windows.Controls.MenuItem;

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

    // 横竖版尺寸常量 (必须与 XAML 里的 Window Height/Width 保持一致, 否则切换时窗口被压变形)
    private const double VerticalWidth = 50;
    private const double VerticalHeight = 110;
    private const double HorizontalWidth = 160;
    private const double HorizontalHeight = 50;

    public MainWindow()
    {
        InitializeComponent();
        // 兜底: 构造函数里先用 16x16 默认图标, 保证启动就一定能看到托盘
        Tray.IconSource = CreateTrayIcon(16);
    }

    /// <summary>
    /// 生成托盘图标: 圆角背景 + 中央 T 字
    ///  - 100% DPI → 16 像素
    ///  - 150% DPI → 24 像素
    ///  - 200% DPI → 32 像素 (4K 屏)
    /// </summary>
    private ImageSource CreateTrayIcon(double sizeDip = 16)
    {
        double dpiScale = 1.0;
        try
        {
            var dpi = VisualTreeHelper.GetDpi(this);
            if (dpi.PixelsPerDip > 0) dpiScale = dpi.PixelsPerDip;
        }
        catch
        {
            dpiScale = 1.0;
        }

        int size = Math.Max(16, (int)Math.Round(sizeDip * dpiScale));
        double cornerRadius = 3 * dpiScale;
        double fontSize = 11 * dpiScale;

        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            // 圆角青蓝色背景
            dc.DrawRoundedRectangle(
                new SolidColorBrush(Color.FromRgb(96, 196, 230)),
                null,
                new Rect(0, 0, size, size),
                cornerRadius, cornerRadius);

            // 中央 "T" 字 (代表 Temperature)
            var ft = new FormattedText("T",
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                new Typeface(new FontFamily("Arial"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal),
                fontSize,
                Brushes.White,
                pixelsPerDip: dpiScale);
            dc.DrawText(ft, new Point(((size - ft.Width) / 2), ((size - ft.Height) / 2)));
        }

        var rtb = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(visual);
        rtb.Freeze();
        return rtb;
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        // 1. 读取保存的配置
        var config = ConfigService.Load();
        App.LogStartup(
            $"Loaded config: Left={config.Left}, Top={config.Top}, " +
            $"IsHorizontal={config.IsHorizontal}, RefreshSec={config.RefreshIntervalSeconds}");

        // 2. 创建 ViewModel 并应用样式/刷新时间 (这俩必须在 Loaded 之前, 否则会触发 UI 事件)
        _vm = new MainViewModel
        {
            IsHorizontal = config.IsHorizontal,
            RefreshIntervalSeconds = config.RefreshIntervalSeconds
        };
        DataContext = _vm;

        // 3. 订阅 ViewModel 变化 → 实时持久化
        _vm.PropertyChanged += OnViewModelPropertyChanged;

        // 4. 恢复位置 + 多屏校验
        // 关键: minVisibleW/H 必须远小于窗口实际宽高, 否则窄窗口永远过不了校验永远走兜底
        if (config.Left.HasValue && config.Top.HasValue &&
            ScreenHelper.IsRectVisible(config.Left.Value, config.Top.Value, ActualWidth, ActualHeight, this,
                minVisibleW: 20, minVisibleH: 20))
        {
            Left = config.Left.Value;
            Top = config.Top.Value;
        }
        else
        {
            // 兜底: 屏幕右上角
            var work = SystemParameters.WorkArea;
            Left = work.Right - ActualWidth - 16;
            Top = work.Top + 16;
        }

        // 4.5 关键: 同步窗口尺寸到当前布局 (修复 "框是竖版但显示横版布局" 的 bug)
        // XAML 里的 Width/Height 写的是 70×110 (竖版), 但配置可能是横版 → 启动时要立即同步
        SyncWindowSizeToLayout();

        // 5. Window 已加载, 此时 DPI 准确, 升级到高 DPI 清晰版图标
        try
        {
            var dpi = VisualTreeHelper.GetDpi(this);
            if (dpi.PixelsPerDip > 1.0)
            {
                Tray.IconSource = CreateTrayIcon(16);
            }
        }
        catch (Exception ex)
        {
            try
            {
                var logPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "TempWidget_tray_error.txt");
                System.IO.File.WriteAllText(logPath, $"[{DateTime.Now}] {ex}\n");
            }
            catch { }
        }

        // 6. 同步托盘菜单的开机启动状态
        try { AutoStartMenu.IsChecked = AutoStartService.IsEnabled; }
        catch { /* 注册表读不到, 默认 false */ }

        // 7. 同步刷新时间菜单 (单选)
        SyncRefreshIntervalMenu();
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (_vm is null) return;
        // 实时保存: 样式 / 刷新时间变化时立即写盘
        if (e.PropertyName == nameof(MainViewModel.IsHorizontal) ||
            e.PropertyName == nameof(MainViewModel.RefreshIntervalSeconds))
        {
            SaveConfig();
        }
    }

    /// <summary>
    /// 立即把当前样式 + 刷新时间写入配置
    /// </summary>
    private void SaveConfig()
    {
        if (_vm is null) return;
        var cfg = ConfigService.Load();
        cfg.IsHorizontal = _vm.IsHorizontal;
        cfg.RefreshIntervalSeconds = _vm.RefreshIntervalSeconds;
        ConfigService.Save(cfg);
    }

    /// <summary>
    /// 立即把当前窗口位置写入配置 (动画结束后调用)
    /// </summary>
    private void SaveWindowPosition()
    {
        var cfg = ConfigService.Load();
        cfg.Left = Left;
        cfg.Top = Top;
        ConfigService.Save(cfg);
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
    /// 动画结束后实时保存位置到配置
    /// </summary>
    private void SnapToEdge()
    {
        const double threshold = 25;
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

        if (snapped)
        {
            // 启动动画, 等左右/上下动画都结束后再落盘, 避免写入中间态
            int pending = 0;
            void OnCompleted(object? _, EventArgs __)
            {
                pending--;
                if (pending <= 0)
                    SaveWindowPosition();
            }

            var animLeft = AnimateLeftTo(newLeft);
            if (animLeft != null)
            {
                pending++;
                animLeft.Completed += OnCompleted;
            }

            var animTop = AnimateTopTo(newTop);
            if (animTop != null)
            {
                pending++;
                animTop.Completed += OnCompleted;
            }

            if (pending == 0)
                SaveWindowPosition();
        }
        else
        {
            // 没吸附, 但位置可能变过 (DragMove 后), 也保存一次
            SaveWindowPosition();
        }
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
        AnimateLeftTo(targetLeft);
        AnimateTopTo(targetTop);
    }

    private DoubleAnimation? AnimateLeftTo(double targetLeft)
    {
        if (Math.Abs(Left - targetLeft) < 0.1) return null;
        const double durationMs = 200;
        var anim = new DoubleAnimation(Left, targetLeft, TimeSpan.FromMilliseconds(durationMs))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };
        BeginAnimation(LeftProperty, anim);
        return anim;
    }

    private DoubleAnimation? AnimateTopTo(double targetTop)
    {
        if (Math.Abs(Top - targetTop) < 0.1) return null;
        const double durationMs = 200;
        var anim = new DoubleAnimation(Top, targetTop, TimeSpan.FromMilliseconds(durationMs))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };
        BeginAnimation(TopProperty, anim);
        return anim;
    }

    /// <summary>
    /// 平滑调整窗口大小 (横竖版旋转动画), 300ms Quadratic EaseInOut
    /// onCompleted: 全部动画完成后回调, 用于动画后吸边等操作
    /// </summary>
    private void AnimateSize(double targetW, double targetH, double? targetLeft, Action? onCompleted = null)
    {
        // 1. 停掉所有正在跑的动画
        BeginAnimation(WidthProperty, null);
        BeginAnimation(HeightProperty, null);
        BeginAnimation(LeftProperty, null);
        BeginAnimation(TopProperty, null);

        // 2. 关键修复: 强制 layout pass + 同步依赖属性
        //    BeginAnimation(null) 后依赖属性行为不一致: Width 回 base, Height 可能保持动画中值
        //    ActualWidth/ActualHeight 也未必立即更新
        //    必须先 UpdateLayout() 同步, 再赋值, 启动新动画 from 才能准
        UpdateLayout();
        Width = ActualWidth;
        Height = ActualHeight;

        const double durationMs = 300;
        var easing = new QuadraticEase { EasingMode = EasingMode.EaseInOut };

        int pending = 0;
        void CheckDone() { if (--pending == 0) onCompleted?.Invoke(); }

        // 如果传了 targetLeft, 同步动画移动 Left (防越界)
        if (targetLeft.HasValue && Math.Abs(targetLeft.Value - Left) > 0.1)
        {
            pending++;
            var animLeft = new DoubleAnimation(Left, targetLeft.Value, TimeSpan.FromMilliseconds(durationMs)) { EasingFunction = easing };
            animLeft.Completed += (_, _) => CheckDone();
            BeginAnimation(LeftProperty, animLeft);
        }

        // 关键: from 用 Width/Height (依赖属性) 而不是 ActualWidth/ActualHeight
        // 因为 ActualWidth 反映的是当前 layout 后的值, 而 Width 是依赖属性 (动画基值)
        // 我们刚把 Width 同步到 ActualWidth, 现在 Width 就是可靠的"当前真实值"
        if (Math.Abs(Width - targetW) > 0.5)
        {
            pending++;
            var animWidth = new DoubleAnimation(Width, targetW, TimeSpan.FromMilliseconds(durationMs)) { EasingFunction = easing };
            animWidth.Completed += (_, _) => CheckDone();
            BeginAnimation(WidthProperty, animWidth);
        }

        if (Math.Abs(Height - targetH) > 0.5)
        {
            pending++;
            var animHeight = new DoubleAnimation(Height, targetH, TimeSpan.FromMilliseconds(durationMs)) { EasingFunction = easing };
            animHeight.Completed += (_, _) => CheckDone();
            BeginAnimation(HeightProperty, animHeight);
        }

        // 距离太近没启动任何动画, 立即回调
        if (pending == 0) onCompleted?.Invoke();
    }

    /// <summary>
    /// 启动时立即同步窗口尺寸到当前布局 (无动画, 避免闪烁)
    /// 修 "框是竖版尺寸但显示横版布局" 的 bug
    /// </summary>
    private void SyncWindowSizeToLayout()
    {
        if (_vm is null) return;

        double targetW = _vm.IsHorizontal ? HorizontalWidth : VerticalWidth;
        double targetH = _vm.IsHorizontal ? HorizontalHeight : VerticalHeight;

        // 停掉所有正在跑的尺寸/位置动画, 直接设值
        BeginAnimation(WidthProperty, null);
        BeginAnimation(HeightProperty, null);
        BeginAnimation(LeftProperty, null);
        BeginAnimation(TopProperty, null);

        Width = targetW;
        Height = targetH;
    }

    /// <summary>
    /// 托盘菜单: 切换横/竖版
    /// 关键修复: 切换前计算目标 Left, 避免窗口右边越界溢出屏幕
    /// </summary>
    private void OrientationMenu_Click(object sender, RoutedEventArgs e)
    {
        if (_vm is null) return;

        bool toHorizontal = !_vm.IsHorizontal;
        _vm.IsHorizontal = toHorizontal;

        double targetW = toHorizontal ? HorizontalWidth : VerticalWidth;
        double targetH = toHorizontal ? HorizontalHeight : VerticalHeight;

        // 防越界: 如果当前窗口右边已经接近屏幕右边界, 且切换后窗口变宽,
        // 必须把 Left 也向左平移, 否则 Width 增长时会先溢出到屏幕外
        double? targetLeft = null;
        var area = GetWorkAreaInDip();
        double projectedRight = Left + targetW;

        if (projectedRight > area.Right + 0.5)
        {
            // 扩展后越界 → 同步把 Left 左移, 让右边贴到工作区右边界
            targetLeft = Math.Max(area.Left, area.Right - targetW);
        }

        if (toHorizontal)
            OrientationMenu.Header = "切换为竖版样式";
        else
            OrientationMenu.Header = "切换为横版样式";

        AnimateSize(targetW, targetH, targetLeft, SnapToEdge);

        // 实时保存: 样式 (ViewModel 的 IsHorizontal setter 已经触发 PropertyChanged,
        //          Window_Loaded 里订阅的 OnViewModelPropertyChanged 会自动 SaveConfig)

        // 旋转后会在尺寸动画结束时自动吸一次边, 避免在中途拿到错误的 ActualWidth/ActualHeight
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

        // 真的退出: 最后保存一次最新位置 (兜底, 防止拖动后没等动画完成就退出)
        // 主动停掉所有动画, 强制把 Left/Top 同步到基值
        BeginAnimation(LeftProperty, null);
        BeginAnimation(TopProperty, null);
        SaveWindowPosition();
        SaveConfig();

        // 释放资源
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
    /// internal 让 App.xaml.cs 的单实例唤醒回调也能调用
    /// </summary>
    internal void ShowFromTray()
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

    /// <summary>
    /// 托盘子菜单: 选择温度刷新间隔 (1/3/6 秒)
    /// </summary>
    private void RefreshInterval_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem item || item.Tag is not string s) return;
        if (!int.TryParse(s, out int seconds)) return;
        if (_vm is null) return;

        _vm.RefreshIntervalSeconds = seconds;
        SyncRefreshIntervalMenu();
    }

    /// <summary>
    /// 让 1/3/6 秒三个菜单项同步当前 RefreshIntervalSeconds (单选效果)
    /// </summary>
    private void SyncRefreshIntervalMenu()
    {
        if (_vm is null) return;
        Refresh1s.IsChecked = _vm.RefreshIntervalSeconds == 1;
        Refresh3s.IsChecked = _vm.RefreshIntervalSeconds == 3;
        Refresh6s.IsChecked = _vm.RefreshIntervalSeconds == 6;
    }

    /// <summary>
    /// 托盘菜单: 导出 sensor 列表到 %TEMP%\TempWidget_sensors.txt
    /// 排查 "温度一直是 —" 时用
    /// </summary>
    private void DumpSensors_Click(object sender, RoutedEventArgs e)
    {
        var path = _vm?.DumpSensors() ?? "";
        if (!string.IsNullOrEmpty(path))
        {
            MessageBox.Show(
                $"Sensor 列表已导出:\n{path}\n\n请把文件内容贴给我看 (CPU Package / GPU Core 是否找到)。",
                "诊断导出", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        else
        {
            MessageBox.Show("导出失败", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
