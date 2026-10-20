using Godot;

namespace GameKit.Interaction;

/// <summary>A pressable button. Toggles <see cref="Target"/> (anything with a <c>Toggle()</c> method) and lights up.</summary>
[GlobalClass]
public partial class InteractableButton : StaticBody3D, IInteractable, IHighlightable
{
    [Export] public NodePath Target { get; set; } = new();
    [Export] public string Prompt { get; set; } = "Press button";

    private StandardMaterial3D? _material;
    private bool _on;

    public override void _Ready()
    {
        foreach (var child in GetChildren())
            if (child is MeshInstance3D mesh && mesh.MaterialOverride is StandardMaterial3D m)
                _material = m;
    }

    public string GetPrompt(Node interactor) => Prompt;

    public bool CanInteract(Node interactor) => true;

    public void Interact(Node interactor)
    {
        _on = !_on;
        if (_material != null)
            _material.AlbedoColor = _on ? new Color(0.2f, 0.9f, 0.3f) : new Color(0.8f, 0.15f, 0.1f);
        var target = GetNodeOrNull(Target);
        if (target != null && target.HasMethod("Toggle"))
            target.Call("Toggle");
    }

    public void SetHighlighted(bool highlighted)
    {
        if (_material == null)
            return;
        _material.EmissionEnabled = highlighted;
        _material.Emission = new Color(0.4f, 0.4f, 0.4f);
    }
}
