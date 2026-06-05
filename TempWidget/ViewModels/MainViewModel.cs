using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Threading;
using LibreHardwareMonitor.Hardware;

namespace TempWidget.ViewModels;

/// <summary>
/// 主窗口 ViewModel: 负责 LibreHardwareMonitor 初始化、1 秒轮询温度、INotifyPropertyChanged 推送给 UI
/// </summary>
public class MainViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly Computer _computer;
    private readonly DispatcherTimer _timer;
    private ISensor? _cpuSensor;
    private ISensor? _gpuSensor;
    private bool _disposed;

    // ----- 绑定属性 -----

    private float? _cpuTemp;
    /// <summary>CPU 温度 (°C), 读不到时为 null</summary>
    public float? CpuTemp
    {
        get => _cpuTemp;
        private set
        {
            if (_cpuTemp == value) return;
            _cpuTemp = value;
            OnPropertyChanged();
        }
    }

    private float? _gpuTemp;
    /// <summary>GPU 温度 (°C), 读不到时为 null</summary>
    public float? GpuTemp
    {
        get => _gpuTemp;
        private set
        {
            if (_gpuTemp == value) return;
            _gpuTemp = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(GpuTempPercent));
        }
    }

    /// <summary>CPU 温度映射到 0-100 进度条 (30°C→0%, 100°C→100%)</summary>
    public double CpuTempPercent =>
        CpuTemp is float t ? Math.Clamp((t - 30.0) / 70.0 * 100.0, 0, 100) : 0;

    /// <summary>GPU 温度映射到 0-100 进度条</summary>
    public double GpuTempPercent =>
        GpuTemp is float t ? Math.Clamp((t - 30.0) / 70.0 * 100.0, 0, 100) : 0;

    private bool _isHorizontal;
    /// <summary>true = 横版, false = 竖版</summary>
    public bool IsHorizontal
    {
        get => _isHorizontal;
        set
        {
            if (_isHorizontal == value) return;
            _isHorizontal = value;
            OnPropertyChanged();
        }
    }

    // ----- 构造与生命周期 -----

    public MainViewModel()
    {
        // 1) 初始化 LibreHardwareMonitor, 开启 CPU + GPU
        _computer = new Computer
        {
            IsCpuEnabled = true,
            IsGpuEnabled = true,
        };
        _computer.Open();

        // 2) 定位精确名称的 sensor
        LocateSensors();

        // 3) 1 秒周期 DispatcherTimer
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => Tick();
        _timer.Start();

        // 立即跑一次, 不必等满 1 秒
        Tick();
    }

    // ----- sensor 定位 -----

    private void LocateSensors()
    {
        foreach (var hw in _computer.Hardware)
        {
            try { hw.Update(); } catch { /* 单个硬件更新失败不影响其他 */ }

            if (_cpuSensor is null && IsCpuHardware(hw))
                _cpuSensor = FindTemperatureSensorByName(hw, "CPU Package");

            if (_gpuSensor is null && IsGpuHardware(hw))
                _gpuSensor = FindTemperatureSensorByName(hw, "GPU Core");
        }
    }

    /// <summary>
    /// 在一个 hardware 上精确匹配名字的 Temperature sensor (大小写不敏感)
    /// </summary>
    private static ISensor? FindTemperatureSensorByName(IHardware hw, string exactName)
    {
        return hw.Sensors.FirstOrDefault(s =>
            s.SensorType == SensorType.Temperature &&
            string.Equals(s.Name, exactName, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsCpuHardware(IHardware hw) =>
        hw.HardwareType == HardwareType.Cpu ||
        hw.Name.Contains("CPU", StringComparison.OrdinalIgnoreCase);

    private static bool IsGpuHardware(IHardware hw) =>
        hw.HardwareType == HardwareType.GpuNvidia ||
        hw.HardwareType == HardwareType.GpuAmd ||
        hw.HardwareType == HardwareType.GpuIntel ||
        hw.Name.Contains("GeForce", StringComparison.OrdinalIgnoreCase) ||
        hw.Name.Contains("Radeon", StringComparison.OrdinalIgnoreCase) ||
        hw.Name.Contains("Arc", StringComparison.OrdinalIgnoreCase);

    // ----- 轮询 -----

    private void Tick()
    {
        if (_disposed) return;

        try
        {
            // 热插拔兜底: 如果之前没找到, 再找一次
            if (_cpuSensor is null || _gpuSensor is null)
                LocateSensors();

            float? cpu = null, gpu = null;

            if (_cpuSensor is not null)
            {
                _cpuSensor.Hardware.Update();
                cpu = _cpuSensor.Value;  // 读不到时为 null
            }
            if (_gpuSensor is not null)
            {
                _gpuSensor.Hardware.Update();
                gpu = _gpuSensor.Value;
            }

            CpuTemp = cpu;
            GpuTemp = gpu;
            // 触发百分比属性通知 (CpuTemp setter 没传 cpu 时已经通知过, 这里保险再发一次)
            OnPropertyChanged(nameof(CpuTempPercent));
            OnPropertyChanged(nameof(GpuTempPercent));
        }
        catch
        {
            // 静默, 下一 tick 继续
        }
    }

    // ----- INotifyPropertyChanged -----

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    // ----- IDisposable -----

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _timer.Stop();
        _computer.Close();
    }
}
