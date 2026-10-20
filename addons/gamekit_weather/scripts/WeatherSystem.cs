using Godot;
using Godot.Collections;

namespace GameKit.Weather;

/// <summary>
/// Drives the sky clouds, rain, fog, wind, lightning, sound, sun, exposure and surface wetness from
/// <see cref="WeatherPreset"/> resources, with smooth transitions.
///
/// Setup: add this node anywhere in the level. The WorldEnvironment's sky should use
/// <c>resources/weather_sky_material.tres</c> (<c>weather_environment.tres</c> does), and project.godot
/// must declare the four weather shader globals (see the module README). It finds the WorldEnvironment
/// and the sun when they are not assigned, creates the rain, the rain occlusion map and the sound, and
/// starts on the first preset. The <see cref="CycleAction"/> input (F7 by default) cycles presets.
/// </summary>
[GlobalClass]
public partial class WeatherSystem : Node
{
    [Signal] public delegate void WeatherChangedEventHandler(WeatherPreset preset);
    /// <summary>Emitted at each lightning flash. Distance is in meters.</summary>
    [Signal] public delegate void LightningStruckEventHandler(float distance);

    /// <summary>Used when <see cref="Presets"/> is left empty.</summary>
    public static readonly string[] DefaultPresets =
    {
        "res://addons/gamekit_weather/presets/clear.tres",
        "res://addons/gamekit_weather/presets/partly_cloudy.tres",
        "res://addons/gamekit_weather/presets/overcast.tres",
        "res://addons/gamekit_weather/presets/light_rain.tres",
        "res://addons/gamekit_weather/presets/storm.tres",
        "res://addons/gamekit_weather/presets/thunderstorm.tres",
    };

    /// <summary>Shader globals this system writes; project.godot must declare them.</summary>
    public static readonly string[] ShaderGlobals = { "weather_wetness", "weather_rain", "weather_rain_occlusion", "weather_occlusion_area" };

    private const float SpeedOfSound = 343f;
    private static readonly StringName WetnessGlobal = "weather_wetness";
    private static readonly StringName RainGlobal = "weather_rain";
    private static readonly Vector3[] FlashPulses = { new(0f, 1f, 0f), new(0.12f, 0.6f, 0f), new(0.3f, 0.8f, 0f) };

    /// <summary>Presets cycled by <see cref="NextWeather"/>. The first one is applied at start. Leave empty
    /// to use <see cref="DefaultPresets"/>.</summary>
    [Export] public Array<WeatherPreset> Presets { get; set; } = new();
    /// <summary>Default transition length in seconds.</summary>
    [Export] public float TransitionTime { get; set; } = 12f;
    /// <summary>Input action that cycles presets. Added (F7) when missing; empty disables it.</summary>
    [Export] public StringName CycleAction { get; set; } = "cycle_weather";
    /// <summary>Clouds drift this many times faster than ground wind (they're higher up).</summary>
    [Export] public float CloudWindMultiplier { get; set; } = 6f;
    /// <summary>Fraction of the wind that slants the rain.</summary>
    [Export] public float RainWindMultiplier { get; set; } = 0.6f;
    /// <summary>How much storm clouds (CloudDarkness) dim the sky light, through
    /// Environment.BackgroundEnergyMultiplier, so sky, ambient light, reflections and GI dim together.</summary>
    [Export(PropertyHint.Range, "0,1")] public float StormSkyDimming { get; set; } = 0.5f;
    /// <summary>Volumetric fog tint in the darkest weather.</summary>
    [Export] public Color StormFogColor { get; set; } = new(0.16f, 0.17f, 0.19f);
    /// <summary>Shadow blur under a full overcast. Clouds act like a softbox, so shadows get very soft
    /// instead of disappearing. Uses ShadowBlur, not the sun's angular size: a wider sun would also blow up
    /// its specular highlight on wet surfaces.</summary>
    [Export(PropertyHint.Range, "1,10")] public float OvercastShadowBlur { get; set; } = 6f;

    [ExportGroup("Lightning")]
    [Export] public float LightningEnergy { get; set; } = 2.5f;
    [Export] public Color LightningColor { get; set; } = new(0.78f, 0.84f, 1f);
    /// <summary>Strikes land this far away, in meters. Thunder is delayed accordingly.</summary>
    [Export] public float LightningDistanceMin { get; set; } = 300f;
    [Export] public float LightningDistanceMax { get; set; } = 3000f;

    [ExportGroup("Sound")]
    [Export] public bool AudioEnabled { get; set; } = true;

    [ExportGroup("Scene")]
    /// <summary>Found automatically when empty.</summary>
    [Export] public WorldEnvironment? WorldEnvironment { get; set; }
    /// <summary>Found automatically when empty.</summary>
    [Export] public DirectionalLight3D? Sun { get; set; }
    /// <summary>Created automatically when empty.</summary>
    [Export] public RainEmitter? Rain { get; set; }

    [ExportGroup("Wetness")]
    /// <summary>Seconds for surfaces to get fully wet in heavy rain.</summary>
    [Export] public float WettingTime { get; set; } = 20f;
    /// <summary>Seconds for surfaces to dry after the rain stops.</summary>
    [Export] public float DryingTime { get; set; } = 90f;

    /// <summary>The blended state currently shown. Read-only for callers.</summary>
    public WeatherPreset Current { get; } = new();
    /// <summary>The preset being shown or transitioned to.</summary>
    public WeatherPreset? Target { get; private set; }
    /// <summary>Name of the preset being shown or transitioned to.</summary>
    public string WeatherName => Target?.DisplayName ?? Current.DisplayName;
    /// <summary>Surface wetness 0..1; lags behind the rain. Set it to start a level after rain.</summary>
    public float Wetness
    {
        get => _wetness;
        set => _wetness = Mathf.Clamp(value, 0f, 1f);
    }
    public WeatherAudio? Audio { get; private set; }
    public RainOcclusion? Occlusion { get; private set; }

    private readonly WeatherPreset _from = new();
    private float _progress = 1f;
    private float _duration = 1f;
    private int _index;
    private float _time;
    private float _wetness;
    private Vector2 _cloudOffset;
    private float _cloudPeriod;
    private ShaderMaterial? _skyMaterial;
    private Godot.Environment? _environment;
    private CameraAttributes? _cameraAttributes;
    private readonly FastNoiseLite _gustNoise = new() { Frequency = 0.15f };
    private DirectionalLight3D _lightningLight = null!;
    private float _nextStrike;
    private float _flashTime = -1f;
    private Color _skyHorizonColor = new(0.62f, 0.68f, 0.76f);
    private Color _skyOvercastColor = new(0.46f, 0.48f, 0.52f);

    // Scene values at start; the weather scales them and restores them on exit (resources are shared).
    private float _baseSunEnergy = 1f, _baseSunFogEnergy = 1f, _baseSunShadowBlur = 1f, _baseSunSpecular = 1f;
    private float _baseSunShadowOpacity = 1f;
    private bool _baseSunShadows = true, _baseSunVisible = true;
    private float _baseFogDensity, _baseBackgroundEnergy = 1f, _baseFogAnisotropy = 0.2f, _baseFogSkyAffect = 1f;
    private float _baseFogAmbientInject, _baseFogLightEnergy = 1f, _baseFogDistanceDensity, _baseFogSkyAffectDistance;
    private bool _baseFogEnabled;
    private Color _baseFogAlbedo = Colors.White, _baseFogLightColor = Colors.White;
    private float _baseExposure = 1f, _baseAutoExposureScale = 0.4f;

    public override void _Ready()
    {
        Node scene = GetTree().CurrentScene ?? GetParent();
        WorldEnvironment ??= FindFirst<WorldEnvironment>(scene);
        Sun ??= FindFirst<DirectionalLight3D>(scene);
        if (Rain == null)
        {
            Rain = new RainEmitter { Name = "Rain" };
            AddChild(Rain);
        }
        // Keeps covered surfaces dry; runs always (surfaces also dry after rain).
        Occlusion = new RainOcclusion { Name = "RainOcclusion" };
        AddChild(Occlusion);
        if (AudioEnabled)
        {
            Audio = new WeatherAudio { Name = "Audio" };
            AddChild(Audio);
        }

        _lightningLight = new DirectionalLight3D
        {
            Name = "Lightning",
            LightColor = LightningColor,
            LightEnergy = 0f,
            ShadowEnabled = false,
            Visible = false,
        };
        AddChild(_lightningLight);
        _gustNoise.Seed = (int)GD.Randi();

        CaptureSceneBase();
        foreach (string name in ShaderGlobals)
        {
            if (!ProjectSettings.HasSetting($"shader_globals/{name}"))
                GD.PushError($"WeatherSystem: add the '{name}' shader global in Project Settings > Shader Globals (see addons/gamekit_weather/README.md).");
        }
        EnsureCycleAction();

        if (Presets.Count == 0)
        {
            foreach (string path in DefaultPresets)
                Presets.Add(GD.Load<WeatherPreset>(path));
        }
        SetWeather(Presets[0], 0f);
    }

    public override void _ExitTree()
    {
        // Environment, camera attributes and sun are shared; leave them as we found them.
        if (_cameraAttributes != null)
        {
            _cameraAttributes.ExposureMultiplier = _baseExposure;
            _cameraAttributes.AutoExposureScale = _baseAutoExposureScale;
        }
        if (_environment != null)
        {
            _environment.BackgroundEnergyMultiplier = _baseBackgroundEnergy;
            _environment.VolumetricFogDensity = _baseFogDensity;
            _environment.VolumetricFogAlbedo = _baseFogAlbedo;
            _environment.VolumetricFogAmbientInject = _baseFogAmbientInject;
            _environment.VolumetricFogAnisotropy = _baseFogAnisotropy;
            _environment.VolumetricFogSkyAffect = _baseFogSkyAffect;
            _environment.FogEnabled = _baseFogEnabled;
            _environment.FogDensity = _baseFogDistanceDensity;
            _environment.FogLightColor = _baseFogLightColor;
            _environment.FogLightEnergy = _baseFogLightEnergy;
            _environment.FogSkyAffect = _baseFogSkyAffectDistance;
        }
        if (Sun != null)
        {
            Sun.LightEnergy = _baseSunEnergy;
            Sun.LightVolumetricFogEnergy = _baseSunFogEnergy;
            Sun.ShadowEnabled = _baseSunShadows;
            Sun.ShadowBlur = _baseSunShadowBlur;
            Sun.LightSpecular = _baseSunSpecular;
            Sun.ShadowOpacity = _baseSunShadowOpacity;
            Sun.Visible = _baseSunVisible;
        }
        RenderingServer.GlobalShaderParameterSet(WetnessGlobal, 0f);
        RenderingServer.GlobalShaderParameterSet(RainGlobal, 0f);
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!CycleAction.IsEmpty && InputMap.HasAction(CycleAction) && @event.IsActionPressed(CycleAction))
            NextWeather();
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        _time += dt;
        if (Target != null && _progress < 1f)
        {
            _progress = Mathf.Min(_progress + dt / _duration, 1f);
            WeatherPreset.Blend(_from, Target, Mathf.SmoothStep(0f, 1f, _progress), Current);
        }
        UpdateLightning(dt);
        Apply(dt);
    }

    /// <summary>Transitions to a preset over <paramref name="duration"/> seconds (negative =
    /// <see cref="TransitionTime"/>, 0 = instantly).</summary>
    public void SetWeather(WeatherPreset preset, float duration = -1f)
    {
        if (duration < 0f)
            duration = TransitionTime;
        Target = preset;
        _index = Mathf.Max(Presets.IndexOf(preset), 0);
        WeatherPreset.Blend(Current, Current, 0f, _from);
        if (duration <= 0f)
        {
            _progress = 1f;
            WeatherPreset.Blend(preset, preset, 1f, Current);
            _wetness = preset.RainIntensity;
        }
        else
        {
            _progress = 0f;
            _duration = duration;
        }
        EmitSignal(SignalName.WeatherChanged, preset);
    }

    public void NextWeather()
    {
        if (Presets.Count > 0)
            SetWeather(Presets[(_index + 1) % Presets.Count]);
    }

    /// <summary>Fires a lightning strike now, at a random distance unless one is given.</summary>
    public void TriggerLightning(float distance = -1f)
    {
        if (distance < 0f)
            distance = (float)GD.RandRange(LightningDistanceMin, LightningDistanceMax);
        float azimuth = GD.Randf() * Mathf.Tau;
        float elevation = Mathf.DegToRad((float)GD.RandRange(20.0, 60.0));
        var direction = new Vector3(Mathf.Cos(elevation) * Mathf.Sin(azimuth), Mathf.Sin(elevation), Mathf.Cos(elevation) * Mathf.Cos(azimuth));
        _skyMaterial?.SetShaderParameter("lightning_direction", direction);
        _lightningLight.Basis = Basis.LookingAt(-direction);
        _flashTime = 0f;
        float closeness = 1f - Mathf.InverseLerp(LightningDistanceMin, LightningDistanceMax, distance);
        Audio?.PlayThunder(distance / SpeedOfSound, closeness);
        EmitSignal(SignalName.LightningStruck, distance);
    }

    private void UpdateLightning(float dt)
    {
        if (Current.LightningPerMinute > 0.05f)
        {
            _nextStrike -= dt;
            if (_nextStrike <= 0f)
            {
                TriggerLightning();
                _nextStrike = 60f / Current.LightningPerMinute * (float)GD.RandRange(0.4, 1.6);
            }
        }
        else
        {
            _nextStrike = (float)GD.RandRange(2.0, 6.0);
        }

        float flash = 0f;
        if (_flashTime >= 0f)
        {
            _flashTime += dt;
            // A main flash and two flickers, like a real multi-stroke strike.
            foreach (Vector3 pulse in FlashPulses)
            {
                if (_flashTime >= pulse.X)
                    flash = Mathf.Max(flash, pulse.Y * Mathf.Exp(-(_flashTime - pulse.X) * 18f));
            }
            if (_flashTime > 1f)
                _flashTime = -1f;
        }
        _lightningLight.Visible = flash > 0.01f;
        _lightningLight.LightEnergy = flash * LightningEnergy;
        _skyMaterial?.SetShaderParameter("lightning_flash", flash);
    }

    private void Apply(float dt)
    {
        WeatherPreset c = Current;
        // Gusts: slow noise around the base wind speed.
        float gust = Mathf.Clamp(_gustNoise.GetNoise1D(_time) * 1.6f + 0.5f, 0f, 1f);
        float windSpeed = c.WindSpeed * (1f + c.Gustiness * (gust * 1.4f - 0.4f));
        float heading = Mathf.DegToRad(c.WindDirectionDegrees);
        Vector2 wind = new Vector2(Mathf.Sin(heading), -Mathf.Cos(heading)) * windSpeed;
        float darkness = 1f - c.SceneBrightness;

        if (_skyMaterial != null)
        {
            _cloudOffset += wind * CloudWindMultiplier * dt;
            if (_cloudPeriod > 0f)
                _cloudOffset = new Vector2(Mathf.PosMod(_cloudOffset.X, _cloudPeriod), Mathf.PosMod(_cloudOffset.Y, _cloudPeriod));
            _skyMaterial.SetShaderParameter("cloud_coverage", c.CloudCoverage);
            _skyMaterial.SetShaderParameter("cloud_density", c.CloudDensity);
            _skyMaterial.SetShaderParameter("cloud_darkness", c.CloudDarkness);
            _skyMaterial.SetShaderParameter("overcast", c.Overcast);
            _skyMaterial.SetShaderParameter("cloud_offset", _cloudOffset);
        }

        // How much direct sunlight still reaches the air below the clouds. Fog sits under the cloud layer,
        // so it only scatters sunlight that gets through.
        float sunThroughClouds = (1f - c.Overcast) * (1f - 0.6f * c.CloudCoverage * c.CloudDensity);
        if (Sun != null)
        {
            // A thick overcast leaves no direct sunlight; the sky lights everything. Smooth curve: gone well
            // before the sky is fully closed.
            float directSun = c.SunEnergyScale * (1f - Mathf.SmoothStep(0.25f, 0.8f, c.Overcast));
            Sun.LightEnergy = _baseSunEnergy * directSun;
            Sun.LightVolumetricFogEnergy = _baseSunFogEnergy * sunThroughClouds;
            // Clouds spread the sunlight like a softbox: softer shadows as the sky closes.
            Sun.ShadowBlur = Mathf.Lerp(_baseSunShadowBlur, OvercastShadowBlur, 1f - sunThroughClouds);
            // Diffuse light under clouds makes no sharp highlights (wet floors would otherwise show a hard
            // sun glint even in a storm).
            Sun.LightSpecular = _baseSunSpecular * (1f - Mathf.SmoothStep(0.1f, 0.5f, c.Overcast));
            // A sun with no energy left is switched off entirely: one less light.
            Sun.Visible = _baseSunVisible && directSun > 0.0005f;
            // Shadows fade out with the sun instead of switching off (no popping).
            float shadowFade = Mathf.SmoothStep(0f, 0.2f, directSun);
            Sun.ShadowOpacity = _baseSunShadowOpacity * shadowFade;
            Sun.ShadowEnabled = _baseSunShadows && shadowFade > 0.001f;
        }
        ApplyExposure();
        if (_environment != null)
        {
            float skyLight = 1f - StormSkyDimming * c.CloudDarkness;
            _environment.BackgroundEnergyMultiplier = _baseBackgroundEnergy * skyLight;
            _environment.VolumetricFogDensity = _baseFogDensity + c.FogDensity;
            // Fog is lit by the sky (ambient inject) like real haze, so only tint it grey.
            _environment.VolumetricFogAlbedo = _baseFogAlbedo.Lerp(StormFogColor, darkness * 0.5f);
            _environment.VolumetricFogAmbientInject = 1f;
            // Weather fog would hide the cloud deck entirely; let it show through.
            _environment.VolumetricFogSkyAffect = _baseFogSkyAffect * Mathf.Lerp(1f, 0.1f, 1f - sunThroughClouds);
            // Overcast light comes from everywhere, so no bright glow toward the sun.
            _environment.VolumetricFogAnisotropy = _baseFogAnisotropy * sunThroughClouds;
            // Distance fog is an unlit color: give it the sky's horizon color at the sky's brightness so it
            // blends into the horizon.
            _environment.FogEnabled = c.DistanceFog > 0.0005f;
            _environment.FogDensity = c.DistanceFog;
            _environment.FogLightColor = HorizonColor() * _baseBackgroundEnergy * skyLight;
            _environment.FogLightEnergy = 1f;
            _environment.FogSkyAffect = c.Overcast * 0.5f;
        }

        Rain!.Intensity = c.RainIntensity;
        Rain.Wind = new Vector3(wind.X, 0f, wind.Y) * RainWindMultiplier;
        if (_environment != null)
            Rain.CurtainColor = _environment.FogLightColor;

        if (Audio != null)
        {
            Audio.RainAmount = c.RainIntensity;
            Audio.WindAmount = Mathf.Clamp(windSpeed / 20f, 0f, 1f);
        }

        // Surfaces lag behind the rain: they wet over seconds and dry over minutes.
        if (c.RainIntensity > 0.02f)
            _wetness = Mathf.MoveToward(_wetness, 1f, dt / WettingTime * (0.3f + 0.7f * c.RainIntensity));
        else
            _wetness = Mathf.MoveToward(_wetness, 0f, dt / DryingTime);
        RenderingServer.GlobalShaderParameterSet(WetnessGlobal, _wetness);
        RenderingServer.GlobalShaderParameterSet(RainGlobal, c.RainIntensity);
    }

    /// <summary>Darkens the picture for thick clouds. Auto exposure would undo a plain exposure change, so
    /// with it on, dark weather lowers its target brightness.</summary>
    private void ApplyExposure()
    {
        if (_cameraAttributes == null)
            return;
        if (_cameraAttributes.AutoExposureEnabled)
        {
            _cameraAttributes.AutoExposureScale = _baseAutoExposureScale * Current.SceneBrightness;
            _cameraAttributes.ExposureMultiplier = _baseExposure;
        }
        else
        {
            _cameraAttributes.AutoExposureScale = _baseAutoExposureScale;
            _cameraAttributes.ExposureMultiplier = _baseExposure * Current.SceneBrightness;
        }
    }

    /// <summary>The sky shader's horizon color for the current weather (matches sky_gradient()).</summary>
    private Color HorizonColor()
    {
        Color overcastGrey = _skyOvercastColor * (1f - 0.65f * Current.CloudDarkness);
        return _skyHorizonColor.Lerp(overcastGrey, Current.Overcast);
    }

    private void CaptureSceneBase()
    {
        if (WorldEnvironment != null)
        {
            _environment = WorldEnvironment.Environment;
            _cameraAttributes = WorldEnvironment.CameraAttributes;
        }
        if (_cameraAttributes != null)
        {
            _baseExposure = _cameraAttributes.ExposureMultiplier;
            _baseAutoExposureScale = _cameraAttributes.AutoExposureScale;
        }
        if (_environment != null)
        {
            _baseFogDensity = _environment.VolumetricFogDensity;
            _baseFogAlbedo = _environment.VolumetricFogAlbedo;
            _baseFogAmbientInject = _environment.VolumetricFogAmbientInject;
            _baseBackgroundEnergy = _environment.BackgroundEnergyMultiplier;
            _baseFogAnisotropy = _environment.VolumetricFogAnisotropy;
            _baseFogSkyAffect = _environment.VolumetricFogSkyAffect;
            _baseFogEnabled = _environment.FogEnabled;
            _baseFogDistanceDensity = _environment.FogDensity;
            _baseFogLightColor = _environment.FogLightColor;
            _baseFogLightEnergy = _environment.FogLightEnergy;
            _baseFogSkyAffectDistance = _environment.FogSkyAffect;
            if (_environment.Sky?.SkyMaterial is ShaderMaterial sky)
            {
                _skyMaterial = sky;
                // Read once: querying shader defaults stalls the render thread. Null without a GPU
                // (headless); keep the defaults then.
                if (SkyParameter("sky_horizon_color").VariantType == Variant.Type.Color)
                    _skyHorizonColor = SkyParameter("sky_horizon_color").AsColor();
                if (SkyParameter("overcast_color").VariantType == Variant.Type.Color)
                    _skyOvercastColor = SkyParameter("overcast_color").AsColor();
                Variant cloudScale = SkyParameter("cloud_scale");
                // Cloud textures tile every 1 / cloud_scale meters; wrap drift there to keep precision.
                float scale = cloudScale.VariantType == Variant.Type.Float ? cloudScale.AsSingle() : 0f;
                _cloudPeriod = scale > 0f ? 1f / scale : 0f;
                _skyMaterial.SetShaderParameter("lightning_color", LightningColor);
            }
        }
        if (_skyMaterial == null)
            GD.PushWarning("WeatherSystem: the environment's sky should use gamekit_weather/resources/weather_sky_material.tres; clouds are disabled.");
        if (Sun != null)
        {
            _baseSunEnergy = Sun.LightEnergy;
            _baseSunFogEnergy = Sun.LightVolumetricFogEnergy;
            _baseSunShadows = Sun.ShadowEnabled;
            _baseSunShadowBlur = Sun.ShadowBlur;
            _baseSunSpecular = Sun.LightSpecular;
            _baseSunShadowOpacity = Sun.ShadowOpacity;
            _baseSunVisible = Sun.Visible;
        }
    }

    /// <summary>Slow (syncs with the render thread); only call at setup.</summary>
    private Variant SkyParameter(StringName name)
    {
        Variant value = _skyMaterial!.GetShaderParameter(name);
        if (value.VariantType == Variant.Type.Nil && _skyMaterial.Shader != null)
            value = RenderingServer.ShaderGetParameterDefault(_skyMaterial.Shader.GetRid(), name);
        return value;
    }

    private void EnsureCycleAction()
    {
        if (CycleAction.IsEmpty || InputMap.HasAction(CycleAction))
            return;
        InputMap.AddAction(CycleAction);
        InputMap.ActionAddEvent(CycleAction, new InputEventKey { PhysicalKeycode = Key.F7 });
    }

    private static T? FindFirst<T>(Node root) where T : Node
    {
        Array<Node> found = root.FindChildren("*", typeof(T).Name, true, false);
        return found.Count > 0 ? (T)found[0] : null;
    }
}
