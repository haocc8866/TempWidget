using System.IO;
using System.Windows.Threading;
using LibreHardwareMonitor.Hardware;
using TempWidget.Models;

namespace TempWidget.Services;

/// <summary>
/// 温度采集服务: 2 秒一次扫描所有 sensor, 定位 CPU/GPU 核心温度, 推送事件
/// </summary>
public class TemperatureService : IDisposable
{
    private readonly Computer _computer;
    private DispatcherTimer? _timer;
    private ISensor? _cpuSensor;
    private ISensor? _gpuSensor;
    private bool _disposed;

    public event Action<TempReading>? Updated;

    public TemperatureService()
    {
        _computer = new Computer
        {
            IsCpuEnabled = true,
            IsGpuEnabled = true,
            IsMotherboardEnabled = true,
            IsControllerEnabled = true,
            IsStorageEnabled = false,
            IsNetworkEnabled = false,
            IsMemoryEnabled = false,
        };
    }

    public void Start()
    {
        _computer.Open();
        LocateSensors();

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _timer.Tick += (_, _) => Tick();
        _timer.Start();

        Tick(); // 立即跑一次
    }

    private void LocateSensors()
    {
        foreach (var hw in _computer.Hardware)
        {
            hw.Update();

            if (_cpuSensor is null && IsCpu(hw))
                _cpuSensor = FindBestTemperatureSensor(hw, "CPU", CpuPriorityKeywords);

            if (_gpuSensor is null && IsGpu(hw))
                _gpuSensor = FindBestTemperatureSensor(hw, "GPU", GpuPriorityKeywords);
        }

        DumpSensorsIfMissing();
    }

    /// <summary>
    /// 在一个 hardware 上找最佳温度 sensor:
    /// 1. 按优先级关键词匹配
    /// 2. 兜底: 所有 Core* 中最大的
    /// 3. 最后兜底: 第一个有值的 Temperature sensor
    /// </summary>
    private ISensor? FindBestTemperatureSensor(IHardware hw, string hwLabel, string[] priorityKeywords)
    {
        var temps = hw.Sensors
            .Where(s => s.SensorType == SensorType.Temperature)
            .ToList();

        if (temps.Count == 0) return null;

        // 1. 优先级关键词
        foreach (var keyword in priorityKeywords)
        {
            var s = temps.FirstOrDefault(x =>
                x.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase));
            if (s != null) return s;
        }

        // 2. Core 系列里取最大值 (CPU/GPU 多核平均或最大)
        var cores = temps
            .Where(x => x.Name.StartsWith("Core", StringComparison.OrdinalIgnoreCase) ||
                        x.Name.StartsWith("CCD", StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (cores.Count > 0)
        {
            return cores.OrderByDescending(x => x.Value ?? float.MinValue).First();
        }

        // 3. 第一个
        return temps.First();
    }

    private static readonly string[] CpuPriorityKeywords =
    {
        "Package",      // Intel/AMD 通用
        "Tdie",         // AMD Ryzen
        "Tctl",         // AMD Ryzen
        "CPU Package",  // Intel
        "CCD1",         // AMD CCD
        "CCDS",         // AMD CCD sum
        "Core Max",     // 一些软件命名
    };

    private static readonly string[] GpuPriorityKeywords =
    {
        "GPU Core",     // NVIDIA
        "Edge",         // AMD
        "GPU Temperature", // 通用
        "Hot Spot",     // AMD hotspot
    };

    private static bool IsCpu(IHardware hw) =>
        hw.HardwareType == HardwareType.Cpu ||
        hw.Name.Contains("CPU", StringComparison.OrdinalIgnoreCase) ||
        hw.Name.Contains("Intel", StringComparison.OrdinalIgnoreCase) ||
        hw.Name.Contains("Ryzen", StringComparison.OrdinalIgnoreCase) ||
        hw.Name.Contains("AMD", StringComparison.OrdinalIgnoreCase);

    private static bool IsGpu(IHardware hw) =>
        hw.HardwareType == HardwareType.GpuNvidia ||
        hw.HardwareType == HardwareType.GpuAmd ||
        hw.HardwareType == HardwareType.GpuIntel ||
        hw.Name.Contains("GPU", StringComparison.OrdinalIgnoreCase) ||
        hw.Name.Contains("GeForce", StringComparison.OrdinalIgnoreCase) ||
        hw.Name.Contains("Radeon", StringComparison.OrdinalIgnoreCase) ||
        hw.Name.Contains("Arc", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 调试用: 找不到目标 sensor 时把所有 sensor 名字 dump 到文件, 方便用户反馈
    /// </summary>
    private void DumpSensorsIfMissing()
    {
        if (_cpuSensor is not null && _gpuSensor is not null) return;

        try
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"Dump @ {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"CPU sensor found: {_cpuSensor?.Name ?? "(none)"}");
            sb.AppendLine($"GPU sensor found: {_gpuSensor?.Name ?? "(none)"}");
            sb.AppendLine();
            sb.AppendLine("All hardware & temperature sensors:");

            foreach (var hw in _computer.Hardware)
            {
                sb.AppendLine($"  [{hw.HardwareType}] {hw.Name}");
                foreach (var s in hw.Sensors.Where(s => s.SensorType == SensorType.Temperature))
                {
                    sb.AppendLine($"    - {s.Name} = {s.Value}°C");
                }
            }

            var path = Path.Combine(Path.GetTempPath(), "TempWidget_sensors.txt");
            File.WriteAllText(path, sb.ToString());
        }
        catch
        {
            // dump 失败不影响主流程
        }
    }

    private void Tick()
    {
        if (_disposed) return;

        try
        {
            // 重新定位 (例如热插拔)
            if (_cpuSensor is null || _gpuSensor is null)
                LocateSensors();

            float? cpu = null, gpu = null;

            if (_cpuSensor is not null)
            {
                _cpuSensor.Hardware.Update();
                cpu = _cpuSensor.Value;
            }

            if (_gpuSensor is not null)
            {
                _gpuSensor.Hardware.Update();
                gpu = _gpuSensor.Value;
            }

            Updated?.Invoke(new TempReading(cpu, gpu, DateTime.Now));
        }
        catch
        {
            // 静默失败, 下个 tick 继续
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _timer?.Stop();
        _computer.Close();
    }
}
