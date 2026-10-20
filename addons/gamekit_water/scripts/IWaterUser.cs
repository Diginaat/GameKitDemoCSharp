using Godot;

namespace GameKit.Water;

/// <summary>
/// Implemented by bodies that react to water (swimmers, floating AI, vehicles). A <see cref="WaterVolume"/>
/// calls these when the body's collider enters or leaves it; bodies without the interface still ripple
/// the surface with a default size.
/// </summary>
public interface IWaterUser
{
    void OnWaterEntered(WaterVolume water);

    void OnWaterExited(WaterVolume water);

    /// <summary>World-space centre, half height and horizontal radius of the body, for surface ripples.</summary>
    (Vector3 Center, float HalfHeight, float Radius) WaterExtent { get; }
}
