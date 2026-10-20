using System;
using GameKit.Core;
using Godot;

namespace GameKit.Viewmodel;

/// <summary>
/// A viewmodel weapon. Owns ammo, fire rate and its AnimationPlayer clips (equip, holster, fire, reload).
/// Builds placeholder geometry and clips in code when the scene provides none, so it works without assets;
/// a production weapon scene supplies its own meshes and an AnimationPlayer with the same clip names.
/// It never touches locomotion or collision.
/// </summary>
[GlobalClass]
public partial class Weapon : Node3D
{
    public static readonly StringName ClipEquip = "equip";
    public static readonly StringName ClipHolster = "holster";
    public static readonly StringName ClipFire = "fire";
    public static readonly StringName ClipReload = "reload";

    [Export] public string DisplayName { get; set; } = "Rifle";
    [Export] public bool Automatic { get; set; } = true;
    [Export] public float FireInterval { get; set; } = 0.1f;
    [Export] public int MagazineSize { get; set; } = 30;
    [Export] public float Range { get; set; } = 100f;
    [Export] public float HitImpulse { get; set; } = 6f;
    /// <summary>View punch in degrees (x = up, y = sideways max).</summary>
    [Export] public Vector2 RecoilDegrees { get; set; } = new(0.9f, 0.35f);
    [Export] public Color PlaceholderColor { get; set; } = new(0.22f, 0.24f, 0.27f);
    [Export] public Vector3 PlaceholderSize { get; set; } = new(0.06f, 0.09f, 0.42f);

    private AnimationPlayer _anim = null!;
    private double _nextFireTime;

    public int Ammo { get; private set; }
    public bool IsReloading => _anim.IsPlaying() && _anim.CurrentAnimation == ClipReload;
    public bool IsSwitching => _anim.IsPlaying() && (_anim.CurrentAnimation == ClipEquip || _anim.CurrentAnimation == ClipHolster);

    public event Action<Weapon>? Fired;

    public override void _Ready()
    {
        Ammo = MagazineSize;
        _anim = GetNodeOrNull<AnimationPlayer>("AnimationPlayer") ?? BuildPlaceholder();
        _anim.AnimationFinished += OnAnimationFinished;
    }

    public void Equip()
    {
        Visible = true;
        _anim.Play(ClipEquip);
    }

    public void Holster() => _anim.Play(ClipHolster);

    /// <summary>Fires if ready. <paramref name="origin"/>/<paramref name="direction"/> describe the aim ray.</summary>
    public bool TryFire(Vector3 origin, Vector3 direction, Rid exclude, PhysicsDirectSpaceState3D space)
    {
        double now = Time.GetTicksMsec() / 1000.0;
        if (now < _nextFireTime || IsReloading || IsSwitching)
            return false;
        if (Ammo <= 0)
        {
            Reload();
            return false;
        }
        _nextFireTime = now + FireInterval;
        Ammo--;
        _anim.Stop();
        _anim.Play(ClipFire);

        var query = PhysicsRayQueryParameters3D.Create(origin, origin + direction * Range, 1 | 4, new Godot.Collections.Array<Rid> { exclude });
        var hit = space.IntersectRay(query);
        if (hit.Count > 0 && hit["collider"].AsGodotObject() is RigidBody3D body)
            body.ApplyImpulse(direction * HitImpulse, hit["position"].AsVector3() - body.GlobalPosition);
        Fired?.Invoke(this);
        return true;
    }

    public void Reload()
    {
        if (Ammo == MagazineSize || IsReloading || IsSwitching)
            return;
        _anim.Play(ClipReload);
    }

    private void OnAnimationFinished(StringName clip)
    {
        if (clip == ClipReload)
            Ammo = MagazineSize;
        else if (clip == ClipHolster)
            Visible = false;
    }

    // ---- Placeholder content ------------------------------------------------------------

    private AnimationPlayer BuildPlaceholder()
    {
        var pivot = new Node3D { Name = "Pivot" };
        AddChild(pivot);
        var metal = new StandardMaterial3D { AlbedoColor = PlaceholderColor, Metallic = 0.6f, Roughness = 0.45f };
        var skin = new StandardMaterial3D { AlbedoColor = new Color(0.78f, 0.6f, 0.48f), Roughness = 0.8f };
        var sleeve = new StandardMaterial3D { AlbedoColor = new Color(0.25f, 0.3f, 0.22f), Roughness = 0.9f };
        var s = PlaceholderSize;
        AddBox(pivot, new Vector3(0f, 0f, -s.Z * 0.5f), s, metal);                                           // receiver
        AddBox(pivot, new Vector3(0f, s.Y * 0.15f, -s.Z - 0.08f), new Vector3(0.025f, 0.025f, 0.16f), metal); // barrel
        AddBox(pivot, new Vector3(0f, -s.Y, -s.Z * 0.25f), new Vector3(0.04f, 0.1f, 0.05f), metal);           // grip
        AddBox(pivot, new Vector3(0f, -s.Y * 0.9f, -s.Z * 0.6f), new Vector3(0.035f, 0.12f, 0.06f), metal);   // magazine
        AddBox(pivot, new Vector3(0f, -s.Y * 0.9f, -s.Z * 0.18f), new Vector3(0.06f, 0.06f, 0.08f), skin);    // right hand
        AddBox(pivot, new Vector3(0.02f, -s.Y * 0.9f, 0.08f), new Vector3(0.08f, 0.08f, 0.22f), sleeve);      // right forearm
        AddBox(pivot, new Vector3(-0.01f, -s.Y * 0.55f, -s.Z * 0.8f), new Vector3(0.06f, 0.05f, 0.08f), skin); // left hand
        AddBox(pivot, new Vector3(-0.09f, -s.Y * 0.9f, -s.Z * 0.55f), new Vector3(0.08f, 0.08f, 0.24f), sleeve);

        var anim = new AnimationPlayer { Name = "AnimationPlayer" };
        AddChild(anim);
        var library = new AnimationLibrary();
        var lowered = new Vector3(0.05f, -0.3f, 0.1f);
        var loweredRot = new Vector3(-0.9f, 0.3f, 0.2f);
        library.AddAnimation(ClipEquip, Clip(0.3f, (0f, lowered, loweredRot), (0.3f, Vector3.Zero, Vector3.Zero)));
        library.AddAnimation(ClipHolster, Clip(0.25f, (0f, Vector3.Zero, Vector3.Zero), (0.25f, lowered, loweredRot)));
        library.AddAnimation(ClipFire, Clip(0.12f,
            (0f, Vector3.Zero, Vector3.Zero),
            (0.03f, new Vector3(0f, 0.005f, 0.035f), new Vector3(0.06f, 0f, 0f)),
            (0.12f, Vector3.Zero, Vector3.Zero)));
        library.AddAnimation(ClipReload, Clip(1.1f,
            (0f, Vector3.Zero, Vector3.Zero),
            (0.25f, new Vector3(0.02f, -0.08f, 0.02f), new Vector3(-0.2f, 0.2f, 0.7f)),
            (0.8f, new Vector3(0.02f, -0.1f, 0.02f), new Vector3(-0.25f, 0.25f, 0.75f)),
            (1.1f, Vector3.Zero, Vector3.Zero)));
        anim.AddAnimationLibrary("", library);
        return anim;
    }

    private static Animation Clip(float length, params (float time, Vector3 pos, Vector3 rot)[] keys)
    {
        var a = new Animation { Length = length };
        int p = a.AddTrack(Animation.TrackType.Value);
        a.TrackSetPath(p, "Pivot:position");
        int r = a.AddTrack(Animation.TrackType.Value);
        a.TrackSetPath(r, "Pivot:rotation");
        foreach (var (time, pos, rot) in keys)
        {
            a.TrackInsertKey(p, time, pos);
            a.TrackInsertKey(r, time, rot);
        }
        return a;
    }

    private static void AddBox(Node3D parent, Vector3 position, Vector3 size, Material material)
    {
        parent.AddChild(new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = size },
            MaterialOverride = material,
            Position = position,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Layers = RenderLayers.Viewmodel,
        });
    }
}
