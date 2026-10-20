using Godot;

namespace GameKit.Weather;

/// <summary>
/// One weather state. <see cref="WeatherSystem"/> blends between presets over time.
/// To add a parameter: add an export here, blend it in <see cref="Blend"/>, and use it in
/// <c>WeatherSystem.Apply</c>.
/// </summary>
[GlobalClass]
public partial class WeatherPreset : Resource
{
    [Export] public string DisplayName { get; set; } = "Clear";

    [ExportGroup("Clouds")]
    /// <summary>Fraction of the sky covered by clouds.</summary>
    [Export(PropertyHint.Range, "0,1")] public float CloudCoverage { get; set; } = 0.2f;
    /// <summary>Cloud opacity.</summary>
    [Export(PropertyHint.Range, "0,1")] public float CloudDensity { get; set; } = 0.8f;
    /// <summary>Darkens cloud undersides, for rain and storm clouds.</summary>
    [Export(PropertyHint.Range, "0,1")] public float CloudDarkness { get; set; }
    /// <summary>Pulls the whole sky toward a flat grey.</summary>
    [Export(PropertyHint.Range, "0,1")] public float Overcast { get; set; }

    [ExportGroup("Precipitation")]
    [Export(PropertyHint.Range, "0,1")] public float RainIntensity { get; set; }
    /// <summary>Lightning flashes per minute. 0 = no lightning; a thunderstorm uses 4-10.</summary>
    [Export(PropertyHint.Range, "0,30")] public float LightningPerMinute { get; set; }

    [ExportGroup("Atmosphere")]
    /// <summary>Wind in m/s. Moves clouds and slants rain.</summary>
    [Export(PropertyHint.Range, "0,30")] public float WindSpeed { get; set; } = 3f;
    /// <summary>Wind heading in degrees, 0 = toward -Z. Blended along the shortest turn.</summary>
    [Export(PropertyHint.Range, "0,360")] public float WindDirectionDegrees { get; set; } = 30f;
    /// <summary>How much the wind varies in gusts (rain slant and wind sound).</summary>
    [Export(PropertyHint.Range, "0,1")] public float Gustiness { get; set; } = 0.2f;
    /// <summary>Added to the environment's volumetric fog density.</summary>
    [Export(PropertyHint.Range, "0,0.2")] public float FogDensity { get; set; }
    /// <summary>Cheap exponential distance fog density (Environment.FogDensity). 0 = off.</summary>
    [Export(PropertyHint.Range, "0,0.1")] public float DistanceFog { get; set; }
    /// <summary>Multiplier for the sun's energy.</summary>
    [Export(PropertyHint.Range, "0,1")] public float SunEnergyScale { get; set; } = 1f;
    /// <summary>Camera exposure multiplier. Below 1 darkens the whole picture (thick clouds).</summary>
    [Export(PropertyHint.Range, "0.1,1")] public float SceneBrightness { get; set; } = 1f;

    /// <summary>Writes the blend of <paramref name="a"/> and <paramref name="b"/> into <paramref name="into"/>
    /// (no allocation). The name comes from <paramref name="b"/>.</summary>
    public static void Blend(WeatherPreset a, WeatherPreset b, float weight, WeatherPreset into)
    {
        into.DisplayName = b.DisplayName;
        into.CloudCoverage = Mathf.Lerp(a.CloudCoverage, b.CloudCoverage, weight);
        into.CloudDensity = Mathf.Lerp(a.CloudDensity, b.CloudDensity, weight);
        into.CloudDarkness = Mathf.Lerp(a.CloudDarkness, b.CloudDarkness, weight);
        into.Overcast = Mathf.Lerp(a.Overcast, b.Overcast, weight);
        into.RainIntensity = Mathf.Lerp(a.RainIntensity, b.RainIntensity, weight);
        into.LightningPerMinute = Mathf.Lerp(a.LightningPerMinute, b.LightningPerMinute, weight);
        into.WindSpeed = Mathf.Lerp(a.WindSpeed, b.WindSpeed, weight);
        into.Gustiness = Mathf.Lerp(a.Gustiness, b.Gustiness, weight);
        into.FogDensity = Mathf.Lerp(a.FogDensity, b.FogDensity, weight);
        into.DistanceFog = Mathf.Lerp(a.DistanceFog, b.DistanceFog, weight);
        into.SunEnergyScale = Mathf.Lerp(a.SunEnergyScale, b.SunEnergyScale, weight);
        into.SceneBrightness = Mathf.Lerp(a.SceneBrightness, b.SceneBrightness, weight);
        float heading = Mathf.LerpAngle(Mathf.DegToRad(a.WindDirectionDegrees), Mathf.DegToRad(b.WindDirectionDegrees), weight);
        into.WindDirectionDegrees = Mathf.PosMod(Mathf.RadToDeg(heading), 360f);
    }
}
