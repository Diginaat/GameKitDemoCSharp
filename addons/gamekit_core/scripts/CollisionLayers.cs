namespace GameKit.Core;

/// <summary>
/// Physics layer bits the GameKit modules assume (names in Project Settings > Layer Names > 3D Physics).
/// Bodies: world geometry on <see cref="World"/>, the player on <see cref="Player"/> (mask World | Props),
/// rigid props on <see cref="Props"/>. Volumes (water, ladders) sit on <see cref="Triggers"/> and mask the
/// bodies they react to.
/// </summary>
public static class CollisionLayers
{
    /// <summary>Layer 1: static and animated level geometry.</summary>
    public const uint World = 1u << 0;

    /// <summary>Layer 2: player characters.</summary>
    public const uint Player = 1u << 1;

    /// <summary>Layer 3: rigid props (pushable, pickable).</summary>
    public const uint Props = 1u << 2;

    /// <summary>Layer 4: trigger volumes (water, ladders).</summary>
    public const uint Triggers = 1u << 3;
}
