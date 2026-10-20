using System;
using System.Collections.Generic;
using Godot;

namespace GameKit.Core;

/// <summary>
/// Synthesizes simple placeholder sounds (filtered noise bursts) so movement audio works with no assets.
/// Replace by assigning real streams on <see cref="Player.PlayerFootsteps"/>; nothing else depends on this.
/// </summary>
public static class PlaceholderSounds
{
    private const int MixRate = 22050;
    private static readonly Dictionary<(float, float, float, float), AudioStreamWav> Cache = new();

    /// <summary>Releases cached streams (call before quitting so no engine objects outlive the C# runtime).</summary>
    public static void ClearCache() => Cache.Clear();

    /// <param name="duration">Seconds.</param>
    /// <param name="brightness">0 = dull (heavy low-pass), 1 = bright.</param>
    /// <param name="decay">Envelope decay rate (1/s).</param>
    /// <param name="volume">Peak amplitude 0..1.</param>
    public static AudioStreamWav Burst(float duration, float brightness, float decay, float volume)
    {
        var key = (duration, brightness, decay, volume);
        if (Cache.TryGetValue(key, out var cached))
            return cached;

        int samples = (int)(duration * MixRate);
        var data = new byte[samples * 2];
        var rng = new Random(samples ^ (int)(brightness * 1000));
        // One-pole low-pass; coefficient from brightness.
        float alpha = Mathf.Lerp(0.04f, 0.6f, Mathf.Clamp(brightness, 0f, 1f));
        float filtered = 0f;
        for (int i = 0; i < samples; i++)
        {
            float t = (float)i / MixRate;
            float attack = Mathf.Min(1f, t / 0.004f);
            float envelope = attack * Mathf.Exp(-decay * t);
            float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
            filtered += alpha * (noise - filtered);
            short value = (short)(Mathf.Clamp(filtered * envelope * volume * 2.5f, -1f, 1f) * short.MaxValue);
            data[i * 2] = (byte)(value & 0xFF);
            data[i * 2 + 1] = (byte)((value >> 8) & 0xFF);
        }

        var stream = new AudioStreamWav
        {
            Format = AudioStreamWav.FormatEnum.Format16Bits,
            MixRate = MixRate,
            Stereo = false,
            Data = data,
        };
        Cache[key] = stream;
        return stream;
    }

    /// <summary>Looping noise bed (slides).</summary>
    public static AudioStreamWav Loop(float brightness, float volume)
    {
        var stream = (AudioStreamWav)Burst(1f, brightness, 0f, volume).Duplicate();
        stream.LoopMode = AudioStreamWav.LoopModeEnum.Forward;
        stream.LoopBegin = 0;
        stream.LoopEnd = MixRate;
        return stream;
    }
}
