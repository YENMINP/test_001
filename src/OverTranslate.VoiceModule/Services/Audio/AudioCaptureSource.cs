using NAudio.CoreAudioApi;
using NAudio.Wave;
using NLog;

namespace OverTranslate.Services.Audio;

/// <summary>
/// Captures either the microphone or the system's output audio (步驟1's choice), from either the
/// default device or one <see cref="Views.Voice.VoicePage"/>'s 步驟2 picker named, and hands
/// 16kHz mono float32 frames to <paramref name="onFrame"/> — see <see cref="VadSegmenter"/>.
/// </summary>
/// <remarks>
/// Replaces the loopback-only <c>SystemAudioCapture</c> this project started with. Both
/// <see cref="WasapiCapture"/> (microphone) and <see cref="WasapiLoopbackCapture"/> (system audio)
/// implement <see cref="IWaveIn"/> with the same shape (<c>DataAvailable</c>, <c>WaveFormat</c>,
/// <c>StartRecording</c>/<c>StopRecording</c>), so one class here handles both — which one gets
/// constructed is the only branch, in <see cref="Create"/>.
/// </remarks>
internal sealed class AudioCaptureSource : IDisposable
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    private const int TargetSampleRate = 16_000;
    private const int FrameMilliseconds = 20;

    private readonly IWaveIn _capture;
    private readonly WasapiOut? _keepAliveOut;
    private readonly MediaFoundationResampler _resampler;
    private readonly BufferedWaveProvider _sourceBuffer;
    private readonly Action<float[], TimeSpan> _onFrame;
    private readonly int _frameSampleCount;
    private readonly List<float> _pending = [];
    private bool _loggedFirstFrame;

    /// <summary>
    /// Fired when WASAPI stops the capture on its own — a bad device state, the endpoint
    /// disappearing, exclusive-mode conflict, etc. Previously nothing observed
    /// <see cref="IWaveIn.RecordingStopped"/> at all, so a capture that died this way looked from
    /// the outside exactly like one that was simply never receiving audio: no frames, no error,
    /// nothing in the log.
    /// </summary>
    public event Action<Exception>? OnCaptureFailed;

    /// <param name="microphoneMode">Mirrors 步驟1: true captures the microphone, false captures
    /// system-output loopback.</param>
    /// <param name="deviceId">A device Id from <see cref="AudioDeviceCatalog.List"/>, or null/empty
    /// for the system default (步驟2's first entry).</param>
    public static AudioCaptureSource Create(
        bool microphoneMode, string? deviceId, Action<float[], TimeSpan> onFrame)
    {
        var device = AudioDeviceCatalog.Resolve(deviceId, microphoneMode);
        Log.Info(
            "Resolved audio device: {Name} (mode: {Mode})",
            device.FriendlyName, microphoneMode ? "microphone" : "system-loopback");

        IWaveIn capture = microphoneMode
            ? new WasapiCapture(device)
            : new WasapiLoopbackCapture(device);

        WasapiOut? keepAliveOut = null;
        if (!microphoneMode)
        {
            // WASAPI's audio engine only keeps pushing packets to a loopback capture while the
            // render endpoint has an active stream; once nothing has asked it to render anything
            // for a moment, Windows lets the endpoint go idle and the loopback capture can stop
            // receiving real buffers — which reads, from here, exactly like "no audio ever arrived"
            // (one empty first callback, then silence forever, independent of whatever the user
            // is actually playing through the speaker). A second client on the same endpoint,
            // playing silence for as long as the session runs, keeps the engine from ever going
            // idle. Deliberately a second MMDevice handle rather than reusing `device`: each WASAPI
            // client here needs its own IAudioClient, and WasapiLoopbackCapture takes ownership of
            // the one it was constructed with.
            var outputDevice = AudioDeviceCatalog.Resolve(deviceId, microphoneMode);
            keepAliveOut = new WasapiOut(outputDevice, AudioClientShareMode.Shared, true, 100);
            keepAliveOut.Init(new SilenceProvider(capture.WaveFormat));
        }

        return new AudioCaptureSource(capture, keepAliveOut, onFrame);
    }

    private AudioCaptureSource(IWaveIn capture, WasapiOut? keepAliveOut, Action<float[], TimeSpan> onFrame)
    {
        _capture = capture;
        _keepAliveOut = keepAliveOut;
        _onFrame = onFrame;

        Log.Info(
            "Capture wave format: {SampleRate}Hz, {Channels}ch, {Bits}bit ({Encoding})",
            capture.WaveFormat.SampleRate, capture.WaveFormat.Channels,
            capture.WaveFormat.BitsPerSample, capture.WaveFormat.Encoding);

        _sourceBuffer = new BufferedWaveProvider(_capture.WaveFormat)
        {
            // Both capture kinds deliver close to real time; a deep buffer here would only add
            // latency to something that is supposed to feel close to live.
            BufferDuration = TimeSpan.FromSeconds(2),
            DiscardOnBufferOverflow = true,
            // BufferedWaveProvider defaults this to true, which makes Read() always return exactly
            // the requested count — padding with silence once real data runs out — rather than
            // returning less (or zero) when there isn't enough buffered yet. With that default, the
            // drain loop below never sees a 0 and never exits; see its remarks.
            ReadFully = false,
        };

        // Downmix-to-mono + resample-to-16kHz in one step. Requires MediaFoundationApi.Startup() to
        // have run once already — see AudioTranslationSession's static constructor.
        var targetFormat = WaveFormat.CreateIeeeFloatWaveFormat(TargetSampleRate, 1);
        _resampler = new MediaFoundationResampler(_sourceBuffer, targetFormat)
        {
            ResamplerQuality = 60,
        };

        _frameSampleCount = TargetSampleRate * FrameMilliseconds / 1000;
        _capture.DataAvailable += OnDataAvailable;
        _capture.RecordingStopped += OnRecordingStopped;
    }

    public void Start()
    {
        // Order matters: the keep-alive stream needs to already be pushing silence before the
        // loopback client starts, not just running eventually — see its remarks in Create.
        _keepAliveOut?.Play();
        _capture.StartRecording();
    }

    public void Stop()
    {
        _capture.StopRecording();
        _keepAliveOut?.Stop();
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        if (e.Exception is not null)
        {
            Log.Error(e.Exception, "Audio capture stopped unexpectedly");
            OnCaptureFailed?.Invoke(e.Exception);
        }
        else
        {
            Log.Info("Audio capture stopped");
        }
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        if (!_loggedFirstFrame)
        {
            _loggedFirstFrame = true;
            Log.Info("First capture buffer received: {Bytes} bytes", e.BytesRecorded);
        }

        _sourceBuffer.AddSamples(e.Buffer, 0, e.BytesRecorded);

        // Drain whatever the resampler now has ready. The output byte count is not a fixed
        // multiple of the input — that's the nature of resampling — so read until empty rather
        // than assuming one input chunk produces exactly one output chunk. This loop is only safe
        // to write this way because _sourceBuffer.ReadFully = false above makes Read() return 0
        // once real data runs out, instead of padding forever — the WASAPI capture thread is
        // blocked inside this callback for as long as this loop runs, so a version that never hits
        // 0 hangs capture completely after its very first call (this shipped that way once
        // already: every subsequent DataAvailable callback silently never fired, which looked from
        // the outside like "no audio ever arrives" regardless of what the source device is
        // actually playing). maxIterations is a second, independent guard against that same class
        // of bug recurring, not just a reliance on ReadFully being set correctly above.
        var readBuffer = new byte[8192];
        int bytesRead;
        int iterations = 0;
        const int maxIterations = 64; // 64 * 8192B of 16kHz mono float32 is >4s drained in one callback — generous
        while ((bytesRead = _resampler.Read(readBuffer, 0, readBuffer.Length)) > 0 && ++iterations <= maxIterations)
        {
            int sampleCount = bytesRead / sizeof(float);
            for (int i = 0; i < sampleCount; i++)
                _pending.Add(BitConverter.ToSingle(readBuffer, i * sizeof(float)));
        }

        while (_pending.Count >= _frameSampleCount)
        {
            var frame = _pending.GetRange(0, _frameSampleCount).ToArray();
            _pending.RemoveRange(0, _frameSampleCount);
            _onFrame(frame, TimeSpan.FromMilliseconds(FrameMilliseconds));
        }
    }

    public void Dispose()
    {
        _capture.DataAvailable -= OnDataAvailable;
        _capture.RecordingStopped -= OnRecordingStopped;
        _capture.StopRecording();
        _capture.Dispose();
        _keepAliveOut?.Dispose();
        _resampler.Dispose();
    }
}