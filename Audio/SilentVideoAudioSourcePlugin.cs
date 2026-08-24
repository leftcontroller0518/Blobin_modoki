using FFmpeg.AutoGen;
using System.IO;
using YukkuriMovieMaker.Plugin;
using YukkuriMovieMaker.Plugin.FileSource;
using YukkuriMovieMaker.Plugin.Update;

namespace Blobin.Audio;

/// <summary>
/// 音声ストリームを持たない動画に対して、長さだけを持つ無音音声を提供する。
///
/// YMM4のMediaFoundation音声ソースは、無音動画でもトラック0を選択して
/// MF_E_INVALIDSTREAMNUMBER (0xC00D36B3) を投げることがあるため、先にこの
/// プラグインで正常な空音声ソースへ置き換える。
/// </summary>
public sealed class SilentVideoAudioSourcePlugin : IAudioFileSourcePlugin
{
    const int DefaultHz = 48_000;
    static readonly object ffmpegLock = new();
    static bool ffmpegInitialized;
    static bool ffmpegInitializationFailed;

    public string Name => "Blobin 無音動画音声回避";

    public PluginDetailsAttribute Details { get; } = new()
    {
        AuthorName = "Blobin",
        ContentId = "Blobin.SilentVideoAudioSource",
    };

    public IPluginUpdater? Updater => null;

    public IAudioFileSource? CreateAudioFileSource(string filePath, int audioTrackIndex)
    {
        BlobinDebug.Log($"Silent audio source probe: path=\"{filePath}\" track={audioTrackIndex}");

        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            return null;

        // FFmpegで音声ストリームの有無を検査する。検査不能なファイルは
        // 既存の読み込みプラグインへ任せ、Blobinが音声を奪わないようにする。
        if (!TryGetMediaInfo(filePath, out var duration, out var hasAudio))
            return null;

        if (hasAudio)
            return null;

        BlobinDebug.Log($"Silent audio source selected: path=\"{filePath}\" track={audioTrackIndex} duration={duration}");
        return new SilentAudioFileSource(duration, DefaultHz);
    }

    static unsafe bool TryGetMediaInfo(string filePath, out TimeSpan duration, out bool hasAudio)
    {
        duration = TimeSpan.Zero;
        hasAudio = false;

        try
        {
            EnsureFFmpegInitialized();

            AVFormatContext* format = null;
            var result = ffmpeg.avformat_open_input(&format, filePath, null, null);
            if (result < 0 || format is null)
                return false;

            try
            {
                result = ffmpeg.avformat_find_stream_info(format, null);
                if (result < 0)
                    return false;

                if (format->duration > 0)
                    duration = TimeSpan.FromSeconds(format->duration / (double)ffmpeg.AV_TIME_BASE);

                for (uint i = 0; i < format->nb_streams; i++)
                {
                    if (format->streams[i]->codecpar->codec_type == AVMediaType.AVMEDIA_TYPE_AUDIO)
                    {
                        hasAudio = true;
                        break;
                    }
                }

                return true;
            }
            finally
            {
                ffmpeg.avformat_close_input(&format);
            }
        }
        catch (Exception ex)
        {
            BlobinDebug.Log($"Silent audio probe failed: {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    static void EnsureFFmpegInitialized()
    {
        if (ffmpegInitialized)
            return;
        if (ffmpegInitializationFailed)
            throw new InvalidOperationException("FFmpeg bindings could not be initialized.");

        lock (ffmpegLock)
        {
            if (ffmpegInitialized)
                return;
            if (ffmpegInitializationFailed)
                throw new InvalidOperationException("FFmpeg bindings could not be initialized.");

            try
            {
                var rootPath = Path.Combine(AppContext.BaseDirectory, "Resources", "bin", "x64", "ffmpeg");
                ffmpeg.RootPath = rootPath;
                DynamicallyLoadedBindings.Initialize();
                ffmpeg.avformat_network_init();
                ffmpegInitialized = true;
            }
            catch
            {
                ffmpegInitializationFailed = true;
                throw;
            }
        }
    }

    sealed class SilentAudioFileSource : IAudioFileSource
    {
        readonly object syncRoot = new();
        readonly TimeSpan duration;
        readonly int hz;
        TimeSpan position;

        public SilentAudioFileSource(TimeSpan duration, int hz)
        {
            this.duration = duration > TimeSpan.Zero ? duration : TimeSpan.FromDays(365);
            this.hz = hz;
        }

        public TimeSpan Duration => duration;
        public int Hz => hz;

        public void Seek(TimeSpan time)
        {
            lock (syncRoot)
                position = Clamp(time);
        }

        public int Read(float[] buffer, int offset, int count)
        {
            if (buffer is null)
                throw new ArgumentNullException(nameof(buffer));
            if (offset < 0 || count < 0 || offset > buffer.Length - count)
                throw new ArgumentOutOfRangeException();

            lock (syncRoot)
            {
                if (position >= duration)
                    return 0;

                var remaining = (long)Math.Ceiling((duration - position).TotalSeconds * hz);
                var read = (int)Math.Min(count, Math.Max(0, remaining));
                Array.Clear(buffer, offset, read);
                position += TimeSpan.FromSeconds(read / (double)hz);
                return read;
            }
        }

        public void Dispose()
        {
        }

        TimeSpan Clamp(TimeSpan time) => time < TimeSpan.Zero ? TimeSpan.Zero : time > duration ? duration : time;
    }
}
