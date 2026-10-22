using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace SoloScreen;

/// <summary>
/// 截图音效。
///
/// 音效来源优先级（每次播放前都会检查，支持运行中替换）：
///   1. exe 同级的 audio.wav  —— 外置，用户可自行替换
///   2. exe 同级的 audio.mp3  —— 外置，用户可自行替换
///   3. 编译进 exe 的内置音效（assets/audio.wav）—— 没有外置文件时使用，不必随程序附带音效文件
///
/// 两个关键设计（否则会出现"偶尔只听到后半截"）：
/// 1) 预加载：WAV 启动时就整个读进非托管内存，用 PlaySound(SND_MEMORY) 直接播；
///    MP3 启动时就打开 MCI 设备并常驻，播放时只做 seek + play，不重新 open。
///    原实现每次都 close→open→play，解码器初始化/缓冲期间开头几百毫秒会被吞掉。
/// 2) 不打断：上一段还没播完时，新的一次截图不再重新触发音效（WAV 用 SND_NOSTOP，
///    MP3 查 MCI 状态），避免快速连拍时把声音掐成碎片。
/// </summary>
internal static class SoundUtil
{
    private const string MciAlias = "SoloScreenAudio";
    private const string EmbeddedResourceName = "SoloScreen.audio.wav";

    private const uint SndSync = 0x0000;
    private const uint SndAsync = 0x0001;
    private const uint SndNoDefault = 0x0002;
    private const uint SndMemory = 0x0004;
    private const uint SndNoStop = 0x0010;

    /// <summary>当前音效来源。</summary>
    private enum Source
    {
        None,
        ExternalWav,
        ExternalMp3,
        Embedded,
    }

    [DllImport("winmm.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool PlaySound(IntPtr pszSound, IntPtr hmod, uint fdwSound);

    [DllImport("winmm.dll", CharSet = CharSet.Unicode)]
    private static extern int mciSendString(string command, StringBuilder? buffer, int bufferSize, IntPtr hwndCallback);

    private static Source _source = Source.None;
    private static string? _loadedPath;
    private static DateTime _loadedStamp;

    private static IntPtr _wavMemory;      // 外置/内置 WAV 都放这块非托管内存
    private static IntPtr _silenceMemory;  // 预热用的 400ms 静音 WAV
    private static bool _mp3Open;

    /// <summary>当前音效来源的说明（用于启动日志，便于排查）。</summary>
    public static string SourceDescription => _source switch
    {
        Source.ExternalWav => "外置 audio.wav",
        Source.ExternalMp3 => "外置 audio.mp3",
        Source.Embedded => "内置",
        _ => "无",
    };

    /// <summary>
    /// 启动时调用：把音频准备好，并做一次"设备预热"。
    /// 预热的必要性：系统空闲时音频设备会进入省电状态，第一次播放要等设备唤醒，
    /// 这 100~200ms 会把开头很短的一段吞掉（表现为"只听到后半截"）。
    /// 预热方式：播放一小段全零静音，设备被唤醒但听不到任何声音。
    /// </summary>
    public static void Prepare()
    {
        try
        {
            EnsurePrepared();
            if (_source is Source.ExternalWav or Source.Embedded or Source.ExternalMp3)
                HeatDevice();
        }
        catch (Exception ex)
        {
            SimpleLog.Write("音效预加载失败：" + ex.Message);
        }
    }

    /// <summary>退出时释放资源。</summary>
    public static void Shutdown()
    {
        FreeWav();
        CloseMci();
        FreeSilence();
    }

    /// <summary>上次播放（含预热）的时间，用来判断设备是否又睡回去了。</summary>
    private static DateTime _lastActivityUtc = DateTime.MinValue;
    private static readonly TimeSpan ColdThreshold = TimeSpan.FromSeconds(20);

    /// <summary>异步播放静音，把音频设备唤醒（听不到声音）。用于启动时预热，不阻塞。</summary>
    private static void HeatDevice()
    {
        EnsureSilenceBuffer();
        if (_silenceMemory == IntPtr.Zero) return;

        PlaySound(_silenceMemory, IntPtr.Zero, SndMemory | SndAsync | SndNoDefault | SndNoStop);
        _lastActivityUtc = DateTime.UtcNow;
    }

    /// <summary>
    /// 同步播放静音：阻塞到静音播完，此时设备已唤醒、且静音肯定结束，
    /// 随后播放的正式音效不会被 SND_NOSTOP 误判为"上一段还在响"而跳过。
    /// </summary>
    private static void HeatDeviceSync()
    {
        EnsureSilenceBuffer();
        if (_silenceMemory == IntPtr.Zero) return;

        PlaySound(_silenceMemory, IntPtr.Zero, SndMemory | SndSync | SndNoDefault);
        _lastActivityUtc = DateTime.UtcNow;
    }

    public static void PlayProgramDirAudio()
    {
        try
        {
            EnsurePrepared();
            if (_source == Source.None) return;

            var now = DateTime.UtcNow;
            bool cold = (now - _lastActivityUtc) > ColdThreshold;
            _lastActivityUtc = now;

            if (cold)
            {
                // 设备可能已休眠：同步播放静音（阻塞到播完）唤醒设备，再放正式音效。
                // 必须同步：若静音还在异步播放，正式音效会因 SND_NOSTOP 判定"上一段还在响"而整个被跳过。
                HeatDeviceSync();
                PlayNow();
                return;
            }

            PlayNow();
        }
        catch (Exception ex)
        {
            SimpleLog.Write("播放音效失败：" + ex.Message);
        }
    }

    private static void PlayNow()
    {
        switch (_source)
        {
            case Source.ExternalWav:
            case Source.Embedded:
                if (_wavMemory == IntPtr.Zero) return;
                // 内存播放；若上一次的 PlaySound 还在响，SND_NOSTOP 会让本次直接失败（不打断）
                PlaySound(_wavMemory, IntPtr.Zero, SndMemory | SndAsync | SndNoDefault | SndNoStop);
                break;

            case Source.ExternalMp3:
                if (IsMciPlaying()) return; // 上一段还没播完，不重新触发

                if (mciSendString($"seek {MciAlias} to start", null, 0, IntPtr.Zero) != 0)
                {
                    // 设备状态异常（例如被其他程序释放）：重开后本次放弃播放
                    var path = _loadedPath;
                    var stamp = _loadedStamp;
                    CloseMci();
                    if (path is not null) OpenMci(path, stamp);
                    return;
                }

                mciSendString($"play {MciAlias}", null, 0, IntPtr.Zero);
                break;
        }
    }

    /// <summary>
    /// 构造一段 250ms 的全零静音 WAV（标准 44.1kHz / 16bit / 立体声），用于唤醒休眠的音频设备。
    /// 时长是在"设备唤醒延迟"与"冷启动音效延迟"之间取的平衡：太长则闲置后第一次截图的音效明显延迟；
    /// 太短则静音播完时设备还没唤醒，正式音效开头仍会被吞（回到"只听到后半截"）。
    /// </summary>
    private static void EnsureSilenceBuffer()
    {
        if (_silenceMemory != IntPtr.Zero) return;

        const int sampleRate = 44100;
        const short channels = 2;
        const short bitsPerSample = 16;
        const int milliseconds = 250;

        int blockAlign = channels * bitsPerSample / 8;
        int dataSize = sampleRate * blockAlign * milliseconds / 1000;
        int totalSize = 44 + dataSize;

        var wav = new byte[totalSize];
        using (var ms = new MemoryStream(wav))
        using (var bw = new BinaryWriter(ms))
        {
            bw.Write("RIFF"u8.ToArray());
            bw.Write(totalSize - 8);
            bw.Write("WAVE"u8.ToArray());
            bw.Write("fmt "u8.ToArray());
            bw.Write(16);                       // fmt 块大小
            bw.Write((short)1);                 // PCM
            bw.Write(channels);
            bw.Write(sampleRate);
            bw.Write(sampleRate * blockAlign);  // 每秒字节数
            bw.Write((short)blockAlign);
            bw.Write(bitsPerSample);
            bw.Write("data"u8.ToArray());
            bw.Write(dataSize);
            // 数据区保持全零（静音）
        }

        var memory = Marshal.AllocHGlobal(totalSize);
        Marshal.Copy(wav, 0, memory, totalSize);
        _silenceMemory = memory;
    }

    private static void FreeSilence()
    {
        if (_silenceMemory == IntPtr.Zero) return;
        Marshal.FreeHGlobal(_silenceMemory);
        _silenceMemory = IntPtr.Zero;
    }


    /// <summary>决定用哪个音效：外置优先，否则用内置。</summary>
    private static void EnsurePrepared()
    {
        var dir = AppContext.BaseDirectory;
        var wav = Path.Combine(dir, "audio.wav");
        var mp3 = Path.Combine(dir, "audio.mp3");

        if (File.Exists(wav))
        {
            var stamp = File.GetLastWriteTimeUtc(wav);
            if (_source == Source.ExternalWav && _loadedPath == wav && _loadedStamp == stamp) return;

            CloseMci();
            try
            {
                LoadWavBytes(File.ReadAllBytes(wav));
                _source = Source.ExternalWav;
                _loadedPath = wav;
                _loadedStamp = stamp;
            }
            catch (Exception ex)
            {
                // 外置文件读不出来（被占用/损坏）：退回内置音效
                SimpleLog.Write("读取外置音效失败，改用内置音效：" + ex.Message);
                LoadEmbeddedWav();
            }
            return;
        }

        if (File.Exists(mp3))
        {
            var stamp = File.GetLastWriteTimeUtc(mp3);
            if (_source == Source.ExternalMp3 && _loadedPath == mp3 && _loadedStamp == stamp) return;

            FreeWav();
            CloseMci();
            OpenMci(mp3, stamp);
            return;
        }

        // 两种外置文件都没有：使用内置音效
        if (_source == Source.Embedded && _wavMemory != IntPtr.Zero) return;
        CloseMci();
        LoadEmbeddedWav();
    }

    private static void LoadEmbeddedWav()
    {
        try
        {
            using var res = Assembly.GetExecutingAssembly()
                .GetManifestResourceStream(EmbeddedResourceName);
            if (res is null)
            {
                FreeWav();
                return;
            }

            using var ms = new MemoryStream();
            res.CopyTo(ms);

            LoadWavBytes(ms.ToArray());
            _source = Source.Embedded;
            _loadedPath = null;
        }
        catch (Exception ex)
        {
            SimpleLog.Write("加载内置音效失败：" + ex.Message);
            FreeWav();
        }
    }

    /// <summary>把 WAV 字节放进取样到非托管内存（PlaySound 内存播放要求缓冲区持续有效）。</summary>
    private static void LoadWavBytes(byte[] bytes)
    {
        var memory = Marshal.AllocHGlobal(bytes.Length);
        Marshal.Copy(bytes, 0, memory, bytes.Length);

        if (_wavMemory != IntPtr.Zero) Marshal.FreeHGlobal(_wavMemory);
        _wavMemory = memory;
    }

    private static void FreeWav()
    {
        if (_wavMemory == IntPtr.Zero) return;
        Marshal.FreeHGlobal(_wavMemory);
        _wavMemory = IntPtr.Zero;
        _source = Source.None;
        _loadedPath = null;
    }

    private static void OpenMci(string path, DateTime stamp)
    {
        int rc = mciSendString($"open \"{path}\" type mpegvideo alias {MciAlias}", null, 0, IntPtr.Zero);
        if (rc != 0)
        {
            SimpleLog.Write($"打开音效文件失败（MCI 错误码 {rc}）：{path}");
            // 打不开外置 mp3 也不能没声音，退回内置
            LoadEmbeddedWav();
            return;
        }

        _mp3Open = true;
        _source = Source.ExternalMp3;
        _loadedPath = path;
        _loadedStamp = stamp;
    }

    private static void CloseMci()
    {
        if (!_mp3Open) return;
        mciSendString($"close {MciAlias}", null, 0, IntPtr.Zero);
        _mp3Open = false;
        if (_source == Source.ExternalMp3)
        {
            _source = Source.None;
            _loadedPath = null;
        }
    }

    private static bool IsMciPlaying()
    {
        var buffer = new StringBuilder(64);
        if (mciSendString($"status {MciAlias} mode", buffer, buffer.Capacity, IntPtr.Zero) != 0)
            return false;
        return buffer.ToString().Trim().Equals("playing", StringComparison.OrdinalIgnoreCase);
    }
}
