namespace GameKit.World;

/// <summary>
/// Implemented by bodies that can climb. A <see cref="LadderVolume"/> calls these when the body's collider
/// enters or leaves it; the body decides how it climbs.
/// </summary>
public interface ILadderUser
{
    void OnLadderEntered(LadderVolume ladder);

    void OnLadderExited(LadderVolume ladder);
}
