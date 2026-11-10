using System.Runtime.InteropServices;

namespace HardwarePanel;

/// <summary>
/// GPU 读取：单个 PDH 通配符查询，一次 PdhCollectQueryData 刷新全部实例，
/// 替代“每实例一个 PerformanceCounter”（每秒上百次独立 PDH 查询是 CPU 占用大头）。
/// 通配符实例随进程增删自动更新，无需重扫。
/// </summary>
internal sealed class GpuCounters : IDisposable
{
    private const uint PdhMoreData = 0x800007D2;
    private const uint PdhFmtDouble = 0x200;

    [StructLayout(LayoutKind.Sequential)]
    private struct CounterItem
    {
        public IntPtr Name;  // LPWSTR，不需要读
        public uint CStatus; // PDH_FMT_COUNTERVALUE 头部（x64 下含对齐，item 共 24 字节）
        public double Value;
    }

    private IntPtr _query;
    private IntPtr _engine;
    private IntPtr _dedicated;
    private IntPtr _shared;
    private IntPtr _buffer;
    private int _capacityBytes;
    private readonly bool _ok;

    public GpuCounters()
    {
        _ok = PdhOpenQuery(IntPtr.Zero, IntPtr.Zero, out _query) == 0
           && PdhAddEnglishCounter(_query, @"\GPU Engine(*)\Utilization Percentage", IntPtr.Zero, out _engine) == 0
           && PdhAddEnglishCounter(_query, @"\GPU Adapter Memory(*)\Dedicated Usage", IntPtr.Zero, out _dedicated) == 0
           && PdhAddEnglishCounter(_query, @"\GPU Adapter Memory(*)\Shared Usage", IntPtr.Zero, out _shared) == 0;
        if (_ok)
            PdhCollectQueryData(_query); // 基线样本：百分比计数器需要两次采样才有值
    }

    /// <returns>(总使用率 %, 专用显存 字节, 共享内存 字节)</returns>
    public (float Util, float DedicatedBytes, float SharedBytes) Read()
    {
        if (!_ok || PdhCollectQueryData(_query) != 0)
            return (0, 0, 0);

        float util = Math.Clamp((float)Sum(_engine), 0, 100);
        return (util, (float)Sum(_dedicated), (float)Sum(_shared));
    }

    private double Sum(IntPtr counter)
    {
        if (counter == IntPtr.Zero)
            return 0;

        // 按文档两段式：先 0/NULL 取所需字节数（此时必返回 PDH_MORE_DATA），再真正取数。
        // 注意：入参字节数 >0 且不足时，返回的尺寸不可靠，必须从 0 开始问。
        uint size = 0, count;
        if (PdhGetFormattedCounterArray(counter, PdhFmtDouble, ref size, out count, IntPtr.Zero) != PdhMoreData)
            return 0;
        Ensure((int)size);

        uint res = PdhGetFormattedCounterArray(counter, PdhFmtDouble, ref size, out count, _buffer);
        if (res != 0)
            return 0;

        double sum = 0;
        int stride = Marshal.SizeOf<CounterItem>();
        for (int i = 0; i < count; i++)
            sum += Marshal.PtrToStructure<CounterItem>(_buffer + i * stride).Value;
        return sum;
    }

    private void Ensure(int bytes)
    {
        if (bytes <= _capacityBytes)
            return;
        if (_buffer != IntPtr.Zero)
            Marshal.FreeHGlobal(_buffer);
        _capacityBytes = bytes * 2; // 余量，减少扩容频率
        _buffer = Marshal.AllocHGlobal(_capacityBytes);
    }

    public void Dispose()
    {
        if (_buffer != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(_buffer);
            _buffer = IntPtr.Zero;
        }
        if (_query != IntPtr.Zero)
        {
            PdhCloseQuery(_query); // 关闭查询同时释放其中计数器
            _query = IntPtr.Zero;
        }
    }

    [DllImport("pdh.dll")]
    private static extern uint PdhOpenQuery(IntPtr dataSource, IntPtr userData, out IntPtr query);

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    private static extern uint PdhAddEnglishCounter(IntPtr query, string counterPath, IntPtr userData, out IntPtr counter);

    [DllImport("pdh.dll")]
    private static extern uint PdhCollectQueryData(IntPtr query);

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    private static extern uint PdhGetFormattedCounterArray(IntPtr counter, uint format,
        ref uint bufferSize, out uint itemCount, IntPtr itemBuffer);

    [DllImport("pdh.dll")]
    private static extern uint PdhCloseQuery(IntPtr query);
}
