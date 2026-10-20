using Godot;

namespace GameKit.FpsController;

/// <summary>
/// Collision-aware step-up (Source <c>StepMove</c>, done with Godot body test-motion on PhysX):
/// when grounded horizontal motion is blocked by a steep face, sweep the capsule up, forward and down.
/// If the real surface under the landing is walkable and no higher than the step height, the body is
/// placed at the swept landing. Every leg is a shape cast, so the body never passes through geometry.
///
/// A capsule's rounded bottom first lands on the step's corner, slightly below the step top. That is
/// expected: the solver repeats on the following ticks as the capsule rolls over the corner, and the
/// motor treats a successful step as grounded so gravity and floor snap do not pull it back off.
/// Step-down is handled by <see cref="CharacterBody3D.FloorSnapLength"/>.
/// </summary>
public sealed class StepSolver
{
    private const float MinStep = 0.02f;
    private const float MinForwardTravel = 0.002f;
    private const float WalkableProbeInset = 0.03f;
    private const float RayReach = 0.1f;

    private readonly CharacterBody3D _body;
    private readonly KinematicCollision3D _hit = new();
    private readonly PhysicsRayQueryParameters3D _ray = new();

    /// <summary>Debug: last landing point tested and whether it was accepted.</summary>
    public Vector3 LastProbePoint { get; private set; }
    public bool LastProbeAccepted { get; private set; }
    public int QueriesThisTick { get; private set; }

    public StepSolver(CharacterBody3D body)
    {
        _body = body;
        _ray.Exclude = new Godot.Collections.Array<Rid> { body.GetRid() };
    }

    /// <summary>
    /// Tries to climb a step in the way of <paramref name="horizontalMotion"/>. On success moves the body to
    /// the landing (this tick's horizontal motion included) and returns the height gained; 0 = no step.
    /// <paramref name="feetY"/> is the resting feet height; the body may currently be lifted slightly above it.
    /// </summary>
    public float TryStepUp(Vector3 horizontalMotion, float stepHeight, float minFloorNormalY, float feetY)
    {
        QueriesThisTick = 0;
        if (horizontalMotion.LengthSquared() < 1e-8f)
            return 0f;

        var from = _body.GlobalTransform;
        // 1. Blocked at all? Walkable slopes are left to move_and_slide.
        if (!Test(from, horizontalMotion) || _hit.GetNormal().Y >= minFloorNormalY)
            return 0f;

        // 2. Up, as far as the ceiling allows (measured from the resting feet height).
        float maxUp = stepHeight - (from.Origin.Y - feetY);
        float up = Test(from, Vector3.Up * maxUp) ? _hit.GetTravel().Y : maxUp;
        if (up < MinStep)
            return 0f;
        var raised = from.Translated(Vector3.Up * up);

        // 3. Forward at the raised height.
        var forward = horizontalMotion;
        if (Test(raised, forward))
        {
            forward = _hit.GetTravel();
            if (forward.LengthSquared() < MinForwardTravel * MinForwardTravel)
                return 0f;
        }
        var moved = raised.Translated(forward);

        // 4. Down onto the step (or its corner).
        if (!Test(moved, Vector3.Down * (moved.Origin.Y - feetY + MinStep)))
            return 0f;
        var landing = moved.Origin + _hit.GetTravel();
        float climbed = landing.Y - feetY;
        LastProbePoint = _hit.GetPosition();
        LastProbeAccepted = false;
        if (climbed < MinStep || climbed > stepHeight + 0.001f)
            return 0f;

        // 5. The contact may be the step's edge, whose normal says nothing about the surface. Ray the real
        // surface just past the edge: it must be walkable and within step height of the feet.
        if (!ProbeSurface(_hit.GetPosition() + horizontalMotion.Normalized() * WalkableProbeInset, out var surface, out var normal)
            || normal.Y < minFloorNormalY
            || surface.Y - feetY > stepHeight + 0.01f)
            return 0f;

        LastProbeAccepted = true;
        _body.GlobalPosition = landing;
        return climbed;
    }

    /// <summary>
    /// Keeps a grounded player on stairs and down small ledges when the engine's own floor snap fails
    /// (on PhysX it can, while the capsule rim still touches the edge it is leaving). Lifts off current
    /// contacts, sweeps down up to <paramref name="stepHeight"/>, and accepts walkable floor, or an edge the
    /// capsule rests on when walkable ground lies directly below its centre. Returns true when grounded.
    /// </summary>
    public bool TrySnapDown(float stepHeight, float minFloorNormalY, float lift)
    {
        var from = _body.GlobalTransform;
        float up = Test(from, Vector3.Up * lift) ? _hit.GetTravel().Y : lift;
        var raised = from.Translated(Vector3.Up * up);
        if (!Test(raised, Vector3.Down * (stepHeight + up)))
            return false;
        var landing = raised.Origin + _hit.GetTravel();
        bool floor = _hit.GetNormal().Y >= minFloorNormalY;
        if (!floor)
        {
            // Resting on an edge: only grounded if there is walkable ground right under the centre.
            if (!ProbeSurface(landing + Vector3.Down * (stepHeight * 0.5f), out var surface, out var normal, stepHeight * 0.5f + 0.05f)
                || normal.Y < minFloorNormalY)
                return false;
        }
        _body.GlobalPosition = landing;
        return true;
    }

    private bool Test(Transform3D from, Vector3 motion)
    {
        QueriesThisTick++;
        return _body.TestMove(from, motion, _hit);
    }

    private bool ProbeSurface(Vector3 point, out Vector3 position, out Vector3 normal, float reach = RayReach)
    {
        QueriesThisTick++;
        _ray.From = point + Vector3.Up * reach;
        _ray.To = point + Vector3.Down * reach;
        _ray.CollisionMask = _body.CollisionMask;
        var result = _body.GetWorld3D().DirectSpaceState.IntersectRay(_ray);
        if (result.Count == 0)
        {
            position = normal = Vector3.Zero;
            return false;
        }
        position = result["position"].AsVector3();
        normal = result["normal"].AsVector3();
        return true;
    }
}
