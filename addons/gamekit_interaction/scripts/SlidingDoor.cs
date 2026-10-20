using Godot;

namespace GameKit.Interaction;

/// <summary>
/// Kinematic sliding door, usable directly or toggled by a button. Moves in the physics tick with
/// sync-to-physics, so it pushes the player instead of overlapping them.
/// </summary>
[GlobalClass]
public partial class SlidingDoor : AnimatableBody3D, IInteractable, IHighlightable
{
    [Export] public Vector3 OpenOffset { get; set; } = new(0f, 2.4f, 0f);
    [Export] public float Speed { get; set; } = 2f;

    private Vector3 _closed;
    private bool _open;
    private float _t;
    private StandardMaterial3D? _material;

    public override void _Ready()
    {
        SyncToPhysics = true;
        _closed = Position;
        foreach (var child in GetChildren())
            if (child is MeshInstance3D mesh && mesh.MaterialOverride is StandardMaterial3D m)
                _material = m;
    }

    public void Toggle() => _open = !_open;

    public string GetPrompt(Node interactor) => _open ? "Close door" : "Open door";

    public bool CanInteract(Node interactor) => true;

    public void Interact(Node interactor) => Toggle();

    public void SetHighlighted(bool highlighted)
    {
        if (_material == null)
            return;
        _material.EmissionEnabled = highlighted;
        _material.Emission = new Color(0.25f, 0.25f, 0.25f);
    }

    public override void _PhysicsProcess(double delta)
    {
        float length = Mathf.Max(OpenOffset.Length(), 0.01f);
        _t = Mathf.MoveToward(_t, _open ? 1f : 0f, Speed * (float)delta / length);
        Position = _closed + OpenOffset * Mathf.SmoothStep(0f, 1f, _t);
    }
}
