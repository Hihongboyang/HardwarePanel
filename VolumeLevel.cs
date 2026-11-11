using System.Runtime.InteropServices;

namespace HardwarePanel;

/// <summary>
/// 主音量读取（CoreAudio IAudioEndpointVolume）。
/// 任何一步失败都静默返回 null：音量不可用时信息屏该行显示「—」，不影响其它采集。
/// </summary>
internal sealed class VolumeLevel
{
    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private class MMDeviceEnumeratorComObject { }

    /// <summary>vtable 顺序必须与 Windows SDK 一致（只声明到用到的方法）。</summary>
    [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        void EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices); // 占位：不调用
        void GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);
    }

    [Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        void Activate(ref Guid iid, int clsCtx, IntPtr activationParams,
            [MarshalAs(UnmanagedType.IUnknown)] out object iface);
    }

    [Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioEndpointVolume
    {
        void RegisterControlChangeNotify(IntPtr notify);   // 占位：不调用
        void UnregisterControlChangeNotify(IntPtr notify); // 占位：不调用
        void GetChannelCount(out int channelCount);        // 占位：不调用
        void SetMasterVolumeLevel(float levelDb, int eventContext);
        void SetMasterVolumeLevelScalar(float level, int eventContext);
        void GetMasterVolumeLevel(out float levelDb);
        void GetMasterVolumeLevelScalar(out float level);
    }

    private IAudioEndpointVolume? _endpoint;

    public VolumeLevel()
    {
        try
        {
            var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
            enumerator.GetDefaultAudioEndpoint(0 /*eRender*/, 1 /*eMultimedia*/, out IMMDevice device);
            Guid iid = new("5CDF2C82-841E-4546-9722-0CF74078229A");
            device.Activate(ref iid, 1 /*CLSCTX_INPROC_SERVER*/, IntPtr.Zero, out object o);
            _endpoint = o as IAudioEndpointVolume;
        }
        catch
        {
            _endpoint = null; // 无音频设备/服务未启动
        }
    }

    /// <returns>0–100 主音量；不可用返回 null。</returns>
    public double? Read()
    {
        try
        {
            if (_endpoint is null) return null;
            _endpoint.GetMasterVolumeLevelScalar(out float level);
            return Math.Round(Math.Clamp(level, 0f, 1f) * 100, 0);
        }
        catch
        {
            return null;
        }
    }
}
