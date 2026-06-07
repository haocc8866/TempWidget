using System.Diagnostics;
using Microsoft.Win32;

namespace TempWidget.Services;

/// <summary>
/// 开机自启: 用 Task Scheduler 创建 onlogon 任务 (Run with highest privileges)
/// 解决 "HKCU Run 键 + requireAdministrator manifest = 登录静默失败" 的问题
/// 登录时静默以 admin 权限起, 不弹 UAC
/// </summary>
public static class AutoStartService
{
    private const string TaskName = "TempWidget";
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string LegacyValueName = "TempWidget";

    public static bool IsEnabled
    {
        get
        {
            // 1. 主判定: Task Scheduler 任务存在
            if (TaskExists()) return true;
            // 2. 兼容旧版本: HKCU Run 键还有值也算开启 (会失败但应该清理)
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
            return key?.GetValue(LegacyValueName) != null;
        }
    }

    public static void SetEnabled(bool enabled)
    {
        if (enabled)
        {
            var exe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exe))
                throw new InvalidOperationException("无法获取当前 exe 路径");

            // 1. 先清掉旧 HKCU Run 键 (兼容旧版本)
            CleanupLegacyRunKey();

            // 2. 创建/覆盖 Task Scheduler 任务
            // /sc onlogon   登录触发
            // /rl highest   以最高权限运行 (admin) - 关键参数
            // /f            强制覆盖
            // /tr "path"    任务执行路径, 用双引号包整段防路径含空格/中文被截断
            var args = $"/create /tn \"{TaskName}\" /tr \"{exe}\" /sc onlogon /rl highest /f";
            var (exit, err) = RunSchtasks(args);
            if (exit != 0)
                throw new InvalidOperationException($"schtasks /create 失败 (exit {exit}): {err}");
        }
        else
        {
            // 1. 删 Task Scheduler 任务 (忽略 NotFound)
            RunSchtasks($"/delete /tn \"{TaskName}\" /f");

            // 2. 顺手清掉旧 HKCU Run 键
            CleanupLegacyRunKey();
        }
    }

    private static bool TaskExists()
    {
        var (exit, _) = RunSchtasks($"/query /tn \"{TaskName}\"");
        return exit == 0;
    }

    private static (int exitCode, string stderr) RunSchtasks(string arguments)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "schtasks.exe",
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var p = Process.Start(psi)!;
            // 读 stderr (中文 Windows 错误在这里)
            string err = p.StandardError.ReadToEnd();
            p.WaitForExit(5000);
            return (p.ExitCode, err);
        }
        catch (Exception ex)
        {
            return (-1, ex.Message);
        }
    }

    private static void CleanupLegacyRunKey()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            if (key?.GetValue(LegacyValueName) != null)
                key.DeleteValue(LegacyValueName, throwOnMissingValue: false);
        }
        catch
        {
            // 清理失败不影响主流程
        }
    }
}
