using System;
using System.Text;
using GameKit.Interaction;
using Godot;

namespace GameKit.FpsController;

/// <summary>
/// F3: text readout of movement state. F4: in-world lines for the capsule, velocity, floor normal and
/// step probe. Both cost nothing while hidden; the text refreshes at 10 Hz with a reused StringBuilder.
/// </summary>
public partial class PlayerDebugOverlay : CanvasLayer
{
    private const double RefreshInterval = 0.1;

    [Export] public NodePath PlayerPath { get; set; } = new();

    private FpsPlayerController _player = null!;
    private Label _label = null!;
    private MeshInstance3D _lines = null!;
    private ImmediateMesh _mesh = null!;
    private readonly StringBuilder _sb = new(1024);
    private double _refresh;
    private double _tickUsAverage;
    private long _lastAllocated;
    private long _allocatedPerSecond;
    private int _gen0Start;

    public override void _Ready()
    {
        _player = GetNode<FpsPlayerController>(PlayerPath);
        _label = new Label { Position = new Vector2(12, 12) };
        _label.AddThemeFontSizeOverride("font_size", 15);
        _label.AddThemeColorOverride("font_shadow_color", Colors.Black);
        _label.AddThemeConstantOverride("shadow_offset_x", 1);
        _label.AddThemeConstantOverride("shadow_offset_y", 1);
        AddChild(_label);

        _mesh = new ImmediateMesh();
        var material = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            VertexColorUseAsAlbedo = true,
            NoDepthTest = true,
        };
        _lines = new MeshInstance3D { Mesh = _mesh, MaterialOverride = material, Visible = false, TopLevel = true };
        _lines.PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Off;
        _player.CallDeferred(Node.MethodName.AddChild, _lines);
        _gen0Start = GC.CollectionCount(0);
        _lastAllocated = GC.GetTotalAllocatedBytes();
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (e.IsActionPressed(InputActions.DebugOverlay))
            _label.Visible = !_label.Visible;
        else if (e.IsActionPressed(InputActions.DebugDraw))
            _lines.Visible = !_lines.Visible;
    }

    public override void _Process(double delta)
    {
        _tickUsAverage = Mathf.Lerp(_tickUsAverage, _player.LastTickMicroseconds, 0.05);
        if (_lines.Visible)
            DrawLines();
        _refresh -= delta;
        if (!_label.Visible || _refresh > 0)
            return;
        long allocated = GC.GetTotalAllocatedBytes();
        _allocatedPerSecond = (long)((allocated - _lastAllocated) / (RefreshInterval - _refresh));
        _lastAllocated = allocated;
        _refresh = RefreshInterval;
        _label.Text = BuildText();
    }

    private string BuildText()
    {
        var m = _player.Motor;
        var s = _player.Movement;
        float slope = Mathf.RadToDeg(Mathf.Acos(Mathf.Clamp(m.FloorNormal.Y, -1f, 1f)));
        _sb.Clear();
        _sb.Append("Profile   ").Append(s.ProfileName).Append("  |  Camera ").Append(_player.CameraSettings.PresetName).Append('\n');
        _sb.Append("State     ").Append(m.ActiveAbility?.StateName ?? m.State.ToString());
        if (m.Stance.IsCrouched) _sb.Append(" +Crouch");
        if (m.Stance.IsStandBlocked) _sb.Append(" (stand blocked)");
        if (m.IsSprinting) _sb.Append(" +Sprint");
        _sb.Append('\n');
        _sb.Append("H speed   ").Append(m.HorizontalVelocity.Length().ToString("0.00")).Append(" m/s\n");
        _sb.Append("V speed   ").Append(m.Velocity.Y.ToString("0.00")).Append(" m/s\n");
        _sb.Append("Accel     ").Append(m.HorizontalAcceleration.Length().ToString("0.0")).Append(" m/s²\n");
        _sb.Append("Grounded  ").Append(m.IsOnFloor).Append("   normal ").Append(m.FloorNormal.ToString("0.00")).Append("  slope ").Append(slope.ToString("0.0")).Append("°\n");
        _sb.Append("Surface   ").Append(m.Surface.Type).Append('\n');
        _sb.Append("Jump buf  ").Append(m.JumpBuffered).Append("   coyote ").Append(m.CoyoteAvailable).Append('\n');
        _sb.Append("Platform  ").Append(m.PlatformVelocity.ToString("0.0")).Append('\n');
        _sb.Append("Physics   ").Append(Engine.PhysicsTicksPerSecond).Append(" Hz   FPS ").Append(Engine.GetFramesPerSecond()).Append("   cap ").Append(Engine.MaxFps).Append('\n');
        _sb.Append("Tick cost ").Append(_tickUsAverage.ToString("0.0")).Append(" µs   step queries ").Append(m.Steps.QueriesThisTick).Append('\n');
        _sb.Append("GC        ").Append((_allocatedPerSecond / 1024).ToString()).Append(" KB/s   gen0 ").Append(GC.CollectionCount(0) - _gen0Start).Append('\n');
        _sb.Append("Engine    ").Append(ProjectSettings.GetSetting("physics/3d/physics_engine").AsString());
        return _sb.ToString();
    }

    private void DrawLines()
    {
        var m = _player.Motor;
        var origin = _player.GetGlobalTransformInterpolated().Origin;
        float r = _player.Movement.Radius;
        float h = m.Stance.Height;
        _mesh.ClearSurfaces();
        _mesh.SurfaceBegin(Mesh.PrimitiveType.Lines);

        var capsuleColor = m.IsOnFloor ? Colors.Green : Colors.Orange;
        const int Segments = 16;
        for (int i = 0; i < Segments; i++)
        {
            float a0 = i * Mathf.Tau / Segments, a1 = (i + 1) * Mathf.Tau / Segments;
            var p0 = new Vector3(Mathf.Cos(a0) * r, 0f, Mathf.Sin(a0) * r);
            var p1 = new Vector3(Mathf.Cos(a1) * r, 0f, Mathf.Sin(a1) * r);
            foreach (float y in stackalloc float[] { r, h - r })
                Line(origin + p0 + Vector3.Up * y, origin + p1 + Vector3.Up * y, capsuleColor);
            if (i % 4 == 0)
                Line(origin + p0 + Vector3.Up * r, origin + p0 + Vector3.Up * (h - r), capsuleColor);
        }

        var center = origin + Vector3.Up * 0.05f;
        Line(center, center + m.Velocity * 0.25f, Colors.Cyan);
        Line(center, center + m.FloorNormal, Colors.Magenta);
        var probe = m.Steps.LastProbePoint;
        var probeColor = m.Steps.LastProbeAccepted ? Colors.Yellow : Colors.Red;
        Line(probe - Vector3.Right * 0.1f, probe + Vector3.Right * 0.1f, probeColor);
        Line(probe - Vector3.Back * 0.1f, probe + Vector3.Back * 0.1f, probeColor);

        var interaction = _player.GetNodeOrNull<InteractionRay>("Interaction");
        if (interaction != null)
            Line(interaction.RayFrom, interaction.RayTo, interaction.Focused != null ? Colors.White : Colors.Gray);

        _mesh.SurfaceEnd();
    }

    private void Line(Vector3 a, Vector3 b, Color color)
    {
        _mesh.SurfaceSetColor(color);
        _mesh.SurfaceAddVertex(a);
        _mesh.SurfaceSetColor(color);
        _mesh.SurfaceAddVertex(b);
    }
}
