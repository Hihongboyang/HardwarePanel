using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
namespace HardwarePanel;

public sealed record SensorRow(
    string Hardware, string Sensor, string Type, double? Value, string Unit,
    double[]? History = null);

/// <summary>设备目录条目:键 → 所属设备/硬件类型/面板(0-3)。面板归属由后端按 HardwareType 判定。</summary>
public sealed record HwInfoEntry(string Key, string Device, string HwType, int Panel);

/// <summary>
/// 维护最新传感器快照（含历史曲线），HttpListener 对外提供：
///   /            仪表盘页（wwwroot/index.html）
///   /api/sensors JSON 快照（Load/Temperature 行带 history 数组）
///   /api/images  img 目录下图片列表
///   /img/{name}  图片本体
/// </summary>
internal sealed class SensorHub : IDisposable
{
    /// <summary>每个传感器保留 1Hz × 300 点 = 5 分钟历史。</summary>
    private const int HistoryLen = 300;

    /// <summary>面板显示名(下标即面板号),与前端 lang.js 保持一致。</summary>
    public static readonly string[] PanelNames = ["处理器", "显卡", "内存 · 主板", "存储 · 系统"];


    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private static readonly string[] ImageExts = [".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp"];

    private sealed class Ring
    {
        public readonly double[] Data = new double[HistoryLen];
        public int Head;  // 下一个写入位置
        public int Count;

        public void Add(double v)
        {
            Data[Head] = v;
            Head = (Head + 1) % HistoryLen;
            if (Count < HistoryLen) Count++;
        }

        public double[] ToArray()
        {
            var result = new double[Count];
            for (int i = 0; i < Count; i++)
                result[i] = Data[(Head - Count + i + HistoryLen) % HistoryLen];
            return result;
        }
    }

    /// <summary>一个 /api/stream 连接的独立事件队列：Notify 广播时逐个入队。</summary>
    private sealed class SseClient
    {
        public readonly Queue<string> Frames = new();
        public readonly ManualResetEventSlim Ready = new(false);
    }

    private readonly HttpListener _listener = new();
    private readonly object _lock = new();
    private readonly Dictionary<string, Ring> _rings = [];
    private List<SensorRow> _rows = [];
    private CancellationTokenSource? _cts;
    private string _compact = "[]";                                  // 不含历史的增量载荷

    private volatile string _hwinfoJson = "{\"panels\":[],\"keys\":{}}"; // 目录就绪前的空目录
    private readonly ManualResetEventSlim _dataReady = new(false);   // 通知 SSE 客户端
    private readonly object _clientsLock = new();
    private readonly List<SseClient> _clients = [];        // 每个连接独立事件队列，广播
    private static readonly HttpClient WebhookHttp = new() { Timeout = TimeSpan.FromSeconds(5) };

    public void Publish(List<SensorRow> rows)
    {
        var published = new List<SensorRow>(rows.Count);
        var seen = new HashSet<string>();
        lock (_lock)
        {
            foreach (SensorRow r in rows)
            {
                string key = $"{r.Hardware}|{r.Sensor}|{r.Type}";
                seen.Add(key);
                if (r.Value is double v)
                {
                    if (!_rings.TryGetValue(key, out Ring? ring))
                        _rings[key] = ring = new Ring();
                    ring.Add(Math.Round(v, 1));
                    published.Add(r with { History = ring.ToArray() });
                }
                else
                {
                    published.Add(r);
                }
            }
            foreach (string stale in _rings.Keys.Where(k => !seen.Contains(k)).ToList())
                _rings.Remove(stale);
            _rows = published;
            _compact = JsonSerializer.Serialize(
                published.Select(r => r with { History = null }).ToList(), JsonOpts);
        }
        _dataReady.Set();
    }

    /// <summary>发布设备目录(/api/hwinfo 数据源)。随采集刷新,JSON 按需序列化一次。</summary>
    public void PublishCatalog(List<HwInfoEntry> entries)
    {
        Dictionary<string, HwInfoEntry> keys = [];
        foreach (HwInfoEntry e in entries)
            keys[e.Key] = e; // 同键后写覆盖
        _hwinfoJson = JsonSerializer.Serialize(new { Panels = PanelNames, Keys = keys }, JsonOpts);
    }

    /// <summary>当前正在发布的全部键(配置清洗用)。</summary>
    private HashSet<string> CurrentKeys()
    {
        lock (_lock) return [.. _rows.Select(r => $"{r.Hardware}|{r.Sensor}|{r.Type}")];
    }


    public void Start(int port)
    {
        _listener.Prefixes.Add($"http://+:{port}/");
        _listener.Start();
        _cts = new CancellationTokenSource();
        _ = Task.Run(() => ServeLoop(_cts.Token));
    }

    private async Task ServeLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            HttpListenerContext ctx;
            try { ctx = await _listener.GetContextAsync(); }
            catch { break; } // listener 关闭
            _ = Task.Run(() => { try { Handle(ctx); } catch { /* 单个请求失败不拖垮服务 */ } }, ct);
        }
    }

    private void Handle(HttpListenerContext ctx)
    {
        string path = Uri.UnescapeDataString(ctx.Request.Url!.AbsolutePath).TrimStart('/');
        switch (path)
        {
            case "api/sensors":
                Write(ctx, "application/json; charset=utf-8",
                    JsonSerializer.Serialize(Snapshot(), JsonOpts));
                break;
            case "api/hwinfo":
                Write(ctx, "application/json; charset=utf-8", _hwinfoJson);
                break;
            case "api/images":
                Write(ctx, "application/json; charset=utf-8",
                    JsonSerializer.Serialize(ListImages(), JsonOpts));
                break;
            case "api/stream":
                HandleStream(ctx); // SSE：占住连接持续推送，不走 Write 的“一次性响应”路径
                break;
            case "api/config":
                if (ctx.Request.HttpMethod == "PUT") HandlePutConfig(ctx);
                else Write(ctx, "application/json; charset=utf-8",
                    JsonSerializer.Serialize(ConfigStore.Current, JsonOpts));
                break;
            case "api/notify":
                HandleNotify(ctx);
                break;
            case "" or "index.html" or "photos.html" or "settings.html" or "notify.js" or "lang.js":
                ServeFile(ctx, Path.Combine(Root("wwwroot"), path == "" ? "index.html" : path),
                    path.EndsWith(".js") ? "application/javascript; charset=utf-8"
                                         : "text/html; charset=utf-8");
                break;
            default:
                if (path.StartsWith("img/"))
                    ServeImage(ctx, path["img/".Length..]);
                else
                    NotFound(ctx);
                break;
        }
    }

    /// <summary>SSE 流：先推一次全量（含历史），之后每秒推增量（不含历史）。</summary>
    private void HandleStream(HttpListenerContext ctx)
    {
        ctx.Response.ContentType = "text/event-stream; charset=utf-8";
        ctx.Response.Headers["Cache-Control"] = "no-cache";
        ctx.Response.SendChunked = true;
        Stream outStream = ctx.Response.OutputStream;
        byte[] prologue = System.Text.Encoding.UTF8.GetBytes("retry: 2000\n\n");
        outStream.Write(prologue, 0, prologue.Length);
        byte[] full = System.Text.Encoding.UTF8.GetBytes(
            $"data: {JsonSerializer.Serialize(Snapshot(), JsonOpts)}\n\n");
        outStream.Write(full, 0, full.Length);
        outStream.Flush();

        var client = new SseClient();
        lock (_clientsLock) _clients.Add(client);
        try
        {
        while (true)
        {
            // 数据/事件任一就绪即醒；超时也醒来当保活
            WaitHandle.WaitAny([_dataReady.WaitHandle, client.Ready.WaitHandle], 2000);
            _dataReady.Reset();
            client.Ready.Reset();
            try
            {
                string[] frames;
                lock (_clientsLock) { frames = [.. client.Frames]; client.Frames.Clear(); }
                foreach (string frame in frames) // 事件帧优先，逐客户端独立投递
                {
                    byte[] ev = System.Text.Encoding.UTF8.GetBytes(frame);
                    outStream.Write(ev, 0, ev.Length);
                }
                byte[] update = System.Text.Encoding.UTF8.GetBytes($"data: {_compact}\n\n");
                outStream.Write(update, 0, update.Length);
                outStream.Flush();
            }
            catch
            {
                break; // 客户端断开
            }
        }
        }
        finally
        {
            lock (_clientsLock) _clients.Remove(client);
            client.Ready.Dispose();
        }
    }

    private List<SensorRow> Snapshot()
    {
        lock (_lock) return [.. _rows];
    }

    private static List<string> ListImages()
    {
        string dir = ImgDir();
        Directory.CreateDirectory(dir);
        return Directory.EnumerateFiles(dir)
            .Where(f => ImageExts.Contains(Path.GetExtension(f).ToLowerInvariant()))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .Select(f => "img/" + Path.GetFileName(f))
            .ToList();
    }

    private static void ServeImage(HttpListenerContext ctx, string name)
    {
        string fileName = Path.GetFileName(name); // 防目录穿越
        string path = Path.Combine(ImgDir(), fileName);
        if (!File.Exists(path)) { NotFound(ctx); return; }
        string ext = Path.GetExtension(fileName).ToLowerInvariant();
        string mime = ext == ".png" ? "image/png"
                    : ext == ".gif" ? "image/gif"
                    : ext == ".bmp" ? "image/bmp"
                    : ext == ".webp" ? "image/webp"
                    : "image/jpeg";
        ServeFile(ctx, path, mime);
    }

    private static void ServeFile(HttpListenerContext ctx, string path, string contentType)
    {
        if (!File.Exists(path)) { NotFound(ctx); return; }
        Write(ctx, contentType, File.ReadAllBytes(path));
    }

    private static void Write(HttpListenerContext ctx, string contentType, byte[] body)
    {
        ctx.Response.ContentType = contentType;
        ctx.Response.ContentLength64 = body.Length;
        ctx.Response.OutputStream.Write(body, 0, body.Length);
        ctx.Response.Close();
    }

    private static void Write(HttpListenerContext ctx, string contentType, string body) =>
        Write(ctx, contentType, System.Text.Encoding.UTF8.GetBytes(body));

    private static void NotFound(HttpListenerContext ctx)
    {
        ctx.Response.StatusCode = 404;
        ctx.Response.Close();
    }

    /// <summary>优先取输出目录，兼容 dotnet run（cwd = 项目根）。</summary>
    private static string Root(string dir) =>
        Directory.Exists(Path.Combine(AppContext.BaseDirectory, dir))
            ? Path.Combine(AppContext.BaseDirectory, dir)
            : dir;

    private static string ImgDir() => Root("img");

    /// <summary>广播一条通知/告警：SSE 事件帧 + 可选 webhook（ntfy/generic，fire-and-forget）。</summary>
    public void Notify(string title, string body, string source = "app", bool isAlarm = false)
    {
        string json = JsonSerializer.Serialize(
            new { title, body, source, ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds() }, JsonOpts);
        string frame = $"event: {(isAlarm ? "alarm" : "notify")}\ndata: {json}\n\n";
        lock (_clientsLock)
        {
            foreach (SseClient c in _clients)
            {
                c.Frames.Enqueue(frame); // 每个连接独立队列，广播不互相抢
                c.Ready.Set();
            }
        }

        NotifyConfig n = ConfigStore.Current.Notify;
        if (n.WebhookUrl.Length == 0) return;
        object payload = n.WebhookFormat == "generic"
            ? new { title, body, source }
            : new { title, message = body, tags = new[] { "warning" } };
        _ = Task.Run(async () =>
        {
            try { using HttpResponseMessage _ = await WebhookHttp.PostAsJsonAsync(n.WebhookUrl, payload); }
            catch { /* 推送失败静默：屏幕 toast 仍是主通道 */ }
        });
    }

    private void HandlePutConfig(HttpListenerContext ctx)
    {
        AppConfig cfg;
        try
        {
            using var reader = new StreamReader(ctx.Request.InputStream);
            cfg = ConfigStore.Clamp(JsonSerializer.Deserialize<AppConfig>(reader.ReadToEnd(), JsonOpts));
        }
        catch (JsonException)
        {
            ctx.Response.StatusCode = 400;
            Write(ctx, "application/json; charset=utf-8", @"{""ok"":false}");
            return;
        }
        // 失效键即时丢弃:保存的 visible 永远只含当前真实存在的传感器
        HashSet<string> live = CurrentKeys();
        cfg = cfg with { Visible = [.. cfg.Visible.Where(live.Contains)] };
        ConfigStore.Save(cfg);
        ConfigStore.Alarms.ResetCooldowns();
        Write(ctx, "application/json; charset=utf-8", @"{""ok"":true}");
    }

    private void HandleNotify(HttpListenerContext ctx)
    {
        if (ctx.Request.HttpMethod != "POST") { NotFound(ctx); return; }
        string? title = null, body = null, source = null;
        try
        {
            using var reader = new StreamReader(ctx.Request.InputStream);
            if (JsonSerializer.Deserialize<NotifyPayload>(reader.ReadToEnd(), JsonOpts)
                is { } msg)
            {
                title = msg.Title;
                body = msg.Body;
                source = msg.Source;
            }
        }
        catch (JsonException)
        {
            ctx.Response.StatusCode = 400;
            Write(ctx, "application/json; charset=utf-8", @"{""ok"":false}");
            return;
        }

        NotifyConfig n = ConfigStore.Current.Notify;
        if (n.Token.Length > 0 && ctx.Request.QueryString["token"] != n.Token)
        {
            ctx.Response.StatusCode = 401;
            Write(ctx, "application/json; charset=utf-8", @"{""ok"":false}");
            return;
        }
        Notify(title ?? "通知", body ?? "", string.IsNullOrEmpty(source) ? "external" : source,
            isAlarm: false);
        Write(ctx, "application/json; charset=utf-8", @"{""ok"":true}");
    }

    private sealed record NotifyPayload(string? Title, string? Body, string? Source);

    public void Dispose()
    {
        _cts?.Cancel();
        if (_listener.IsListening) _listener.Stop();
        _listener.Close();
        _dataReady.Set(); // 唤醒 SSE 等待线程让它们随连接一起退出
        lock (_clientsLock)
        {
            foreach (SseClient c in _clients) c.Ready.Set();
        }
    }
}
