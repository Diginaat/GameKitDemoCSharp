using GameKit.FpsController;
using GameKit.Interaction;
using GameKit.Lens;
using GameKit.Viewmodel;
using GameKit.Weather;
using Godot;

namespace GameKit.Demo;

/// <summary>
/// The demo's HUD: crosshair, interaction prompt, ammo, current weather and lens look, a controls panel
/// (H hides it), and muffled audio under water. Also lets the demo switch the lens look (L).
/// It is an example of game UI on top of GameKit; copy and change it for your own game.
/// </summary>
[GlobalClass]
public partial class DemoHud : CanvasLayer
{
    private const string Controls =
        "WASD move    Mouse look    Space jump    Ctrl crouch    Shift sprint    Alt walk\n" +
        "Shift+Ctrl slide    Space+W at a ledge: mantle    E use / pick up    LMB fire / throw    R reload\n" +
        "1 / 2 / 3 weapons    F7 weather    L lens look    H hide help    Esc release mouse";

    public static readonly StringName CycleLensAction = "demo_cycle_lens";
    public static readonly StringName ToggleHelpAction = "demo_toggle_help";

    [Export] public NodePath PlayerPath { get; set; } = new();
    /// <summary>Optional: shows the weather name.</summary>
    [Export] public WeatherSystem? Weather { get; set; }
    /// <summary>Lens looks for the L key, in order.</summary>
    [Export] public Godot.Collections.Array<LensProfile> LensProfiles { get; set; } = new();

    private FpsPlayerController _player = null!;
    private CameraLens? _lens;
    private PlayerViewmodel? _viewmodel;
    private Label _prompt = null!, _ammo = null!, _status = null!;
    private Control _help = null!;
    private int _lensIndex;
    private int _lowPassIndex = -1;

    public override void _Ready()
    {
        AddKey(CycleLensAction, Key.L);
        AddKey(ToggleHelpAction, Key.H);

        _player = GetNode<FpsPlayerController>(PlayerPath);
        _viewmodel = _player.GetNodeOrNull<PlayerViewmodel>("CameraRig/Pitch/Effects/Camera3D/Viewmodel");
        _lens = _player.GetNodeOrNull<CameraLens>("CameraRig/Pitch/Effects/Camera3D/Lens");

        var crosshair = new Label { Text = "+", HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        crosshair.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        crosshair.AddThemeFontSizeOverride("font_size", 22);
        crosshair.Modulate = new Color(1, 1, 1, 0.85f);
        AddChild(crosshair);

        _prompt = new Label { HorizontalAlignment = HorizontalAlignment.Center, Position = new Vector2(0, 42) };
        _prompt.SetAnchorsPreset(Control.LayoutPreset.Center);
        _prompt.GrowHorizontal = Control.GrowDirection.Both;
        Style(_prompt, 19);
        AddChild(_prompt);

        _ammo = new Label { HorizontalAlignment = HorizontalAlignment.Right };
        _ammo.SetAnchorsPreset(Control.LayoutPreset.BottomRight);
        _ammo.GrowHorizontal = Control.GrowDirection.Begin;
        _ammo.GrowVertical = Control.GrowDirection.Begin;
        _ammo.Position -= new Vector2(28, 24);
        Style(_ammo, 22);
        AddChild(_ammo);

        _status = new Label { HorizontalAlignment = HorizontalAlignment.Right };
        _status.SetAnchorsPreset(Control.LayoutPreset.TopRight);
        _status.GrowHorizontal = Control.GrowDirection.Begin;
        _status.Position += new Vector2(-24, 18);
        Style(_status, 16);
        AddChild(_status);

        var panel = new PanelContainer();
        panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.05f, 0.06f, 0.07f, 0.55f),
            ContentMarginLeft = 14, ContentMarginRight = 14, ContentMarginTop = 10, ContentMarginBottom = 10,
            CornerRadiusTopLeft = 8, CornerRadiusTopRight = 8, CornerRadiusBottomLeft = 8, CornerRadiusBottomRight = 8,
        });
        var help = new Label { Text = Controls };
        help.AddThemeFontSizeOverride("font_size", 14);
        help.AddThemeConstantOverride("line_spacing", 4);
        panel.AddChild(help);
        panel.SetAnchorsPreset(Control.LayoutPreset.BottomLeft);
        panel.GrowVertical = Control.GrowDirection.Begin;
        panel.Position += new Vector2(18, -18);
        _help = panel;
        AddChild(panel);

        var interaction = _player.GetNodeOrNull<InteractionRay>("Interaction");
        if (interaction != null)
            interaction.PromptChanged += text => _prompt.Text = text == null ? "" : $"[E]  {text}";

        var swim = _player.Motor.GetAbility<SwimMovement>();
        if (swim != null)
            swim.UnderwaterChanged += SetUnderwater;

        if (_lens?.Profile != null)
            _lensIndex = Mathf.Max(LensProfiles.IndexOf(_lens.Profile), 0);
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (e.IsActionPressed(ToggleHelpAction))
            _help.Visible = !_help.Visible;
        else if (e.IsActionPressed(CycleLensAction) && _lens != null && LensProfiles.Count > 0)
        {
            _lensIndex = (_lensIndex + 1) % LensProfiles.Count;
            _lens.Profile = LensProfiles[_lensIndex];
        }
    }

    public override void _Process(double delta)
    {
        var weapon = _viewmodel?.CurrentWeapon;
        _ammo.Text = weapon == null ? "" : $"{weapon.DisplayName}   {weapon.Ammo} / {weapon.MagazineSize}";
        string weather = Weather != null ? $"Weather  {Weather.WeatherName}" : "";
        string lens = _lens?.Profile != null ? $"Lens  {_lens.Profile.DisplayName}" : "";
        _status.Text = $"{weather}\n{lens}";
    }

    public override void _ExitTree()
    {
        if (_lowPassIndex >= 0)
            AudioServer.RemoveBusEffect(0, _lowPassIndex);
        _lowPassIndex = -1;
    }

    private void SetUnderwater(bool underwater)
    {
        // Muffle the master bus while the eyes are under water.
        if (_lowPassIndex < 0)
        {
            AudioServer.AddBusEffect(0, new AudioEffectLowPassFilter { CutoffHz = 600f });
            _lowPassIndex = AudioServer.GetBusEffectCount(0) - 1;
        }
        AudioServer.SetBusEffectEnabled(0, _lowPassIndex, underwater);
    }

    private static void Style(Label label, int size)
    {
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.7f));
        label.AddThemeConstantOverride("outline_size", 6);
    }

    /// <summary>Adds an input action with a default key unless the project already defines it (rebindable).</summary>
    private static void AddKey(StringName action, Key key)
    {
        if (InputMap.HasAction(action))
            return;
        InputMap.AddAction(action);
        InputMap.ActionAddEvent(action, new InputEventKey { PhysicalKeycode = key });
    }
}
