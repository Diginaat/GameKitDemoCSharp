using Godot;

namespace GameKit.Core;

public enum SurfaceType
{
    Default,
    Concrete,
    Metal,
    Wood,
    Grass,
    Ice,
    Mud,
}

/// <param name="Friction">Multiplies ground friction and acceleration (Source surfaceFriction).</param>
/// <param name="SpeedMultiplier">Multiplies wish speed.</param>
/// <param name="FootstepPitch">Pitch of the placeholder footstep sound.</param>
/// <param name="FootstepBrightness">0 = dull thud, 1 = bright click (placeholder synthesis).</param>
public readonly record struct SurfaceInfo(SurfaceType Type, float Friction, float SpeedMultiplier, float FootstepPitch, float FootstepBrightness);

/// <summary>
/// Surface lookup. A collider declares its surface with integer metadata <see cref="MetaKey"/>
/// (a <see cref="SurfaceType"/> value); colliders without it are <see cref="SurfaceType.Default"/>.
/// </summary>
public static class Surfaces
{
    public static readonly StringName MetaKey = "surface";

    private static readonly SurfaceInfo[] Table =
    {
        new(SurfaceType.Default, 1.0f, 1.0f, 1.0f, 0.5f),
        new(SurfaceType.Concrete, 1.0f, 1.0f, 1.0f, 0.55f),
        new(SurfaceType.Metal, 1.0f, 1.0f, 1.35f, 0.95f),
        new(SurfaceType.Wood, 1.0f, 1.0f, 0.8f, 0.4f),
        new(SurfaceType.Grass, 1.0f, 1.0f, 0.7f, 0.15f),
        new(SurfaceType.Ice, 0.12f, 1.0f, 1.2f, 0.8f),
        new(SurfaceType.Mud, 1.4f, 0.65f, 0.55f, 0.05f),
    };

    public static SurfaceInfo Get(SurfaceType type) => Table[(int)type];

    public static SurfaceInfo FromCollider(GodotObject? collider)
    {
        if (collider is Node node && node.HasMeta(MetaKey))
        {
            int value = node.GetMeta(MetaKey).AsInt32();
            if (value >= 0 && value < Table.Length)
                return Table[value];
        }
        return Table[0];
    }

    public static void Tag(Node node, SurfaceType type) => node.SetMeta(MetaKey, (int)type);
}
