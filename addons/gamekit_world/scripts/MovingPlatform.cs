using Godot;

namespace GameKit.World;

/// <summary>
/// Kinematic platform that oscillates along <see cref="Travel"/> and/or spins about Y. Moves in the
/// physics tick with sync-to-physics, so CharacterBody3D riders get correct platform velocity.
/// </summary>
[GlobalClass]
public partial class MovingPlatform : AnimatableBody3D
{
	[Export] public Vector3 Travel { get; set; } = new(0f, 0f, 6f);
	[Export] public float Period { get; set; } = 6f;
	[Export] public float SpinDegreesPerSecond { get; set; }

	private Transform3D _start;
	private double _time;

	public override void _Ready()
	{
		SyncToPhysics = true;
		_start = Transform;
	}

	public override void _PhysicsProcess(double delta)
	{
		_time += delta;
		float phase = Period > 0f ? (float)(_time / Period * Mathf.Tau) : 0f;
		// Smooth back-and-forth: 0 -> Travel -> 0, reversing gently at each end.
		var offset = Travel * (0.5f - 0.5f * Mathf.Cos(phase));
		var basis = _start.Basis.Rotated(Vector3.Up, Mathf.DegToRad(SpinDegreesPerSecond) * (float)_time);
		Transform = new Transform3D(basis, _start.Origin + offset);
	}
}
