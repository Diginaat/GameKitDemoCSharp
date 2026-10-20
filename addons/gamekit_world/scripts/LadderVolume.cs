using Godot;

namespace GameKit.World;

/// <summary>
/// Climbable region. The volume's local +Z axis points away from the ladder face toward the climber.
/// Extend the volume slightly above the top ledge so climbers hop off onto it. Bodies that implement
/// <see cref="ILadderUser"/> are told when they enter and leave. Collision mask: the climbers' layer.
/// </summary>
[GlobalClass]
public partial class LadderVolume : Area3D
{
    public Vector3 OutwardNormal => GlobalBasis.Z.Slide(Vector3.Up).Normalized();

    public override void _Ready()
    {
        BodyEntered += body => (body as ILadderUser)?.OnLadderEntered(this);
        BodyExited += body => (body as ILadderUser)?.OnLadderExited(this);
    }
}
