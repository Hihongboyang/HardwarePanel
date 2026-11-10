namespace HardwarePanel;

/// <summary>
/// 阈值告警引擎（仅支持 "&gt;"）：每秒随采集驱动 Evaluate，命中且过冷却则产出一条，
/// 由 MainForm 经 SensorHub.Notify 广播为 alarm 事件帧 + webhook。无外部依赖。
/// </summary>
internal sealed class AlarmEngine
{
    private readonly Dictionary<int, DateTime> _fired = []; // alarm 下标 → 上次触发（UTC）

    public IReadOnlyList<(string Title, string Body)> Evaluate(AppConfig cfg, List<SensorRow> rows)
    {
        List<(string Title, string Body)> hits = [];
        List<AlarmConfig> alarms = cfg.Alarms;
        for (int i = 0; i < alarms.Count; i++)
        {
            AlarmConfig alarm = alarms[i];
            if (alarm.Match.Length == 0) continue;
            SensorRow? row = rows.FirstOrDefault(r =>
                r.Sensor.Contains(alarm.Match, StringComparison.OrdinalIgnoreCase)
                && (alarm.Type.Length == 0 || r.Type == alarm.Type));
            if (row?.Value is not double v || v <= alarm.Value) continue;
            if (_fired.TryGetValue(i, out DateTime last)
                && DateTime.UtcNow - last < TimeSpan.FromMinutes(alarm.CooldownMinutes))
                continue;
            _fired[i] = DateTime.UtcNow;
            hits.Add(($"{row.Sensor} 超过 {alarm.Value}", $"当前 {v} {row.Unit}"));
        }
        return hits;
    }

    /// <summary>PUT /api/config 后调用：新阈值立即生效。</summary>
    public void ResetCooldowns() => _fired.Clear();
}
