using System.IO;
using System.Windows;
using TempWidget.Services;
using Application = System.Windows.Application;

namespace TempWidget;

public partial class App : Application
{
    private SingleInstance? _instance;
    private MainWindow? _mainWindow;

    public App()
    {
        // 1. UI 线程未捕获异常 (Dispatcher 调度)
        DispatcherUnhandledException += (s, e) =>
        {
            LogUnhandled("[UI]", e.Exception);
            e.Handled = true;
        };

        // 2. 非 UI 线程未捕获异常 (后台 Thread / ThreadPool)
        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
        {
            if (e.ExceptionObject is Exception ex)
                LogUnhandled("[AppDomain]", ex);
            // 这里 e.Handled = true 无效, AppDomain 异常无法阻止退出
            // 但日志已落盘, 至少能排查
        };

        // 3. Task 异步任务未观察到的异常
        TaskScheduler.UnobservedTaskException += (s, e) =>
        {
            LogUnhandled("[Task]", e.Exception);
            e.SetObserved();  // 阻止进程崩溃
        };
    }

    /// <summary>
    /// 统一异常日志: %LOCALAPPDATA%\TempWidget\unhandled.log
    /// 所有三个 handler 共用一个写入函数, 避免重复代码
    /// </summary>
    private static void LogUnhandled(string source, Exception ex)
    {
        try
        {
            var logDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "TempWidget");
            Directory.CreateDirectory(logDir);
            var logPath = Path.Combine(logDir, "unhandled.log");
            File.AppendAllText(logPath,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {source} {ex}\r\n\r\n");
        }
        catch
        {
            // 如果连日志都写不进去, 也不要再抛第二次异常
        }
    }

    /// <summary>
    /// 启动诊断: 写入当前状态 (不受异常影响, 用于排查 "启动后样式不对" 等问题)
    /// </summary>
    public static void LogStartup(string message)
    {
        try
        {
            var logDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "TempWidget");
            Directory.CreateDirectory(logDir);
            var logPath = Path.Combine(logDir, "startup.log");
            File.AppendAllText(logPath,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}\r\n");
        }
        catch { }
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _instance = new SingleInstance();
        _instance.SetUIDispatcher(Dispatcher);
        _instance.WakeUpRequested += OnWakeUpRequested;

        if (!_instance.TryAcquire())
        {
            // 已有实例: SingleInstance 内部已向旧实例发送了唤醒信号, 这里直接退出
            Shutdown();
            return;
        }

        // 创建主窗口
        _mainWindow = new MainWindow();
        MainWindow = _mainWindow;
        _mainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _instance?.Dispose();
        base.OnExit(e);
    }

    /// <summary>
    /// 旧实例收到新实例发来的唤醒信号 → 切回 UI 线程 → 把窗口从托盘拉出来
    /// </summary>
    private void OnWakeUpRequested()
    {
        if (_mainWindow is null) return;
        _mainWindow.ShowFromTray();
    }
}
