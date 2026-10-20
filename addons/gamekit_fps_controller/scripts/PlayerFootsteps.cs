using GameKit.Core;
using Godot;

namespace GameKit.FpsController;

/// <summary>
/// Movement audio: surface-aware footsteps driven by distance travelled on the ground (so cadence
/// follows actual speed and stops when stationary), plus jump, land, slide and crouch sounds.
/// All streams are optional; when unassigned, synthesized placeholders are used. Set Enabled false to mute.
/// </summary>
public partial class PlayerFootsteps : Node
{
    [Export] public bool Enabled { get; set; } = true;
    /// <summary>Stride (m) at walking pace; scales up with speed.</summary>
    [Export] public float BaseStride { get; set; } = 0.75f;
    [Export] public float StridePerSpeed { get; set; } = 0.1f;
    [Export] public float CrouchVolumeDb { get; set; } = -10f;
    [Export] public AudioStream? FootstepOverride { get; set; }
    [Export] public AudioStream? LandOverride { get; set; }
    [Export] public AudioStream? JumpOverride { get; set; }

    private FpsPlayerController _player = null!;
    private AudioStreamPlayer _steps = null!;
    private AudioStreamPlayer _body = null!;
    private AudioStreamPlayer _slide = null!;
    private float _distance;
    private readonly RandomNumberGenerator _rng = new();

    public override void _Ready()
    {
        _player = GetParent<FpsPlayerController>();
        _steps = new AudioStreamPlayer { MaxPolyphony = 3 };
        _body = new AudioStreamPlayer { MaxPolyphony = 2 };
        _slide = new AudioStreamPlayer { Stream = PlaceholderSounds.Loop(0.35f, 0.25f), VolumeDb = -8f };
        AddChild(_steps);
        AddChild(_body);
        AddChild(_slide);

        var motor = _player.Motor;
        motor.Landed += OnLanded;
        motor.Jumped += () => Play(_body, JumpOverride ?? PlaceholderSounds.Burst(0.12f, 0.3f, 30f, 0.35f), 1f, -6f);
        motor.StateChanged += (_, state) =>
        {
            if (!Enabled) return;
            if (state == MovementState.Sliding) _slide.Play();
            else _slide.Stop();
        };
        motor.Stance.Changed += (crouched, _) =>
            Play(_body, PlaceholderSounds.Burst(0.18f, 0.1f, 14f, 0.15f), crouched ? 0.8f : 1f, -14f);
    }

    public override void _PhysicsProcess(double delta)
    {
        var motor = _player.Motor;
        if (motor.State != MovementState.Grounded)
        {
            // Restart the stride on landing so the landing sound is not doubled by a step.
            _distance = 0f;
            return;
        }
        float speed = motor.HorizontalVelocity.Length();
        if (speed < 0.5f)
        {
            _distance = Mathf.Max(_distance - (float)delta * 2f, 0f);
            return;
        }
        _distance += speed * (float)delta;
        float stride = BaseStride + speed * StridePerSpeed;
        if (_distance >= stride)
        {
            _distance -= stride;
            PlayFootstep(motor.Surface, speed);
        }
    }

    private void PlayFootstep(SurfaceInfo surface, float speed)
    {
        var stream = FootstepOverride ?? PlaceholderSounds.Burst(0.09f, surface.FootstepBrightness, 38f, 0.4f);
        float volume = Mathf.LinearToDb(Mathf.Clamp(speed / _player.Movement.RunSpeed, 0.35f, 1.3f)) - 4f;
        if (_player.Motor.Stance.IsCrouched)
            volume += CrouchVolumeDb;
        Play(_steps, stream, surface.FootstepPitch * _rng.RandfRange(0.9f, 1.1f), volume);
    }

    private void OnLanded(float impactSpeed)
    {
        if (impactSpeed < 1.5f)
            return;
        var surface = _player.Motor.Surface;
        var stream = LandOverride ?? PlaceholderSounds.Burst(0.22f, surface.FootstepBrightness * 0.6f, 16f, 0.6f);
        float volume = Mathf.LinearToDb(Mathf.Clamp(impactSpeed / 8f, 0.3f, 1.2f));
        Play(_body, stream, surface.FootstepPitch * 0.8f, volume);
        _distance = 0f;
    }

    private void Play(AudioStreamPlayer player, AudioStream stream, float pitch, float volumeDb)
    {
        if (!Enabled)
            return;
        player.Stream = stream;
        player.PitchScale = pitch;
        player.VolumeDb = volumeDb;
        player.Play();
    }
}
