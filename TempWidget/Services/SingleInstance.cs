using System.Threading;
using System.Windows.Threading;

namespace TempWidget.Services;

/// <summary>
/// 单实例锁 + 跨实例唤醒
///  - Mutex: 抢锁判断是不是第一个实例
///  - EventWaitHandle: 命名事件, 新实例 Set 一下, 旧实例收到信号后回调 WakeUpRequested
/// 旧窗口即使 Hide() 到托盘, 也能被新实例唤醒 (FindWindow 找不到隐藏窗口, 这是更可靠的方案)
/// </summary>
public class SingleInstance : IDisposable
{
    // 不带 "Global\\" 前缀 = Local 命名空间, 普通 WPF 进程可创建, 不需要 admin
    private const string MutexName = "TempWidget_SingleInstance_Mutex_v2";
    private const string EventName = "TempWidget_SingleInstance_WakeUp_v2";

    private Mutex? _mutex;
    private EventWaitHandle? _signal;
    private Thread? _listenThread;
    private Dispatcher? _uiDispatcher;
    private volatile bool _disposed;

    public bool IsOwner { get; private set; }

    /// <summary>
    /// 旧实例收到新实例的唤醒信号时触发 (已在 UI 线程)
    /// App 端订阅此事件 → 调 MainWindow.ShowFromTray()
    /// </summary>
    public event Action? WakeUpRequested;

    /// <summary>
    /// 主线程 Dispatcher 注入进来, 监听线程收到信号时用它把回调切回 UI 线程
    /// </summary>
    public void SetUIDispatcher(Dispatcher dispatcher) => _uiDispatcher = dispatcher;

    public bool TryAcquire()
    {
        _mutex = new Mutex(initiallyOwned: true, MutexName, out bool createdNew);
        IsOwner = createdNew;

        if (createdNew)
        {
            // 第一个实例: 启动后台监听线程, 等新实例发信号
            try
            {
                _signal = new EventWaitHandle(false, EventResetMode.AutoReset, EventName);
            }
            catch
            {
                // 名字已存在但 Mutex 是新的 (极少), 退化为不监听
                _signal = null;
            }

            if (_signal != null)
            {
                _listenThread = new Thread(ListenLoop)
                {
                    IsBackground = true,
                    Name = "TempWidget.SingleInstance.WakeUp"
                };
                _listenThread.Start();
            }
        }
        else
        {
            // 第二个实例: 通知旧实例唤醒
            for (int i = 0; i < 10; i++)
            {
                try
                {
                    using var existing = EventWaitHandle.OpenExisting(EventName);
                    existing.Set();
                    break;
                }
                catch (WaitHandleCannotBeOpenedException)
                {
                    // 旧实例可能还在创建命名事件, 短暂重试几次
                    Thread.Sleep(20);
                }
                catch
                {
                    // 旧实例没在监听 (可能刚退出), 忽略
                    break;
                }
            }
        }

        return createdNew;
    }

    private void ListenLoop()
    {
        while (!_disposed)
        {
            try
            {
                _signal?.WaitOne();   // 阻塞等信号
            }
            catch
            {
                break;  // EventWaitHandle 被 Dispose, 退出
            }

            if (_disposed) break;

            // 跨线程: 把回调切回 UI 线程, 订阅方可以安全操作 WPF 控件
            try
            {
                _uiDispatcher?.BeginInvoke(() =>
                {
                    if (!_disposed) WakeUpRequested?.Invoke();
                });
            }
            catch
            {
                // Dispatcher 已关闭, 退出监听
                break;
            }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        // 1. 唤醒监听线程让它退出
        try { _signal?.Set(); } catch { }
        _listenThread?.Join(500);
        _listenThread = null;

        // 2. 释放 Mutex (仅 owner)
        if (_mutex != null)
        {
            try
            {
                if (IsOwner) _mutex.ReleaseMutex();
            }
            catch { /* 释放失败不影响 */ }
            _mutex.Dispose();
            _mutex = null;
        }

        // 3. 释放事件
        try { _signal?.Dispose(); } catch { }
        _signal = null;
    }
}
