using System;
using System.Threading.Tasks;
using Godot;

namespace GameKit.Weather;

/// <summary>
/// Procedural rain, wind and thunder sound, with no audio files.
///
/// Noise buffers are generated once on a worker thread and shaped with audio bus filters (buses
/// WeatherRain, WeatherWind and WeatherThunder, created on first use and sent to Master).
/// <see cref="WeatherSystem"/> sets <see cref="RainAmount"/> and <see cref="WindAmount"/> each frame and
/// calls <see cref="PlayThunder"/>. To use recorded sounds instead, replace the streams in
/// <c>OnGenerated</c>.
/// </summary>
[GlobalClass]
public partial class WeatherAudio : Node
{
    private const int MixRate = 22050;
    private const float RainSeconds = 3f;
    private const float WindSeconds = 4f;
    private const float ThunderSeconds = 6f;
    private const int ThunderVariants = 3;
    private const int MaxPendingThunder = 4;

    private static readonly StringName RainBus = "WeatherRain";
    private static readonly StringName WindBus = "WeatherWind";
    private static readonly StringName ThunderBus = "WeatherThunder";

    [Export] public float VolumeDb { get; set; } = -4f;

    /// <summary>0..1, rain loudness.</summary>
    public float RainAmount { get; set; }
    /// <summary>0..1, wind loudness including gusts. Also opens the wind filter (whoosh).</summary>
    public float WindAmount { get; set; }

    private AudioStreamPlayer _rainPlayer = null!;
    private AudioStreamPlayer _windPlayer = null!;
    private readonly AudioStreamPlayer[] _thunderPlayers = new AudioStreamPlayer[2];
    private readonly AudioStreamWav[] _thunderStreams = new AudioStreamWav[ThunderVariants];
    private bool _streamsReady;
    private AudioEffectBandPassFilter _windFilter = null!;
    private Task<byte[][]>? _generation;
    // Thunder waiting for its sound delay: (seconds left, strength). Time < 0 = free slot.
    private readonly (float Time, float Strength)[] _pendingThunder = new (float, float)[MaxPendingThunder];

    public override void _Ready()
    {
        EnsureBus(RainBus, new AudioEffectHighPassFilter { CutoffHz = 700f }, new AudioEffectLowPassFilter { CutoffHz = 9000f });
        EnsureBus(WindBus, new AudioEffectBandPassFilter { CutoffHz = 400f, Resonance = 0.8f });
        EnsureBus(ThunderBus, new AudioEffectReverb { RoomSize = 0.95f, Wet = 0.35f });
        _windFilter = (AudioEffectBandPassFilter)AudioServer.GetBusEffect(AudioServer.GetBusIndex(WindBus), 0);

        _rainPlayer = MakePlayer(RainBus);
        _windPlayer = MakePlayer(WindBus);
        for (int i = 0; i < _thunderPlayers.Length; i++)
            _thunderPlayers[i] = MakePlayer(ThunderBus);
        for (int i = 0; i < _pendingThunder.Length; i++)
            _pendingThunder[i].Time = -1f;
        _generation = Task.Run(Generate);
    }

    public override void _ExitTree()
    {
        _generation?.Wait();
        // Release the streams now; a looping playback still held at quit is reported as leaked.
        _rainPlayer.Stop();
        _rainPlayer.Stream = null;
        _windPlayer.Stop();
        _windPlayer.Stream = null;
        foreach (AudioStreamPlayer player in _thunderPlayers)
        {
            player.Stop();
            player.Stream = null;
        }
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        if (_generation is { IsCompleted: true })
        {
            OnGenerated(_generation.Result);
            _generation = null;
        }
        UpdateLoop(_rainPlayer, RainAmount, dt);
        UpdateLoop(_windPlayer, WindAmount, dt);
        _windFilter.CutoffHz = 250f + 1100f * WindAmount;

        for (int i = 0; i < _pendingThunder.Length; i++)
        {
            if (_pendingThunder[i].Time < 0f)
                continue;
            _pendingThunder[i].Time -= dt;
            if (_pendingThunder[i].Time < 0f)
                StartThunder(_pendingThunder[i].Strength);
        }
    }

    /// <summary>Thunder after <paramref name="delay"/> seconds (light travels faster than sound).
    /// <paramref name="strength"/> 0..1: close strikes are louder and sharper.</summary>
    public void PlayThunder(float delay, float strength)
    {
        if (!_streamsReady)
            return;
        for (int i = 0; i < _pendingThunder.Length; i++)
        {
            if (_pendingThunder[i].Time < 0f)
            {
                _pendingThunder[i] = (Mathf.Max(delay, 0f), strength);
                return;
            }
        }
    }

    private void StartThunder(float strength)
    {
        AudioStreamPlayer player = _thunderPlayers[0].Playing ? _thunderPlayers[1] : _thunderPlayers[0];
        player.Stream = _thunderStreams[GD.RandRange(0, ThunderVariants - 1)];
        player.PitchScale = Mathf.Lerp(0.75f, 1.15f, strength) * (float)GD.RandRange(0.95, 1.05);
        player.VolumeDb = VolumeDb + Mathf.LinearToDb(Mathf.Lerp(0.25f, 1f, strength));
        player.Play();
    }

    private void UpdateLoop(AudioStreamPlayer player, float amount, float dt)
    {
        if (player.Stream == null)
            return;
        float target = VolumeDb + Mathf.LinearToDb(Mathf.Max(amount, 0.0001f));
        player.VolumeDb = Mathf.Lerp(player.VolumeDb, target, 1f - Mathf.Exp(-3f * dt));
        if (amount > 0.001f && !player.Playing)
        {
            player.VolumeDb = -60f;
            player.Play();
        }
        else if (amount <= 0.001f && player.Playing && player.VolumeDb < -50f)
        {
            player.Stop();
        }
    }

    private void OnGenerated(byte[][] data)
    {
        _rainPlayer.Stream = MakeStream(data[0], loop: true);
        _windPlayer.Stream = MakeStream(data[1], loop: true);
        for (int i = 0; i < ThunderVariants; i++)
            _thunderStreams[i] = MakeStream(data[2 + i], loop: false);
        _streamsReady = true;
    }

    // --- Generation (worker thread; pure functions of a seeded Random) ---------------------------------

    private static byte[][] Generate()
    {
        var rng = new Random(1234);
        var result = new byte[2 + ThunderVariants][];
        result[0] = GenerateRain(rng);
        result[1] = GenerateWind(rng);
        for (int i = 0; i < ThunderVariants; i++)
            result[2 + i] = GenerateThunder(rng);
        return result;
    }

    private static float Range(Random rng, float min, float max) => min + (float)rng.NextDouble() * (max - min);

    /// <summary>Pink noise (patter of many drops) plus sparse louder close drops.</summary>
    private static byte[] GenerateRain(Random rng)
    {
        int count = (int)(RainSeconds * MixRate);
        var data = new byte[count * 2];
        float b0 = 0f, b1 = 0f, b2 = 0f, drop = 0f;
        for (int i = 0; i < count; i++)
        {
            float white = Range(rng, -1f, 1f);
            b0 = 0.99765f * b0 + white * 0.099046f;
            b1 = 0.963f * b1 + white * 0.2965164f;
            b2 = 0.57f * b2 + white * 1.0526913f;
            float pink = (b0 + b1 + b2 + white * 0.1848f) * 0.11f;
            if (rng.NextDouble() < 0.0015)
                drop = Range(rng, 0.2f, 0.6f);
            drop *= 0.985f;
            Write(data, i, pink + drop * white);
        }
        return data;
    }

    /// <summary>Brown noise; the bus band-pass turns it into wind, and moving the cutoff gives the whoosh.</summary>
    private static byte[] GenerateWind(Random rng)
    {
        int count = (int)(WindSeconds * MixRate);
        var data = new byte[count * 2];
        float brown = 0f;
        for (int i = 0; i < count; i++)
        {
            brown = (brown + 0.02f * Range(rng, -1f, 1f)) / 1.02f;
            Write(data, i, brown * 3.5f);
        }
        return data;
    }

    /// <summary>A sharp crack followed by a long rolling rumble.</summary>
    private static byte[] GenerateThunder(Random rng)
    {
        int count = (int)(ThunderSeconds * MixRate);
        var data = new byte[count * 2];
        float brown = 0f, roll = 0f, rollTarget = 1f;
        for (int i = 0; i < count; i++)
        {
            float t = (float)i / MixRate;
            float white = Range(rng, -1f, 1f);
            brown = (brown + 0.03f * white) / 1.03f;
            // Slowly wandering loudness gives the rolling rumble.
            if (i % 2000 == 0)
                rollTarget = Range(rng, 0.3f, 1f);
            roll = Mathf.Lerp(roll, rollTarget, 0.0004f);
            float crack = white * Mathf.Exp(-t * 30f) * 0.6f;
            float rumble = brown * 6f * Mathf.Min(t * 8f, 1f) * Mathf.Exp(-t / 1.8f) * roll;
            Write(data, i, crack + rumble);
        }
        return data;
    }

    /// <summary>16-bit little-endian PCM sample.</summary>
    private static void Write(byte[] data, int index, float value)
    {
        short s = (short)(Mathf.Clamp(value, -1f, 1f) * 32767f);
        data[index * 2] = (byte)s;
        data[index * 2 + 1] = (byte)(s >> 8);
    }

    // --- Helpers -----------------------------------------------------------------------------------------

    private static AudioStreamWav MakeStream(byte[] data, bool loop)
    {
        var stream = new AudioStreamWav { Format = AudioStreamWav.FormatEnum.Format16Bits, MixRate = MixRate, Data = data };
        if (loop)
        {
            stream.LoopMode = AudioStreamWav.LoopModeEnum.Forward;
            stream.LoopEnd = data.Length / 2;
        }
        return stream;
    }

    private AudioStreamPlayer MakePlayer(StringName bus)
    {
        var player = new AudioStreamPlayer { Bus = bus, VolumeDb = -60f };
        AddChild(player);
        return player;
    }

    /// <summary>Creates the bus (sending to Master) once; reuses it on scene reloads.</summary>
    private static void EnsureBus(StringName name, params AudioEffect[] effects)
    {
        if (AudioServer.GetBusIndex(name) != -1)
            return;
        int index = AudioServer.BusCount;
        AudioServer.AddBus(index);
        AudioServer.SetBusName(index, name);
        AudioServer.SetBusSend(index, "Master");
        foreach (AudioEffect effect in effects)
            AudioServer.AddBusEffect(index, effect);
    }
}
