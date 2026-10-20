using Godot;

namespace GameKit.FpsController;

/// <summary>
/// Pushes the capsule out of any geometry it ended a move inside, and removes velocity into those
/// surfaces. Needed on the PhysX backend: its body motion test keeps only the closest sweep hit, so a
/// surface the capsule already touches (a wall in a corner, the floor) can hide the real obstacle, and
/// the move ends up to one tick of motion inside it (docs/PHYSX_INTEGRATION.md). The overlap query used
/// here (<c>collide_shape</c> → <c>computePenetration</c>) is a separate, reliable code path.
/// The query capsule is shrunk by <see cref="Skin"/> so ordinary resting contact is not treated as overlap.
/// </summary>
public sealed class PenetrationGuard
{
    private const float Skin = 0.005f;
    private const int MaxIterations = 4;
    private const int MaxResults = 16;

    private readonly CharacterBody3D _body;
    private readonly CollisionShape3D _shapeNode;
    private readonly CapsuleShape3D _queryShape = new();
    private readonly PhysicsShapeQueryParameters3D _query = new();

    /// <summary>Total correction applied on the last call (debug).</summary>
    public Vector3 LastCorrection { get; private set; }

    public PenetrationGuard(CharacterBody3D body, CollisionShape3D shapeNode)
    {
        _body = body;
        _shapeNode = shapeNode;
        _query.Shape = _queryShape;
        _query.Exclude = new Godot.Collections.Array<Rid> { body.GetRid() };
    }

    /// <summary>Resolves overlaps at the current pose. Returns true if the body was moved.</summary>
    public bool Resolve(ref Vector3 velocity)
    {
        var capsule = (CapsuleShape3D)_shapeNode.Shape;
        _queryShape.Radius = capsule.Radius - Skin;
        _queryShape.Height = Mathf.Max(capsule.Height - 2f * Skin, 2f * _queryShape.Radius);
        _query.CollisionMask = _body.CollisionMask;
        var space = _body.GetWorld3D().DirectSpaceState;
        var total = Vector3.Zero;

        for (int iteration = 0; iteration < MaxIterations; iteration++)
        {
            _query.Transform = _shapeNode.GlobalTransform;
            var pairs = space.CollideShape(_query, MaxResults);
            if (pairs.Count < 2)
                break;

            // Deepest overlap first; the remaining ones are re-evaluated next iteration from the new pose.
            Vector3 push = Vector3.Zero;
            for (int i = 0; i + 1 < pairs.Count; i += 2)
            {
                // Pair = (query origin, origin - mtdDir * depth); the MTD direction pushes the query shape out.
                var candidate = pairs[i] - pairs[i + 1];
                if (candidate.LengthSquared() > push.LengthSquared())
                    push = candidate;
            }
            if (push.LengthSquared() < 1e-10f)
                break;

            var normal = push.Normalized();
            push += normal * Skin;
            _body.GlobalPosition += push;
            total += push;
            float into = velocity.Dot(normal);
            if (into < 0f)
                velocity -= normal * into;
        }

        LastCorrection = total;
        return total != Vector3.Zero;
    }
}
