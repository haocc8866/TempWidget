using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TempWidget.Services;

/// <summary>
/// 用户配置 (实时持久化)
/// 默认值: 竖版 + 3秒刷新 + 位置未设置(由 Window_Loaded 兜底到屏幕右上角)
/// </summary>
public class AppConfig
{
    /// <summary>窗口 X 坐标 (DIP), -1 = 未保存, 由 Window_Loaded 决定兜底位置</summary>
    public double? Left { get; set; }
    /// <summary>窗口 Y 坐标 (DIP), -1 = 未保存</summary>
    public double? Top { get; set; }
    /// <summary>横版/竖版 (默认竖版)</summary>
    public bool IsHorizontal { get; set; } = false;
    /// <summary>刷新间隔 (秒), 1/3/6 (默认 3 秒, 平衡实时性和 CPU 占用)</summary>
    public int RefreshIntervalSeconds { get; set; } = 3;
}

/// <summary>
/// 配置读写: %LOCALAPPDATA%\TempWidget\config.json
/// 特性:
///  - 文件不存在 → 自动写入默认配置 + 返回默认
///  - 原子替换写入: .tmp + File.Move overwrite
///  - 线程安全: 单一 static lock 保护
///  - 静默失败: IO/JSON 错误不抛, 返默认值
/// </summary>
public static class ConfigService
{
    /// <summary>所有 IO 操作的互斥锁</summary>
    private static readonly object _ioLock = new();

    private static readonly string ConfigDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "TempWidget");

    private static readonly string ConfigPath = Path.Combine(ConfigDir, "config.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>
    /// 加载配置:
    ///  - 文件不存在 → 自动创建默认文件, 返回默认配置
    ///  - 文件存在但 JSON 损坏 / IO 错误 → 返回默认配置 (不抛)
    ///  - 线程安全
    /// </summary>
    public static AppConfig Load()
    {
        lock (_ioLock)
        {
            if (!File.Exists(ConfigPath))
            {
                var defaults = new AppConfig();
                TryWriteInternal(defaults);   // 自动落盘, 下次启动直接读
                return defaults;
            }

            try
            {
                var json = File.ReadAllText(ConfigPath);
                var cfg = JsonSerializer.Deserialize<AppConfig>(json, JsonOptions);
                return cfg ?? new AppConfig();
            }
            catch
            {
                // TODO: log 损坏原因
                return new AppConfig();
            }
        }
    }

    /// <summary>
    /// 立即保存配置到磁盘
    /// 线程安全; 失败静默
    /// </summary>
    public static void Save(AppConfig config)
    {
        lock (_ioLock)
        {
            TryWriteInternal(config);
        }
    }

    /// <summary>
    /// 实际写入: 必须在 lock 内调用
    /// 先写 .tmp, 再 File.Move 原子替换, 防中途崩溃损坏 config.json
    /// </summary>
    private static void TryWriteInternal(AppConfig config)
    {
        try
        {
            Directory.CreateDirectory(ConfigDir);
            var tmp = ConfigPath + ".tmp";
            var json = JsonSerializer.Serialize(config, JsonOptions);
            File.WriteAllText(tmp, json);
            File.Move(tmp, ConfigPath, overwrite: true);
        }
        catch
        {
            // TODO: 记录失败原因到日志
        }
    }
}
