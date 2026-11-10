using System.Diagnostics;

namespace HardwarePanel;

/// <summary>
/// 开机自启：用“登录时、最高权限运行”的计划任务实现。
/// 提权程序（requireAdministrator）放启动文件夹会被 UAC 拦截或每次弹框，
/// 计划任务是标准做法——登录时静默启动、免 UAC。
/// 程序自身已提权，schtasks 直接执行即成功。
/// </summary>
internal static class AutoStartHelper
{
    private const string TaskName = "HardwarePanel";

    public static bool IsEnabled() => Run("/Query /TN " + TaskName) == 0;

    public static bool Enable() => Run(
        $"/Create /TN {TaskName} /TR \"{Command}\" /SC ONLOGON /RL HIGHEST /F") == 0;

    public static bool Disable() => Run($"/Delete /TN {TaskName} /F") == 0;

    /// <summary>自启命令：exe 路径 + --tray（开机直接进托盘）。</summary>
    private static string Command => $"\"{ExePath()}\" --tray";

    private static string ExePath()
    {
        string? path = Environment.ProcessPath;
        if (path is not null && !path.EndsWith("dotnet.exe", StringComparison.OrdinalIgnoreCase))
            return path;

        // dotnet run 场景：ProcessPath 是 dotnet 宿主，改用构建出的 apphost
        string candidate = Path.Combine(AppContext.BaseDirectory, "HardwarePanel.exe");
        return File.Exists(candidate) ? candidate : (path ?? "");
    }

    private static int Run(string args)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo("schtasks.exe", args)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            if (p is null || !p.WaitForExit(10_000))
                return -1;
            return p.ExitCode;
        }
        catch
        {
            return -1;
        }
    }
}
