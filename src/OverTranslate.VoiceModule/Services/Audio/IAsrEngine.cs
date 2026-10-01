namespace OverTranslate.Services.Audio;

/// <summary>Speech-to-text over one already-VAD-segmented utterance.</summary>
internal interface IAsrEngine
{
    /// <param name="pcm16kMono">One complete spoken segment, 16kHz mono float32 — see <see cref="VadSegmenter"/>.</param>
    /// <param name="languageHint">An ISO 639-1 code, or "auto" to let the model decide per segment.</param>
    Task<string> TranscribeAsync(float[] pcm16kMono, string languageHint, CancellationToken cancellationToken = default);
}
