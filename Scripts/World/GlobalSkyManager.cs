using Godot;

namespace SzeneGenerator;

public partial class GlobalSkyManager : Node
{
    // Configuration input
    private GlobalSkySettings _cfg;

    // Sky instance
    private Node _skyInstance;
    
    //Cached SkyDome node(Sky3D)
    private Node _skyDome;
    
    //Region rules (contains LocalSkySettings loaded from JSON)
    private RegionRules _rules;

    // Rain
    private Node3D _rainRig;
    private GpuParticles3D _rainParticles;
    private Node3D _followTarget;

    // Fog / environment
    private WorldEnvironment _worldEnv;
    private Environment _env;

    // Rain enabled/disabled
    [Export] private bool _rainEnabled = true;

    // Rain rig offset relative to the camera
    [Export] private Vector3 _rainOffset = new Vector3(0, 8, 0);

    // Rain emission box size
    [Export] private Vector3 _rainBoxExtents = new Vector3(20, 1, 20);

    // Cached baseline fog values
    private bool _baseFogEnabled;
    private float _baseFogDensity;
    private Color _baseFogLightColor;
    private float _baseFogLightEnergy;

    // Rain → fog interaction
    [Export] private bool _fogAdjustForRain = true;

    // Higher value = denser fog
    // 0.004 = very light rain
    // 0.008 = moderate rain
    // 0.012 = heavy rain
    // > 0.012 = very thick coastal fog
    [Export] private float _rainFogDensity = 0.012f;

    // Reduces sunlight contribution during rain
    [Export] private float _rainFogSunAmountMultiplier = 0.5f;

    // Cooler fog color during rain
    [Export] private Color _rainFogLightTint = new Color(0.85f, 0.9f, 1.0f, 1.0f);

    // Time of day (0.0 – <24.0)
    private float _timeOfDay = 12.0f;

    public GlobalSkyManager()
    {
    }

    public override void _Ready()
    {
        // Apply configuration overrides (if any)
        ApplyConfigOverridesIfAny();

        // Load and instantiate sky scene
        var skyScene = GD.Load<PackedScene>("res://Sky.tscn");
        _skyInstance = skyScene.Instantiate();
        _skyInstance.Name = "Sky";
        AddChild(_skyInstance);
        // Apply local sky (preset + JSON overrides) if provided by region rules
        ApplyLocalSkySettings(_rules?.LocalSky);

        ApplyStartupParameters();

        // Camera follow target
        _followTarget = GetParent().GetNodeOrNull<Node3D>("MainCamera");
        if (_followTarget == null)
        {
            GD.PushWarning("Follow target not found");
        }

        // Rain setup
        if (_rainEnabled)
        {
            CreateRainNodes();
            ConfigureRainBasics();
            ConfigureRainMaterial();
            ConfigureRainDrawPass();
        }

        // Fog / environment setup
        EnsureWorldEnvironment();
        CacheBaseFog();
        ApplyFogForRain(_rainEnabled);

        // Apply environment override to camera
        if (_followTarget is Camera3D cam)
        {
            cam.Environment = _env;
            cam.Attributes = null;
        }
        else
        {
            GD.PushWarning("[Fog] FollowTarget is not a Camera3D - fog override not possible.");
        }
    }

    public override void _Process(double delta)
    {
        if (!_rainEnabled) return;
        if (_followTarget == null) return;

        // Keep rain rig centered on the camera
        _rainRig.GlobalPosition = _followTarget.GlobalPosition + _rainOffset;

        // Rotate rain rig to match camera Y rotation
        var t = _rainRig.GlobalTransform;
        t.Basis = new Basis(new Vector3(0, 1, 0), _followTarget.GlobalBasis.GetEuler().Y);
        _rainRig.GlobalTransform = t;
    }

    //
    public void Configure(GlobalSkySettings cfg)
    {
        _cfg = cfg;
    }
    
    //Configure region rules (loaded from JSON)
    public void Configure(RegionRules rules)
    {
        _rules = rules;
    }

    private void ApplyStartupParameters()
    {
        // Locate TimeOfDay node inside Sky.tscn
        var tod = _skyInstance.GetNodeOrNull<Node>("TimeOfDay");

        // Set initial time of day
        tod.Set("current_time", _timeOfDay);
    }

    private void EnsureWorldEnvironment()
    {
        // 1) Try to find WorldEnvironment inside the Sky scene
        _worldEnv = _skyInstance.FindChild("WorldEnvironment", true, false) as WorldEnvironment;

        // 2) Fallback: search in current scene
        if (_worldEnv == null)
            _worldEnv = GetTree().CurrentScene.FindChild("WorldEnvironment", true, false) as WorldEnvironment;

        // 3) If still not found, create one
        if (_worldEnv == null)
        {
            _worldEnv = new WorldEnvironment { Name = "WorldEnvironment" };
            GetTree().CurrentScene.AddChild(_worldEnv);
        }

        // Ensure Environment resource exists
        _env = _worldEnv.Environment ?? new Environment();
        _worldEnv.Environment = _env;

        GD.Print($"[Fog] Using WorldEnvironment: {_worldEnv.GetPath()}");
    }

    private void CacheBaseFog()
    {
        _baseFogEnabled = _env.FogEnabled;
        _baseFogDensity = _env.FogDensity;
        _baseFogLightColor = _env.FogLightColor;
        _baseFogLightEnergy = _env.FogLightEnergy;
    }

    private void ApplyFogForRain(bool raining)
    {
        if (!_fogAdjustForRain || _env == null) return;

        if (raining)
        {
            _env.FogEnabled = true;
            _env.FogMode = Environment.FogModeEnum.Exponential;

            // Directly controlled via inspector
            _env.FogDensity = _rainFogDensity;

            // Do not multiply with base color (can cause unexpected brightness)
            _env.FogLightColor = _rainFogLightTint;

            // Light energy scaled relative to base
            _env.FogLightEnergy = _baseFogLightEnergy * _rainFogSunAmountMultiplier;

            GD.Print($"[Fog] Applied rain fog: Density={_env.FogDensity}, LightEnergy={_env.FogLightEnergy}, Color={_env.FogLightColor}");
        }
        else
        {
            _env.FogEnabled = _baseFogEnabled;
            _env.FogMode = Environment.FogModeEnum.Exponential;

            _env.FogDensity = _baseFogDensity;
            _env.FogLightColor = _baseFogLightColor;
            _env.FogLightEnergy = _baseFogLightEnergy;

            GD.Print($"[Fog] Restored base fog: Density={_env.FogDensity}");
        }
    }

    private void CreateRainNodes()
    {
        _rainRig = new Node3D { Name = "RainRig" };
        AddChild(_rainRig);

        _rainParticles = new GpuParticles3D { Name = "RainParticles" };
        _rainRig.AddChild(_rainParticles);
    }

    private void ConfigureRainBasics()
    {
        _rainParticles.Emitting = true;
        _rainParticles.OneShot = false;

        // Particle count and lifetime
        _rainParticles.Amount = 6000;
        _rainParticles.Lifetime = 3.0f;

        _rainParticles.VisibilityAabb = new Aabb(
            new Vector3(-200, -200, -200),
            new Vector3(400, 400, 400));
    }

    private void ConfigureRainMaterial()
    {
        var mat = new ParticleProcessMaterial();
        _rainParticles.ProcessMaterial = mat;

        mat.ScaleMin = 0.8f;
        mat.ScaleMax = 1.2f;

        // Emission shape: box
        mat.EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Box;
        mat.EmissionBoxExtents = _rainBoxExtents;

        // Falling direction
        mat.Direction = new Vector3(0, -1, 0);
        mat.Spread = 5.0f;

        mat.InitialVelocityMin = 35.0f;
        mat.InitialVelocityMax = 55.0f;

        mat.Gravity = new Vector3(0, -40, 0);
    }

    private void ConfigureRainDrawPass()
    {
        // Thin capsule mesh as raindrop
        var capsule = new CapsuleMesh
        {
            Radius = 0.01f,
            Height = 0.6f
        };

        _rainParticles.DrawPass1 = capsule;

        var mat = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            DepthDrawMode = BaseMaterial3D.DepthDrawModeEnum.Disabled,
            NoDepthTest = true,
            BillboardMode = BaseMaterial3D.BillboardModeEnum.Disabled,
            AlbedoColor = new Color(1f, 1f, 1f, 0.25f)
        };

        _rainParticles.MaterialOverride = mat;
    }

    private void ApplyConfigOverridesIfAny()
    {
        if (_cfg == null) return;

        _rainEnabled = _cfg.RainEnabled;
        _rainOffset = _cfg.RainOffset;
        _rainBoxExtents = _cfg.RainBoxExtents;

        _fogAdjustForRain = _cfg.FogAdjustForRain;
        _rainFogDensity = _cfg.RainFogDensity;
        _rainFogSunAmountMultiplier = _cfg.RainFogSunAmountMultiplier;
        _rainFogLightTint = _cfg.RainFogLightTint;

        _timeOfDay = _cfg.TimeOfDay;
    }

    // ---------------------------------------------------------------------
    // Local sky preset defaults (for LocalSkySettings)
    // NOTE:
    // This method is currently not used.
    // It only provides baseline values for cloud/weather presets.
    // Region JSON values can later override individual parameters.
    // ---------------------------------------------------------------------
    private static LocalSkySettings GetLocalPreset(string preset)
    {
        // Normalize input so JSON can use "clear", "Clear", "CLEAR", etc.
        preset = (preset ?? "Cloudy").Trim().ToLowerInvariant();

        return preset switch
        {
            "clear" => new LocalSkySettings
            {
                // Clear sky: minimal low clouds, slight high-altitude haze
                Preset = "Clear",

                CirrusCoverage = 0.10f,
                CirrusThickness = 0.60f,

                CumulusCoverage = 0.00f,
                CumulusThickness = 0.00f,

                WindSpeed = 0.6f,
                AtmDarkness = 0.45f,
                Exposure = 1.05f,

                FogVisible = null,
                FogDensity = null,
                RainEnabled = null
            },

            "stormy" => new LocalSkySettings
            {
                // Stormy sky: dense low clouds and darker atmosphere
                Preset = "Stormy",

                CirrusCoverage = 0.60f,
                CirrusThickness = 1.60f,

                CumulusCoverage = 0.92f,
                CumulusThickness = 0.06f,

                WindSpeed = 2.0f,
                AtmDarkness = 0.70f,
                Exposure = 0.85f,

                FogVisible = true,
                FogDensity = 0.0015f,
                RainEnabled = true
            },

            _ => new LocalSkySettings
            {
                // Cloudy baseline
                Preset = "Cloudy",

                CirrusCoverage = 0.45f,
                CirrusThickness = 1.20f,

                CumulusCoverage = 0.60f,
                CumulusThickness = 0.03f,

                WindSpeed = 1.2f,
                AtmDarkness = 0.55f,
                Exposure = 0.95f,

                FogVisible = null,
                FogDensity = null,
                RainEnabled = null
            }
        };
    }
    
    // ---------------------------------------------------------------------
// Applies LocalSkySettings to Sky3D's SkyDome.
// - Loads preset defaults via GetLocalPreset()
// - Overwrites defaults with any non-null JSON overrides
// - Writes values to SkyDome via Set("property_name", value)
//
// NOTE:
// SkyDome also has its own fog properties (fog_visible, fog_density).
// You currently control fog via WorldEnvironment/Environment.
// If you want, you can still write SkyDome fog values too (see below),
// but be aware you may end up with "two fog systems" affecting visuals.
// ---------------------------------------------------------------------
public void ApplyLocalSkySettings(LocalSkySettings settings)
{
    if (settings == null) return;

    // Find SkyDome lazily (only when needed)
    EnsureSkyDome();

    if (_skyDome == null)
    {
        GD.PushWarning("[Sky] SkyDome node not found. Cannot apply LocalSkySettings.");
        return;
    }

    // 1) Start with preset defaults
    var merged = GetLocalPreset(settings.Preset);

    // 2) Overwrite with JSON overrides (only if set)
    MergeLocalSkySettings(merged, settings);

    // 3) Apply to SkyDome (Sky3D property names from the editor docs)
    ApplyToSkyDome(merged);
}

private void EnsureSkyDome()
{
    if (_skyDome != null) return;
    if (_skyInstance == null) return;

    // SkyDome is usually a direct child of Sky.tscn root
    _skyDome = _skyInstance.GetNodeOrNull<Node>("SkyDome");

    // Fallback: search anywhere under the Sky instance
    if (_skyDome == null)
        _skyDome = _skyInstance.FindChild("SkyDome", true, false);
}

private static void MergeLocalSkySettings(LocalSkySettings target, LocalSkySettings overrides)
{
    // Preset name itself is not important after merging; it is only used for lookup.

    if (overrides.CirrusCoverage.HasValue)   target.CirrusCoverage = overrides.CirrusCoverage.Value;
    if (overrides.CirrusThickness.HasValue)  target.CirrusThickness = overrides.CirrusThickness.Value;

    if (overrides.CumulusCoverage.HasValue)  target.CumulusCoverage = overrides.CumulusCoverage.Value;
    if (overrides.CumulusThickness.HasValue) target.CumulusThickness = overrides.CumulusThickness.Value;

    if (overrides.WindSpeed.HasValue)        target.WindSpeed = overrides.WindSpeed.Value;
    if (overrides.WindDirection.HasValue)    target.WindDirection = overrides.WindDirection.Value;

    if (overrides.AtmDarkness.HasValue)      target.AtmDarkness = overrides.AtmDarkness.Value;
    if (overrides.Exposure.HasValue)         target.Exposure = overrides.Exposure.Value;

    if (overrides.FogVisible.HasValue)       target.FogVisible = overrides.FogVisible.Value;
    if (overrides.FogDensity.HasValue)       target.FogDensity = overrides.FogDensity.Value;

    if (overrides.RainEnabled.HasValue)      target.RainEnabled = overrides.RainEnabled.Value;
}

private void ApplyToSkyDome(LocalSkySettings s)
{
    // --- Cirrus (high thin clouds) ---
    if (s.CirrusCoverage.HasValue)
    {
        _skyDome.Set("cirrus_visible", s.CirrusCoverage.Value > 0.001f);
        _skyDome.Set("cirrus_coverage", s.CirrusCoverage.Value);
    }
    if (s.CirrusThickness.HasValue)
        _skyDome.Set("cirrus_thickness", s.CirrusThickness.Value);

    // --- Cumulus (low clouds) ---
    if (s.CumulusCoverage.HasValue)
    {
        _skyDome.Set("cumulus_visible", s.CumulusCoverage.Value > 0.001f);
        _skyDome.Set("cumulus_coverage", s.CumulusCoverage.Value);
    }
    if (s.CumulusThickness.HasValue)
        _skyDome.Set("cumulus_thickness", s.CumulusThickness.Value);

    // --- Wind ---
    if (s.WindSpeed.HasValue)
        _skyDome.Set("wind_speed", s.WindSpeed.Value);
    if (s.WindDirection.HasValue)
        _skyDome.Set("wind_direction", s.WindDirection.Value);

    // --- Atmosphere / exposure ---
    if (s.AtmDarkness.HasValue)
        _skyDome.Set("atm_darkness", s.AtmDarkness.Value);
    if (s.Exposure.HasValue)
        _skyDome.Set("exposure", s.Exposure.Value);

    // --- Optional: SkyDome fog ---
    // If you want the SkyDome fog layer, uncomment these.
    // WARNING: You already use Environment fog; using both may look too strong.
    /*
    if (s.FogVisible.HasValue)
        _skyDome.Set("fog_visible", s.FogVisible.Value);
    if (s.FogDensity.HasValue)
        _skyDome.Set("fog_density", s.FogDensity.Value);
    */

    // --- Optional: RainEnabled ---
    // Your current rain setup creates particles only in _Ready() if _rainEnabled is true.
    // Toggling rain at runtime requires additional logic (create/remove rain nodes).
    // For now, we only store the value (no behavior change).
    if (s.RainEnabled.HasValue)
    {
        // Keep this as a stored flag. Runtime toggling needs extra code.
        _rainEnabled = s.RainEnabled.Value;
    }
}

    
    
}
