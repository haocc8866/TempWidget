using System.Threading;

namespace TempWidget.Services;

/// <summary>
/// 单实例锁: 用全局命名 Mutex 保证 TempWidget 只跑一个进程
/// </summary>
public class SingleInstance : IDisposable
{
    private Mutex? _mutex;
    private const string MutexName = "Global\\TempWidget_SingleInstance_v1";

    /// <summary>当前进程是否是 mutex 的持有者 (即第一个启动的实例)</summary>
    public bool IsOwner { get; private set; }

    public bool TryAcquire()
    {
        _mutex = new Mutex(initiallyOwned: true, MutexName, out bool createdNew);
        IsOwner = createdNew;
        return createdNew;
    }

    public void Dispose()
    {
        if (_mutex != null)
        {
            try
            {
                if (IsOwner) _mutex.ReleaseMutex();
            }
            catch
            {
                // 释放失败不影响
            }
            _mutex.Dispose();
            _mutex = null;
        }
    }
}
