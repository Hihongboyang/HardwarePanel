namespace HardwarePanel;

internal static class Program
{
    private static Mutex? _singleInstance;

    [STAThread]
    private static void Main(string[] args)
    {
        _singleInstance = new Mutex(true, @"Global\HardwarePanel", out bool createdNew);
        if (!createdNew)
        {
            MessageBox.Show("HardwarePanel 已在运行（托盘中），双击托盘图标可打开。",
                "HardwarePanel", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        // --tray：开机自启用，直接进托盘不显示窗口
        bool startHidden = args.Contains("--tray", StringComparer.OrdinalIgnoreCase);
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new MainForm(startHidden));
    }
}
