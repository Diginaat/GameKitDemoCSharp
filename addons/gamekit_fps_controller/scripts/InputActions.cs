using Godot;

namespace GameKit.FpsController;

/// <summary>
/// Action names and default bindings. Defaults are registered at startup only for actions the
/// project's Input Map does not define, so bindings stay editable in Project Settings.
/// </summary>
public static class InputActions
{
    public static readonly StringName MoveForward = "move_forward";
    public static readonly StringName MoveBack = "move_back";
    public static readonly StringName MoveLeft = "move_left";
    public static readonly StringName MoveRight = "move_right";
    public static readonly StringName Jump = "jump";
    public static readonly StringName Crouch = "crouch";
    public static readonly StringName Sprint = "sprint";
    public static readonly StringName Walk = "walk";
    public static readonly StringName Interact = "interact";
    public static readonly StringName Fire = "fire";
    public static readonly StringName AltFire = "alt_fire";
    public static readonly StringName Reload = "reload";
    public static readonly StringName Weapon1 = "weapon_1";
    public static readonly StringName Weapon2 = "weapon_2";
    public static readonly StringName Holster = "holster";
    public static readonly StringName ReleaseMouse = "release_mouse";
    public static readonly StringName DebugOverlay = "debug_overlay";
    public static readonly StringName DebugDraw = "debug_draw";
    public static readonly StringName CycleProfile = "cycle_profile";
    public static readonly StringName ToggleAccessibility = "toggle_accessibility";
    public static readonly StringName CycleTickRate = "cycle_tick_rate";
    public static readonly StringName CycleFpsCap = "cycle_fps_cap";
    public static readonly StringName Respawn = "respawn";

    private static bool _registered;

    public static void EnsureDefaults()
    {
        if (_registered)
            return;
        _registered = true;
        Key(MoveForward, Godot.Key.W);
        Key(MoveBack, Godot.Key.S);
        Key(MoveLeft, Godot.Key.A);
        Key(MoveRight, Godot.Key.D);
        Key(Jump, Godot.Key.Space);
        Key(Crouch, Godot.Key.Ctrl, Godot.Key.C);
        Key(Sprint, Godot.Key.Shift);
        Key(Walk, Godot.Key.Alt);
        Key(Interact, Godot.Key.E);
        Mouse(Fire, MouseButton.Left);
        Mouse(AltFire, MouseButton.Right);
        Key(Reload, Godot.Key.R);
        Key(Weapon1, Godot.Key.Key1);
        Key(Weapon2, Godot.Key.Key2);
        Key(Holster, Godot.Key.Key3);
        Key(ReleaseMouse, Godot.Key.Escape);
        Key(DebugOverlay, Godot.Key.F3);
        Key(DebugDraw, Godot.Key.F4);
        Key(CycleProfile, Godot.Key.F1);
        Key(ToggleAccessibility, Godot.Key.F2);
        Key(CycleTickRate, Godot.Key.F5);
        Key(CycleFpsCap, Godot.Key.F6);
        Key(Respawn, Godot.Key.F9);
    }

    private static void Key(StringName action, params Key[] keys)
    {
        if (InputMap.HasAction(action))
            return;
        InputMap.AddAction(action);
        foreach (var key in keys)
            InputMap.ActionAddEvent(action, new InputEventKey { PhysicalKeycode = key });
    }

    private static void Mouse(StringName action, MouseButton button)
    {
        if (InputMap.HasAction(action))
            return;
        InputMap.AddAction(action);
        InputMap.ActionAddEvent(action, new InputEventMouseButton { ButtonIndex = button });
    }
}
