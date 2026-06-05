using System.Runtime.InteropServices;
using System.Windows;
using TempWidget.Services;
using Application = System.Windows.Application;

namespace TempWidget;

public partial class App : Application
{
    private SingleInstance? _instance;
    private MainWindow? _mainWindow;

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

    public App()
    {
        DispatcherUnhandledException += (s, e) => { e.Handled = true; };
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _instance = new SingleInstance();
        if (!_instance.TryAcquire())
        {
            // 已有实例, 找到它的窗口并激活
            ActivateExistingInstance();
            Shutdown();
            return;
        }

        // 第一个实例: 创建主窗口
        _mainWindow = new MainWindow();
        MainWindow = _mainWindow;
        _mainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _instance?.Dispose();
        base.OnExit(e);
    }

    private void ActivateExistingInstance()
    {
        // 找已存在实例的窗口并置前
        var hwnd = FindWindow(null, "温度小窗");
        if (hwnd != IntPtr.Zero)
        {
            SetForegroundWindow(hwnd);
        }
    }
}
