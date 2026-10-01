using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace OverTranslate.Services.Audio;

/// <summary>
/// Captures the system's output audio (what the speakers are playing — a game, a video, anything),
/// resamples it to 16kHz mono float32, and hands it to <paramref name="onFrame"/> in short frames
/// suitable for <see cref="VadSegmenter"/>.
/// </summary>
/// <remarks>
/// This is loopback capture (WASAPI shared-mode loopback), not microphone input — the distinction
/// that matters for 系統音訊翻譯: it reads what the machine is playing, not what the user is saying.
/// A future "translate what I say too" feature is a second, separate capture (<c>WasapiCapture</c>
/// against the default input device) mixed in alongside this one; it is not this class's job.
///
/// NAudio's WASAPI format negotiation depends on the active output device (commonly 48kHz stereo
/// IEEE float, but not guaranteed), so the resample/downmix step below reads <see cref="WaveFormat"/>
/// off the capture object rather than assuming a fixed input shape.
/// </remarks>
internal sealed class SystemAudioCapture : IDisposable
{
    private const int TargetSampleRate = 16_000;
    private const int FrameMilliseconds = 20;

    private readonly WasapiLoopbackCapture _capture;
    private readonly MediaFoundationResampler _resampler;
    private readonly BufferedWaveProvider _sourceBuffer;
    private readonly Action<float[], TimeSpan> _onFrame;
    private readonly int _frameSampleCount;
    private readonly List<float> _pending = [];

    public SystemAudioCapture(Action<float[], TimeSpan> onFrame)
    {
        _onFrame = onFrame;
        _capture = new WasapiLoopbackCapture();

        _sourceBuffer = new BufferedWaveProvider(_capture.WaveFormat)
        {
            // Loopback delivers in real time; a deep buffer here would only add latency to
            // something that is supposed to feel close to live.
            BufferDuration = TimeSpan.FromSeconds(2),
            DiscardOnBufferOverflow = true,
        };

        // Downmix-to-mono + resample-to-16kHz in one step. MediaFoundationResampler requires the
        // process to have called MediaFoundationApi.Startup() once — see AudioTranslationSession's
        // static constructor.
        var targetFormat = WaveFormat.CreateIeeeFloatWaveFormat(TargetSampleRate, 1);
        _resampler = new MediaFoundationResampler(_sourceBuffer, targetFormat)
        {
            ResamplerQuality = 60,
        };

        _frameSampleCount = TargetSampleRate * FrameMilliseconds / 1000;
        _capture.DataAvailable += OnDataAvailable;
    }

    public void Start() => _capture.StartRecording();

    public void Stop() => _capture.StopRecording();

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        _sourceBuffer.AddSamples(e.Buffer, 0, e.BytesRecorded);

        // Drain whatever the resampler now has ready. The output byte count is not a fixed
        // multiple of the input — that's the nature of resampling — so read until empty rather
        // than assuming one input chunk produces exactly one output chunk.
        var readBuffer = new byte[8192];
        int bytesRead;
        while ((bytesRead = _resampler.Read(readBuffer, 0, readBuffer.Length)) > 0)
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
        _capture.Dispose();
        _resampler.Dispose();
    }
}
