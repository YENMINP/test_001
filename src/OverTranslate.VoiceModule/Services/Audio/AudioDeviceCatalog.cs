using NAudio.CoreAudioApi;

namespace OverTranslate.Services.Audio;

/// <summary>One selectable audio device — Id is what gets stored/passed around, Name is display-only.</summary>
public sealed record AudioDeviceOption(string Id, string Name)
{
    /// <summary>Null Id means "system default" — resolved at capture-start time, not pinned to a
    /// specific device that might be unplugged later.</summary>
    public static AudioDeviceOption Default(string label) => new("", label);
}

/// <summary>
/// Lists the devices <see cref="Views.Voice.VoicePage"/>'s 步驟2 device picker offers, depending on
/// 步驟1's mic-vs-system-audio choice.
/// </summary>
internal static class AudioDeviceCatalog
{
    /// <param name="microphoneMode">True to list capture (microphone/line-in) devices; false to list
    /// render (speaker/output) devices — the ones system-audio loopback can be pointed at.</param>
    public static List<AudioDeviceOption> List(bool microphoneMode)
    {
        var flow = microphoneMode ? DataFlow.Capture : DataFlow.Render;
        var result = new List<AudioDeviceOption>
        {
            AudioDeviceOption.Default(microphoneMode ? "系統預設麥克風" : "系統預設輸出裝置"),
        };

        using var enumerator = new MMDeviceEnumerator();
        foreach (var device in enumerator.EnumerateAudioEndPoints(flow, DeviceState.Active))
        {
            result.Add(new AudioDeviceOption(device.ID, device.FriendlyName));
            device.Dispose();
        }

        return result;
    }

    /// <summary>Resolves a stored device Id back to an <see cref="MMDevice"/>, or the flow's current
    /// default when <paramref name="deviceId"/> is null/empty or no longer exists (device unplugged
    /// since it was chosen) — falling back quietly rather than failing to start.</summary>
    public static MMDevice Resolve(string? deviceId, bool microphoneMode)
    {
        using var enumerator = new MMDeviceEnumerator();
        var flow = microphoneMode ? DataFlow.Capture : DataFlow.Render;

        if (!string.IsNullOrEmpty(deviceId))
        {
            try { return enumerator.GetDevice(deviceId); }
            catch (Exception) { /* fall through to default — see remarks above */ }
        }

        return enumerator.GetDefaultAudioEndpoint(flow, Role.Multimedia);
    }
}
