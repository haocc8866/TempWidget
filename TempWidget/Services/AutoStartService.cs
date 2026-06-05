using Microsoft.Win32;

namespace TempWidget.Services;

/// <summary>
/// 开机自启: 写 HKCU\...\Run 注册表项 (不需要 admin)
/// </summary>
public static class AutoStartService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "TempWidget";

    public static bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
            return key?.GetValue(ValueName) != null;
        }
    }

    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
        if (key == null)
            throw new InvalidOperationException("无法打开 Run 注册表项");

        if (enabled)
        {
            // 用 Environment.ProcessPath 而不是 Process.MainModule.FileName
            // 后者在 .NET Core+ 有时返回空字符串
            var exe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exe))
                throw new InvalidOperationException("无法获取当前 exe 路径");

            // 加上引号防止路径含空格
            key.SetValue(ValueName, $"\"{exe}\"");
        }
        else
        {
            if (key.GetValue(ValueName) != null)
                key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }
}
