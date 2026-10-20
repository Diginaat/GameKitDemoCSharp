using Godot;

namespace GameKit.FpsController;

/// <summary>
/// An optional movement mode (slide, mantle, ladder, swim, or your own) that takes over from the base
/// ground/air movement while active. Abilities are nodes under the player's <c>Abilities</c> child; child
/// order is priority (the first whose <see cref="CanBegin"/> passes takes over). Add, remove or reorder
/// them in the editor or at runtime (e.g. unlocking a skill adds its node); the motor follows.
/// Abilities move the body only through <see cref="PlayerMotor.MoveBody"/> (or swept motion of their own).
/// </summary>
public abstract partial class MovementAbility : Node
{
    /// <summary>State reported while active. Custom abilities return <see cref="MovementState.Custom"/>.</summary>
    public abstract MovementState State { get; }

    /// <summary>Name shown in debug tools; defaults to the state, or the node name for custom abilities.</summary>
    public virtual string StateName => State == MovementState.Custom ? Name : State.ToString();

    /// <summary>Checked every tick while no ability is active, in priority order.</summary>
    public abstract bool CanBegin(PlayerMotor motor, in PlayerCommand cmd);

    public abstract void Begin(PlayerMotor motor, in PlayerCommand cmd);

    /// <summary>Simulates one tick, including moving the body. Return false to hand control back.</summary>
    public abstract bool Tick(PlayerMotor motor, in PlayerCommand cmd, float dt);

    public abstract void End(PlayerMotor motor);
}
