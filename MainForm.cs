using System.Diagnostics;
using LibreHardwareMonitor.Hardware;

namespace HardwarePanel;

internal sealed class MainForm : Form
{
    private const int Port = 8080;

    private readonly Computer _computer = new()
    {
        IsCpuEnabled = true,        // CPU 温度 / 封装功耗 / 负载
        IsGpuEnabled = true,        // 独显（核显另有性能计数器伪行）
        IsMemoryEnabled = true,     // 内存使用量
        IsMotherboardEnabled = true,// SuperIO：主板温度 / 风扇 / 电压
        IsStorageEnabled = true,    // 磁盘温度 / 读写速率 / 寿命
        IsNetworkEnabled = true,    // 网卡上下行速率
    };
    private readonly VolumeLevel _volume = new();
    private readonly GpuCounters _gpu = new();
    private readonly SensorHub _hub = new();
    private readonly ListView _view = new()
    {
        Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, GridLines = true
    };
    private readonly Dictionary<string, ListViewItem> _items = []; // key: 硬件|传感器
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 1000 };
    private readonly NotifyIcon _tray;
    private bool _exiting;
    private bool _seeded;   // 首帧采集后是否已尝试生成默认 visible

    // 只要这些量：温度 / 负载 / 功耗 / 风扇 / 数据 / 频率 / 电压 / 速率 / 寿命
    // （SmallData 在 lib 0.9.7-pre782 中不存在；GPU 显存行在此版本为 Data，按 MB 显示）
    private static readonly HashSet<SensorType> Wanted =
    [
        SensorType.Temperature, SensorType.Load, SensorType.Power, SensorType.Fan,
        SensorType.Data, SensorType.Clock, SensorType.Voltage,
        SensorType.Throughput, SensorType.Level,
    ];

    private static readonly Dictionary<SensorType, string> Units = new()
    {
        [SensorType.Temperature] = "°C",
        [SensorType.Load] = "%",
        [SensorType.Power] = "W",
        [SensorType.Fan] = "RPM",
        [SensorType.Data] = "GB",
        [SensorType.Clock] = "MHz",
        [SensorType.Voltage] = "V",
        [SensorType.Current] = "A",
        [SensorType.Level] = "%",        // SSD 寿命
        [SensorType.Throughput] = "MB/s",// 会在 Collect 里换算
    };

    public MainForm(bool startHidden)
    {
        Text = "HardwarePanel";
        Width = 760; Height = 680;
        StartPosition = FormStartPosition.CenterScreen;

        var appIcon = LoadIcon();
        Icon = appIcon;

        _view.Columns.Add("硬件", 210);
        _view.Columns.Add("传感器", 260);
        _view.Columns.Add("数值", 90);
        _view.Columns.Add("单位", 60);
        Controls.Add(_view);

        _tray = new NotifyIcon
        {
            Icon = appIcon,
            Text = "HardwarePanel",
            Visible = true,
            ContextMenuStrip = BuildTrayMenu()
        };
        _tray.DoubleClick += (_, _) => ShowPanel();

        _computer.Open();
        try
        {
            _hub.Start(Port);
        }
        catch (Exception)
        {
            // 端口被占（常见：重复启动）：本机窗口照常，仅手机面板不可用
            _tray.ShowBalloonTip(5000, "HardwarePanel",
                $"端口 {Port} 被占用，手机面板不可用（本机窗口不受影响）", ToolTipIcon.Warning);
        }
        _timer.Tick += (_, _) => RefreshAll();
        _timer.Start();
        int firstCount = RefreshAll();
        if (firstCount == 0)
            _tray.ShowBalloonTip(5000, "HardwarePanel",
                "未读到任何传感器：WinRing0 驱动可能被杀毒软件拦截", ToolTipIcon.Warning);

        if (startHidden)
        {
            WindowState = FormWindowState.Minimized;
            ShowInTaskbar = false;
        }
    }

    private int RefreshAll()
    {
        var rows = new List<SensorRow>();

        foreach (IHardware hw in _computer.Hardware)
        {
            // 跳过 Virtual Memory(提交量)组:与物理内存组同名行(Memory/Memory Used)
            // 会在信息屏出现两个「内存使用率/已用内存」且数值不同,只保留物理内存
            if (hw.HardwareType == HardwareType.Memory &&
                hw.Name.Contains("Virtual", StringComparison.OrdinalIgnoreCase))
                continue;
            Collect(hw, rows);
            foreach (IHardware sub in hw.SubHardware) // 主板 SuperIO 在这里（风扇/电压）
                Collect(sub, rows, hw.Name);          // 行挂主板名，前端按主板分组
        }

        // 以下每个来源独立容错：任何一个读不到都只是少一行，不中断采集
        try
        {
            var (util, dedicated, shared) = _gpu.Read();
            rows.Add(new SensorRow("GPU 核显", "总使用率", "Load", Math.Round(util, 1), "%"));
            rows.Add(new SensorRow("GPU 核显", "专用显存", "Data", Math.Round(dedicated / 1024 / 1024, 1), "MB"));
            rows.Add(new SensorRow("GPU 核显", "共享内存", "Data", Math.Round(shared / 1024 / 1024, 1), "MB"));
        }
        catch { /* 核显计数器不可用：跳过 */ }

        rows.Add(new SensorRow("系统", "Uptime", "Data",
            Math.Round(TimeSpan.FromMilliseconds(Environment.TickCount64).TotalSeconds, 0), "s"));
        rows.Add(new SensorRow("系统", "Volume", "Load", _volume.Read(), "%"));


        Render(rows);
        _hub.Publish(rows);
        if (!_seeded)
        {
            _seeded = true;
            ConfigStore.SeedDefaultVisible(rows); // 首帧：新建的配置骨架填默认 visible
        }
        foreach (var (title, body) in ConfigStore.Alarms.Evaluate(ConfigStore.Current, rows))
            _hub.Notify(title, body, "alarm", isAlarm: true);
        return rows.Count;
    }

    /// <summary>单个硬件的采集独立容错：某个硬件/传感器读挂了只丢自己的行，不影响其它硬件。</summary>
    private static void Collect(IHardware hw, List<SensorRow> rows, string? nameOverride = null)
    {
        string name = nameOverride ?? hw.Name;
        try
        {
            hw.Update();
        }
        catch { return; /* 驱动/传感器异常：跳过整个硬件 */ }
        foreach (ISensor s in hw.Sensors)
        {
            try
            {
                if (!Wanted.Contains(s.SensorType) || s.Value is null)
                    continue;
                double v = s.Value.Value;
                string unit = Units.GetValueOrDefault(s.SensorType, "");
                if (s.SensorType == SensorType.Throughput)
                {
                    v /= 1048576.0; // 字节/秒 → MB/s
                    unit = "MB/s";
                }
                else if (s.SensorType == SensorType.Data && IsGpuHardware(hw))
                {
                    unit = "MB";   // 此版本 LHM 的 GPU 显存行为 Data（值即 MB）
                }
                else if (s.SensorType == SensorType.Data && hw.HardwareType == HardwareType.Memory)
                {
                    v /= 1073741824.0; // 此版本 LHM 内存 Data 原始值为字节 → GB
                    unit = "GB";
                }
                rows.Add(new SensorRow(name, s.Name, s.SensorType.ToString(), Math.Round(v, 2), unit));
            }
            catch { /* 单个传感器异常：继续下一个 */ }
        }
    }

    private static bool IsGpuHardware(IHardware hw) =>
        hw.HardwareType is HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel;

    private void Render(List<SensorRow> rows)
    {
        // 原地更新数值，避免每秒整表重建的分配与闪烁
        _view.BeginUpdate();
        HashSet<string> seen = [];
        foreach (SensorRow r in rows)
        {
            string key = $"{r.Hardware}|{r.Sensor}";
            seen.Add(key);
            if (!_items.TryGetValue(key, out ListViewItem? item))
            {
                item = new ListViewItem([r.Hardware, r.Sensor, "-", ""]);
                _items[key] = item;
                _view.Items.Add(item);
            }
            item.SubItems[2].Text = r.Value?.ToString() ?? "-";
            item.SubItems[3].Text = r.Unit;
        }
        foreach (string key in _items.Keys.Where(k => !seen.Contains(k)).ToList())
        {
            _view.Items.Remove(_items[key]);
            _items.Remove(key);
        }
        _view.EndUpdate();
    }

    private static Icon LoadIcon()
    {
        try
        {
            using var s = typeof(MainForm).Assembly.GetManifestResourceStream("HardwarePanel.nerv.ico");
            return s is null ? SystemIcons.Application : new Icon(s);
        }
        catch
        {
            return SystemIcons.Application; // ico 解码异常时退回系统图标
        }
    }

    private ContextMenuStrip BuildTrayMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("显示面板", null, (_, _) => ShowPanel());
        menu.Items.Add(new ToolStripSeparator());

        var autoStart = new ToolStripMenuItem("开机自启")
        {
            CheckOnClick = true,
            Checked = AutoStartHelper.IsEnabled(),
        };
        autoStart.Click += (_, _) =>
        {
            bool ok = autoStart.Checked ? AutoStartHelper.Enable() : AutoStartHelper.Disable();
            if (!ok)
            {
                autoStart.Checked = !autoStart.Checked; // 失败回滚勾选状态
                _tray.ShowBalloonTip(3000, "HardwarePanel", "开机自启设置失败", ToolTipIcon.Error);
            }
        };
        menu.Items.Add(autoStart);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("卸载…", null, (_, _) => Uninstall());
        menu.Items.Add("退出", null, (_, _) => ExitApp());
        return menu;
    }

    private void ShowPanel()
    {
        ShowInTaskbar = true;
        Show();
        WindowState = FormWindowState.Normal;
        Activate();
    }

    private void ExitApp()
    {
        _exiting = true;
        Close();
    }

    /// <summary>
    /// 卸载：删开机自启计划任务 →（可选）退出后由 cmd 延迟自删程序文件夹。
    /// exe 运行中无法删除自身，ping 3 秒等进程退出后 rd，再补一次防文件句柄延迟释放。
    /// </summary>
    private void Uninstall()
    {
        var choice = MessageBox.Show(
            "卸载将删除开机自启计划任务并退出程序。\n\n" +
            "是否连同程序文件夹一起删除？（含 config.json 与 img/ 图片）\n" +
            "「否」= 仅清自启并退出，保留全部文件；「取消」= 放弃。",
            "卸载 HardwarePanel",
            MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question,
            MessageBoxDefaultButton.Button3);
        if (choice == DialogResult.Cancel)
            return;

        AutoStartHelper.Disable(); // 任务不存在时失败也无需提示

        if (choice == DialogResult.Yes)
        {
            string dir = AppContext.BaseDirectory.TrimEnd('\\', '/');
            _ = Process.Start(new ProcessStartInfo("cmd.exe",
                $"/c ping -n 4 127.0.0.1 >nul & rd /s /q \"{dir}\" & ping -n 3 127.0.0.1 >nul & rd /s /q \"{dir}\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
            });
        }
        ExitApp();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // 点 X 只是收进托盘：采集与 HTTP 服务继续运行
        if (!_exiting)
        {
            e.Cancel = true;
            Hide();
            _tray.ShowBalloonTip(2000, "HardwarePanel", "程序在后台继续运行，双击托盘图标恢复", ToolTipIcon.Info);
        }
        base.OnFormClosing(e);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _timer.Stop();
        _tray.Dispose();
        _hub.Dispose();
        _gpu.Dispose();
        _computer.Close();
        base.OnFormClosed(e);
    }
}
