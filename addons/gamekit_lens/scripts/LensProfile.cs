using Godot;

namespace GameKit.Lens;

/// <summary>
/// A photographic look for <see cref="CameraLens"/>: lens (distortion, chromatic aberration, vignette,
/// depth of field), film/sensor (halation, grain, bloom), grading and lens flares. Presets live in
/// <c>addons/gamekit_lens/profiles/</c>. After changing values from code, call
/// <see cref="CameraLens.Refresh"/> (or assign the profile again).
/// </summary>
[GlobalClass]
public partial class LensProfile : Resource
{
    [Export] public string DisplayName { get; set; } = "Natural";

    [ExportGroup("Vignette")]
    [Export] public bool VignetteEnabled { get; set; } = true;
    [Export(PropertyHint.Range, "0,2,0.01")] public float VignetteIntensity { get; set; } = 0.22f;
    /// <summary>Distance from the center (1 = screen edge) where darkening starts.</summary>
    [Export(PropertyHint.Range, "0,2.5,0.01")] public float VignetteSize { get; set; } = 0.95f;
    [Export(PropertyHint.Range, "0.001,2,0.01")] public float VignetteSmoothness { get; set; } = 1f;
    [Export(PropertyHint.Range, "0.25,4,0.01")] public float VignetteRoundness { get; set; } = 1f;

    [ExportGroup("Chromatic Aberration")]
    /// <summary>Lateral color fringes toward the frame edges, on high-contrast edges only.</summary>
    [Export] public bool ChromaticAberrationEnabled { get; set; } = true;
    /// <summary>Fringe width in pixels at the corners.</summary>
    [Export(PropertyHint.Range, "0,8,0.01")] public float ChromaticAberration { get; set; } = 1.6f;
    [Export(PropertyHint.Range, "0,1,0.01")] public float CaEdgeStart { get; set; } = 0.55f;
    [Export(PropertyHint.Range, "0,4,0.01")] public float CaBlur { get; set; } = 1.2f;
    [Export(PropertyHint.Range, "0.5,4,0.01")] public float CaRadialPower { get; set; } = 1.85f;
    [Export(PropertyHint.Range, "0.25,4,0.01")] public float CaEdgeResponse { get; set; } = 1.35f;
    [Export(PropertyHint.Range, "0,1,0.01")] public float CaHighlightFringe { get; set; } = 0.35f;

    [ExportGroup("Lens Distortion")]
    /// <summary>Off by default: transparents (water surfaces, rain, Label3D) draw after the lens pass
    /// undistorted, so they shift against distorted geometry near the frame edges.</summary>
    [Export] public bool DistortionEnabled { get; set; }
    /// <summary>Positive = barrel, negative = pincushion. The image is scaled so corners never sample
    /// outside the frame.</summary>
    [Export(PropertyHint.Range, "-0.5,0.5,0.001")] public float Distortion { get; set; } = 0.02f;
    [Export(PropertyHint.Range, "-0.5,0.5,0.001")] public float DistortionCubic { get; set; } = 0.005f;

    [ExportGroup("Depth of Field")]
    /// <summary>Lens blur by distance, applied after distortion. Off by default: it hides far targets.</summary>
    [Export] public bool DofEnabled { get; set; }
    [Export(PropertyHint.Range, "0,1,0.01")] public float DofAmount { get; set; } = 0.5f;
    [Export] public bool DofNearEnabled { get; set; }
    [Export(PropertyHint.Range, "0.01,100,0.01")] public float DofNearDistance { get; set; } = 0.5f;
    [Export(PropertyHint.Range, "0.001,20,0.01")] public float DofNearTransition { get; set; } = 0.3f;
    [Export] public bool DofFarEnabled { get; set; } = true;
    [Export(PropertyHint.Range, "0.01,500,0.01")] public float DofFarDistance { get; set; } = 60f;
    [Export(PropertyHint.Range, "0.001,100,0.01")] public float DofFarTransition { get; set; } = 30f;

    [ExportGroup("Halation")]
    /// <summary>Warm glow that film shows around bright highlights.</summary>
    [Export] public bool HalationEnabled { get; set; } = true;
    [Export(PropertyHint.Range, "0,3,0.01")] public float HalationIntensity { get; set; } = 0.6f;
    /// <summary>Brightness (linear, before tonemapping) where halation starts.</summary>
    [Export(PropertyHint.Range, "0,4,0.01")] public float HalationThreshold { get; set; } = 1.2f;
    [Export(PropertyHint.Range, "0.001,2,0.01")] public float HalationSoftness { get; set; } = 0.6f;
    [Export(PropertyHint.Range, "0,16,0.01")] public float HalationRadius { get; set; } = 2.5f;
    [Export] public Color HalationColor { get; set; } = new(0.96f, 0.26f, 0f);
    /// <summary>Close highlights bleed wider than distant ones, and the bleed stops at depth edges.</summary>
    [Export] public bool HalationDepthAware { get; set; } = true;
    [Export(PropertyHint.Range, "0.1,500,0.01")] public float HalationDepthReference { get; set; } = 8f;
    [Export(PropertyHint.Range, "0.1,4,0.01")] public float HalationDepthCurve { get; set; } = 1f;
    [Export(PropertyHint.Range, "0.05,4,0.01")] public float HalationDepthMinScale { get; set; } = 0.45f;
    [Export(PropertyHint.Range, "0.05,8,0.01")] public float HalationDepthMaxScale { get; set; } = 2.2f;
    [Export(PropertyHint.Range, "0.01,10,0.01")] public float HalationDepthRejection { get; set; } = 1.25f;

    [ExportGroup("Highlight Bloom")]
    /// <summary>Extra local bloom. Off by default: the Environment's glow already blooms.</summary>
    [Export] public bool BloomEnabled { get; set; }
    [Export(PropertyHint.Range, "0,3,0.01")] public float BloomIntensity { get; set; } = 0.13f;
    [Export(PropertyHint.Range, "0,4,0.01")] public float BloomThreshold { get; set; } = 1f;
    [Export(PropertyHint.Range, "0.001,2,0.01")] public float BloomSoftness { get; set; } = 0.57f;
    [Export(PropertyHint.Range, "0,12,0.01")] public float BloomRadius { get; set; } = 1.31f;
    [Export] public Color BloomTint { get; set; } = Colors.White;

    [ExportGroup("Grain")]
    [Export] public bool GrainEnabled { get; set; } = true;
    /// <summary>Digital sensor noise (colored, per channel) instead of monochrome film grain.</summary>
    [Export] public bool RgbSensorNoise { get; set; } = true;
    [Export(PropertyHint.Range, "0,0.2,0.001")] public float GrainStrength { get; set; } = 0.012f;
    /// <summary>Grain cell size in pixels.</summary>
    [Export(PropertyHint.Range, "0.25,8,0.01")] public float GrainSize { get; set; } = 1.5f;
    [Export(PropertyHint.Range, "0,20,0.01")] public float GrainSpeed { get; set; } = 6f;
    /// <summary>Grain weight in shadows, mids and highlights. Real sensors are noisiest in the shadows.</summary>
    [Export(PropertyHint.Range, "0,4,0.01")] public float GrainShadows { get; set; } = 1.6f;
    [Export(PropertyHint.Range, "0,4,0.01")] public float GrainMids { get; set; } = 1f;
    [Export(PropertyHint.Range, "0,4,0.01")] public float GrainHighlights { get; set; } = 0.5f;

    [ExportGroup("Grading")]
    [Export] public bool GradingEnabled { get; set; } = true;
    [Export(PropertyHint.Range, "0,3,0.01")] public float Exposure { get; set; } = 1f;
    /// <summary>Contrast around middle grey (0.18 linear).</summary>
    [Export(PropertyHint.Range, "0.5,1.5,0.01")] public float Contrast { get; set; } = 1.04f;
    [Export(PropertyHint.Range, "0,2,0.01")] public float Saturation { get; set; } = 1.02f;
    /// <summary>White balance: positive = warmer.</summary>
    [Export(PropertyHint.Range, "-1,1,0.001")] public float Temperature { get; set; } = 0.02f;
    /// <summary>Positive = greener, negative = more magenta.</summary>
    [Export(PropertyHint.Range, "-1,1,0.001")] public float Tint { get; set; }
    /// <summary>Added to the linear, not yet exposed image: keep tiny (0.001), dark scenes amplify it.</summary>
    [Export] public Color Lift { get; set; } = Colors.Black;
    [Export] public Color Gamma { get; set; } = Colors.White;
    [Export] public Color Gain { get; set; } = Colors.White;

    [ExportGroup("LUT")]
    [Export] public bool LutEnabled { get; set; }
    /// <summary>2D strip LUT: width = size * size, height = size.</summary>
    [Export] public Texture2D? LutTexture { get; set; }
    [Export(PropertyHint.Range, "2,65,1")] public int LutSize { get; set; } = 33;
    [Export(PropertyHint.Range, "0,1,0.01")] public float LutStrength { get; set; } = 1f;

    [ExportGroup("Lens Flare")]
    [Export] public bool FlareEnabled { get; set; } = true;
    /// <summary>Flare from the scene's sun (CameraLens.Sun). Scales with the sun's energy, so weather that
    /// dims the sun dims the flare too.</summary>
    [Export] public bool SunFlare { get; set; } = true;
    [Export(PropertyHint.Range, "0,8,0.01")] public float FlareIntensity { get; set; } = 0.35f;
    [Export(PropertyHint.Range, "0,8,0.01")] public float FlareBlur { get; set; } = 1.8f;
    [Export(PropertyHint.Range, "0,0.05,0.0001")] public float FlareChroma { get; set; } = 0.02f;
    [Export(PropertyHint.Range, "0,4,0.01")] public float FlareHalo { get; set; } = 0.15f;
    [Export(PropertyHint.Range, "0,4,0.01")] public float FlareGhosts { get; set; } = 0.5f;
    [Export(PropertyHint.Range, "0,4,0.01")] public float FlareRays { get; set; }
    [Export(PropertyHint.Range, "4,16,1")] public float FlareRayCount { get; set; } = 7f;
    [Export(PropertyHint.Range, "0.2,4,0.01")] public float FlareRayLength { get; set; } = 1.65f;
    [Export(PropertyHint.Range, "0,1,0.01")] public float FlareImperfection { get; set; } = 0.2f;
    [Export(PropertyHint.Range, "0.1,3,0.01")] public float FlareGhostSpread { get; set; } = 1f;
    [Export(PropertyHint.Range, "0.1,4,0.01")] public float FlareSourceBloom { get; set; } = 1.25f;
    /// <summary>Flare size as a fraction of the screen height.</summary>
    [Export(PropertyHint.Range, "0.001,0.25,0.001")] public float FlareSize { get; set; } = 0.055f;
    [Export(PropertyHint.Range, "0.1,4,0.01")] public float SunFlareSize { get; set; } = 3f;
    [Export(PropertyHint.Range, "0,8,0.01")] public float SunFlareIntensity { get; set; } = 1f;
    /// <summary>Lights below this energy make no flare.</summary>
    [Export(PropertyHint.Range, "0,100,0.01")] public float FlareEnergyThreshold { get; set; } = 2f;
    [Export(PropertyHint.Range, "0,10,0.01")] public float FlareDistanceFalloff { get; set; } = 1f;
    /// <summary>How fast flares appear and fade (1/s). Fading hides the pop when a source gets blocked.</summary>
    [Export(PropertyHint.Range, "0.1,60,0.01")] public float FlareFadeInSpeed { get; set; } = 30f;
    [Export(PropertyHint.Range, "0.1,60,0.01")] public float FlareFadeOutSpeed { get; set; } = 20f;
    /// <summary>Hide a flare when the screen behind its source isn't bright (a cloud or object covers it).</summary>
    [Export] public bool FlareVisibilityGating { get; set; } = true;
    [Export(PropertyHint.Range, "0,2,0.01")] public float FlareVisibilityThreshold { get; set; } = 0.65f;
    /// <summary>Keep ghosts off the exact screen center (the crosshair).</summary>
    [Export] public bool FlareCenterSafeZone { get; set; } = true;

    /// <summary>Writes the post-processing uniforms of lens_post.gdshader.</summary>
    public void ApplyPost(ShaderMaterial m)
    {
        m.SetShaderParameter("enable_vignette", VignetteEnabled);
        m.SetShaderParameter("vignette_intensity", VignetteIntensity);
        m.SetShaderParameter("vignette_size", VignetteSize);
        m.SetShaderParameter("vignette_smoothness", VignetteSmoothness);
        m.SetShaderParameter("vignette_roundness", VignetteRoundness);

        m.SetShaderParameter("enable_chromatic_aberration", ChromaticAberrationEnabled);
        m.SetShaderParameter("chromatic_aberration", ChromaticAberration);
        m.SetShaderParameter("ca_edge_start", CaEdgeStart);
        m.SetShaderParameter("ca_blur", CaBlur);
        m.SetShaderParameter("ca_radial_power", CaRadialPower);
        m.SetShaderParameter("ca_edge_response", CaEdgeResponse);
        m.SetShaderParameter("ca_highlight_fringe", CaHighlightFringe);

        m.SetShaderParameter("enable_distortion", DistortionEnabled);
        m.SetShaderParameter("distortion", Distortion);
        m.SetShaderParameter("distortion_cubic", DistortionCubic);
        m.SetShaderParameter("distortion_overscan", DistortionOverscan());
        m.SetShaderParameter("distortion_center", new Vector2(0.5f, 0.5f));

        m.SetShaderParameter("enable_post_dof", DofEnabled);
        m.SetShaderParameter("post_dof_amount", DofAmount);
        m.SetShaderParameter("post_dof_near_enabled", DofNearEnabled);
        m.SetShaderParameter("post_dof_near_distance", DofNearDistance);
        m.SetShaderParameter("post_dof_near_transition", Mathf.Max(DofNearTransition, 0.001f));
        m.SetShaderParameter("post_dof_far_enabled", DofFarEnabled);
        m.SetShaderParameter("post_dof_far_distance", DofFarDistance);
        m.SetShaderParameter("post_dof_far_transition", Mathf.Max(DofFarTransition, 0.001f));

        m.SetShaderParameter("enable_halation", HalationEnabled);
        m.SetShaderParameter("halation_intensity", HalationIntensity);
        m.SetShaderParameter("halation_threshold", HalationThreshold);
        m.SetShaderParameter("halation_softness", HalationSoftness);
        m.SetShaderParameter("halation_radius", HalationRadius);
        m.SetShaderParameter("halation_color", Rgb(HalationColor));
        m.SetShaderParameter("halation_depth_aware", HalationDepthAware);
        m.SetShaderParameter("halation_depth_reference", HalationDepthReference);
        m.SetShaderParameter("halation_depth_curve", HalationDepthCurve);
        m.SetShaderParameter("halation_depth_min_scale", HalationDepthMinScale);
        m.SetShaderParameter("halation_depth_max_scale", HalationDepthMaxScale);
        m.SetShaderParameter("halation_depth_rejection", HalationDepthRejection);

        m.SetShaderParameter("enable_highlight_bloom", BloomEnabled);
        m.SetShaderParameter("bloom_intensity", BloomIntensity);
        m.SetShaderParameter("bloom_threshold", BloomThreshold);
        m.SetShaderParameter("bloom_softness", BloomSoftness);
        m.SetShaderParameter("bloom_radius", BloomRadius);
        m.SetShaderParameter("bloom_tint", Rgb(BloomTint));

        m.SetShaderParameter("enable_grain", GrainEnabled);
        m.SetShaderParameter("enable_rgb_digital_noise", RgbSensorNoise);
        m.SetShaderParameter("grain_strength", GrainStrength);
        m.SetShaderParameter("grain_size", GrainSize);
        m.SetShaderParameter("grain_speed", GrainSpeed);
        m.SetShaderParameter("grain_shadows", GrainShadows);
        m.SetShaderParameter("grain_mids", GrainMids);
        m.SetShaderParameter("grain_highlights", GrainHighlights);

        m.SetShaderParameter("enable_color_grading", GradingEnabled);
        m.SetShaderParameter("exposure", Exposure);
        m.SetShaderParameter("contrast", Contrast);
        m.SetShaderParameter("saturation", Saturation);
        m.SetShaderParameter("temperature", Temperature);
        m.SetShaderParameter("tint", Tint);
        m.SetShaderParameter("lift", Rgb(Lift));
        m.SetShaderParameter("gamma", Rgb(Gamma));
        m.SetShaderParameter("gain", Rgb(Gain));

        m.SetShaderParameter("enable_lut", LutEnabled && LutTexture != null);
        m.SetShaderParameter("lut_size", (float)LutSize);
        m.SetShaderParameter("lut_strength", LutStrength);
        if (LutTexture != null)
            m.SetShaderParameter("lut_texture", LutTexture);
    }

    /// <summary>Writes the uniforms of lens_flare.gdshader that don't change per frame.</summary>
    public void ApplyFlare(ShaderMaterial m)
    {
        m.SetShaderParameter("master_intensity", 1f); // intensity is folded into each source
        m.SetShaderParameter("blur_amount", FlareBlur);
        m.SetShaderParameter("chroma_amount", FlareChroma);
        m.SetShaderParameter("halo_intensity", FlareHalo);
        m.SetShaderParameter("ghost_intensity", FlareGhosts);
        m.SetShaderParameter("ray_intensity", FlareRays);
        m.SetShaderParameter("imperfection", FlareImperfection);
        m.SetShaderParameter("ray_count", FlareRayCount);
        m.SetShaderParameter("ray_length", FlareRayLength);
        m.SetShaderParameter("ghost_spread", FlareGhostSpread);
        m.SetShaderParameter("source_bloom_scale", FlareSourceBloom);
        m.SetShaderParameter("enable_source_visibility_gating", FlareVisibilityGating);
        m.SetShaderParameter("source_visibility_threshold", FlareVisibilityThreshold);
        m.SetShaderParameter("source_visibility_softness", 0.18f);
        m.SetShaderParameter("source_visibility_sample_radius", 0.006f);
        m.SetShaderParameter("enable_center_safe_zone", FlareCenterSafeZone);
        m.SetShaderParameter("allowed_flare_area_radius_scale", 0.55f);
        m.SetShaderParameter("center_dead_zone_radius_scale", 0.114f);
        m.SetShaderParameter("allowed_flare_area_feather", 0.15f);
    }

    /// <summary>False when every screen effect is off; the lens pass is then skipped entirely.</summary>
    public bool AnyPostEffect =>
        VignetteEnabled || ChromaticAberrationEnabled || DistortionEnabled || DofEnabled || HalationEnabled
        || BloomEnabled || GrainEnabled || GradingEnabled || (LutEnabled && LutTexture != null);

    /// <summary>Zoom that keeps the distorted corners inside the frame: the barrel factor at the corner
    /// (r² = 2 in the shader's -1..1 space).</summary>
    public float DistortionOverscan() =>
        DistortionEnabled ? Mathf.Max(1f, 1f + Distortion * 2f + DistortionCubic * 4f) : 1f;

    private static Vector3 Rgb(Color c) => new(c.R, c.G, c.B);
}
