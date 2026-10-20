using GameKit.Water;
using Godot;

namespace GameKit.Demo;

/// <summary>
/// Points a pool_tile.gdshader material at its water: surface height, water rectangle and sun direction.
/// pool_tile works in world space, so this keeps the caustics right wherever the pool scene is placed or turned.
/// </summary>
[Tool, GlobalClass]
public partial class PoolCaustics : Node
{
    [Export] public WaterVolume? Water { get; set; }
    /// <summary>The pool_tile.gdshader material on the pool walls and floor.</summary>
    [Export] public ShaderMaterial? TileMaterial { get; set; }

    public override void _Ready()
    {
        if (Water == null || TileMaterial == null)
            return;

        foreach (var child in Water.GetChildren())
        {
            if (child is not CollisionShape3D { Shape: BoxShape3D box } shape)
                continue;
            var bounds = shape.GlobalTransform * new Aabb(-box.Size / 2f, box.Size);
            TileMaterial.SetShaderParameter("water_surface_y", bounds.End.Y);
            TileMaterial.SetShaderParameter("water_rect", new Vector4(bounds.Position.X, bounds.Position.Z, bounds.End.X, bounds.End.Z));
            break;
        }

        // The sun lives in the main scene; when this scene is edited on its own the material keeps its value.
        var root = Engine.IsEditorHint() ? GetTree().EditedSceneRoot : GetTree().CurrentScene;
        var suns = root?.FindChildren("*", nameof(DirectionalLight3D), true, false);
        if (suns is { Count: > 0 } && suns[0] is DirectionalLight3D sun)
            TileMaterial.SetShaderParameter("sun_direction", -sun.GlobalBasis.Z);
    }
}
