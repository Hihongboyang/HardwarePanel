using System.Text.Json;

namespace HardwarePanel;

public sealed record AlarmConfig(string Match, string Type, string Op, double Value, int CooldownMinutes);

public sealed record NotifyConfig(string WebhookUrl, string WebhookFormat, string Token);

public sealed record PhotosConfig(int IntervalSeconds);

public sealed record AppConfig(
    List<string> Visible,
    List<AlarmConfig> Alarms,
    NotifyConfig Notify,
    PhotosConfig Photos);

/// <summary>
/// 运行时配置（AppContext.BaseDirectory\config.json）：
/// 文件缺失/损坏时写默认骨架；PUT /api/config 反序列化 → Clamp → Save（整体替换内存引用）。
/// </summary>
internal static class ConfigStore
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private static readonly string ConfigPath = System.IO.Path.Combine(AppContext.BaseDirectory, "config.json");

    /// <summary>Load 时配置文件是本次新建的骨架（等待首帧采集后填充默认 visible）。</summary>
    public static bool IsFresh { get; private set; }

    /// <summary>告警引擎单例：PUT 配置后由 SensorHub 调 ResetCooldowns()。</summary>
    public static AlarmEngine Alarms { get; } = new();

    private static volatile AppConfig _current = Load();

    public static AppConfig Current => _current;

    private static AppConfig Load()
    {
        try
        {
            if (File.Exists(ConfigPath)
                && JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(ConfigPath), JsonOpts) is AppConfig cfg)
            {
                return Clamp(cfg);
            }
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            // 损坏 → 重建骨架
        }
        IsFresh = true;
        AppConfig fresh = Default();
        try { Save(fresh); }
        catch (IOException) { /* 磁盘暂不可写：先用内存默认，下次 PUT 再落盘 */ }
        return fresh;
    }

    private static AppConfig Default() => new(
        [],
        [
            new AlarmConfig("CPU Package", "Temperature", ">", 90, 10),
            new AlarmConfig("Memory", "Load", ">", 95, 10),
        ],
        new NotifyConfig("", "ntfy", ""),
        new PhotosConfig(10));

    /// <summary>越界值就地 clamp，不报错：intervalSeconds ≥ 3、alarms ≤ 20；仅支持 op "&gt;"。</summary>
    public static AppConfig Clamp(AppConfig? raw)
    {
        raw ??= Default();
        NotifyConfig notify = raw.Notify ?? new NotifyConfig("", "ntfy", "");
        notify = notify with
        {
            WebhookUrl = notify.WebhookUrl ?? "",
            WebhookFormat = string.IsNullOrEmpty(notify.WebhookFormat) ? "ntfy" : notify.WebhookFormat,
            Token = notify.Token ?? "",
        };
        PhotosConfig photos = raw.Photos ?? new PhotosConfig(10);
        photos = photos with { IntervalSeconds = Math.Max(3, photos.IntervalSeconds) };
        List<AlarmConfig> alarms =
        [
            .. (raw.Alarms ?? [])
                .Where(a => a is not null)
                .Take(20)
                .Select(a => a! with { Match = a.Match ?? "", Type = a.Type ?? "", Op = ">" }),
        ];
        return raw with { Visible = raw.Visible ?? [], Alarms = alarms, Notify = notify, Photos = photos };
    }

    /// <summary>首帧采集后调用：仅当配置是本次新建骨架时，按谓词填默认 visible 并写盘。</summary>
    public static void SeedDefaultVisible(List<SensorRow> rows)
    {
        if (!IsFresh) return;
        IsFresh = false;
        List<string> visible = CreateDefaultVisible(rows);
        if (visible.Count == 0) return;
        Save(_current with { Visible = visible });
    }

    /// <summary>
    /// 丢弃 visible 中当前采集不到的键(设备更换 / 数据源被过滤 / LHM 升级改名)。
    /// 启动首帧与每次 PUT 时调用;失效项前后端都不再出现,无需用户手动清理。
    /// 取舍:设备热拔插后重启会清掉对应勾选,需重新勾选(家用常驻机可接受)。
    /// </summary>
    public static void SanitizeVisible(IEnumerable<string> liveKeys)
    {
        HashSet<string> live = [.. liveKeys];
        AppConfig cur = _current;
        List<string> filtered = [.. cur.Visible.Where(live.Contains)];
        if (filtered.Count == cur.Visible.Count) return;
        try { Save(cur with { Visible = filtered }); }
        catch (IOException) { /* 磁盘暂不可写:下次启动/PUT 再试 */ }
    }


    /// <summary>默认 visible 谓词：CPU 总览三项 + GPU 核显伪行 + 内存两行（实际行名以首轮采集为准）。</summary>
    internal static List<string> CreateDefaultVisible(List<SensorRow> rows)
    {
        List<string> keys = [];
        foreach (SensorRow r in rows)
        {
            bool cpu = r.Sensor is "CPU Total" or "CPU Package" or "Core Average";
            bool gpu = r.Hardware == "GPU 核显";
            bool mem = r.Hardware.Contains("Memory", StringComparison.OrdinalIgnoreCase)
                       && r.Sensor is "Memory" or "Used Memory";
            if (cpu || gpu || mem)
                keys.Add($"{r.Hardware}|{r.Sensor}|{r.Type}");
        }
        return keys;
    }

    /// <summary>Indented 写回；先写 .tmp 再 File.Replace，避免半截文件。</summary>
    public static void Save(AppConfig cfg)
    {
        string json = JsonSerializer.Serialize(cfg, JsonOpts);
        string tmp = ConfigPath + ".tmp";
        File.WriteAllText(tmp, json);
        if (File.Exists(ConfigPath)) File.Replace(tmp, ConfigPath, null);
        else File.Move(tmp, ConfigPath);
        _current = cfg;
    }
}
