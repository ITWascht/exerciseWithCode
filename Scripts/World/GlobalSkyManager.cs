using Godot;

namespace SzeneGenerator;

public partial class GlobalSkyManager : Node
{
    private float _baseFogDensity;

    // Cached baseline fog values
    private bool _baseFogEnabled;
    private Color _baseFogLightColor;

    private float _baseFogLightEnergy;

    // Configuration input
    private GlobalSkySettings _cfg;
    private Environment _env;

    // Rain → fog interaction
    [Export] private bool _fogAdjustForRain = true;
    private Node3D _followTarget;

    // Rain emission box size
    [Export] private Vector3 _rainBoxExtents = new(20, 1, 20);

    // Rain enabled/disabled
    [Export] private bool _rainEnabled = true;

    // Higher value = denser fog
    // 0.004 = very light rain
    // 0.008 = moderate rain
    // 0.012 = heavy rain
    // > 0.012 = very thick coastal fog
    [Export] private float _rainFogDensity = 0.012f;

    // Cooler fog color during rain
    [Export] private Color _rainFogLightTint = new(0.85f, 0.9f, 1.0f);

    // Reduces sunlight contribution during rain
    [Export] private float _rainFogSunAmountMultiplier = 0.5f;

    // Rain rig offset relative to the camera
    [Export] private Vector3 _rainOffset = new(0, 8, 0);
    private GpuParticles3D _rainParticles;

    // Rain
    private Node3D _rainRig;

    //Region rules (contains LocalSkySettings loaded from JSON)
    private RegionRules _rules;

    //Cached SkyDome node(Sky3D)
    private Node _skyDome;

    // Sky instance
    private Node _skyInstance;

    // Time of day (0.0 – <24.0)
    private float _timeOfDay = 12.0f;

    // Fog / environment
    private WorldEnvironment _worldEnv;
    
    //Sky anker
    private Node3D _skyRoot3D;


    public override void _Ready()
    {
        //Debug methodcall
        GD.Print("[Sky] GlobalSkyManager _Ready() entered");

        // Apply configuration overrides (if any)
        ApplyConfigOverridesIfAny();

        // Load and instantiate sky scene
        var skyScene = GD.Load<PackedScene>("res://Sky.tscn");
        if (skyScene == null)
        {
            GD.PushError("[Sky] Failed to load res://Sky.tscn (PackedScene is null).");
            return;
        }
        _skyInstance = skyScene.Instantiate();
        _skyInstance.Name = "Sky";
        AddChild(_skyInstance);
        _skyRoot3D = _skyInstance as Node3D;
        GD.Print($"[Sky] Sky root type: {_skyInstance.GetType().Name}, is Node3D={_skyRoot3D != null}");

        // Apply local sky (preset + JSON overrides) if provided by region rules
        ApplyStartupParameters();
        CallDeferred(nameof(ApplyLocalSkyAfterInit));

        // Camera follow target
        // Find the main camera in the active scene tree (robust, independent of parenting)
        _followTarget = GetTree().CurrentScene.FindChild("MainCamera", true, false) as Node3D;
        if (_followTarget == null)
            GD.PushWarning("[Sky] Follow target not found (MainCamera).");

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
        //CacheBaseFog();
        // ApplyFogForRain(_rainEnabled);
    }

    public override void _Process(double delta)
    {
        if (_followTarget == null) return;

        // --- Sky root follows camera position ---
        if (_skyRoot3D != null)
        {
            _skyRoot3D.GlobalPosition = _followTarget.GlobalPosition;
        }

        // --- Rain ---
        if (_rainEnabled && _rainRig != null)
        {
            _rainRig.GlobalPosition = _followTarget.GlobalPosition + _rainOffset;

            var t = _rainRig.GlobalTransform;
            t.Basis = new Basis(new Vector3(0, 1, 0), _followTarget.GlobalBasis.GetEuler().Y);
            _rainRig.GlobalTransform = t;
        }
    }

    //import settings
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
        if (tod != null)
            tod.Set("current_time", _timeOfDay);
        else
            GD.PushWarning("[Sky] TimeOfDay node not found in Sky.tscn");

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

    private void ApplyLocalSkyAfterInit()
    {
        EnsureSkyDome();
        if (_skyDome == null) return;

        // Sky3D builds parts of the scene asynchronously; during build it may overwrite fog parameters.
        // Wait until SkyDome reports the scene is built.
        var built = _skyDome.Get("is_scene_built").AsBool();
        if (!built)
        {
            CallDeferred(nameof(ApplyLocalSkyAfterInit));
            return;
        }

        ApplyLocalSkySettings(_rules?.LocalSky);
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
    
    private void SetRainEnabled(bool enabled)
    {
        if (_rainEnabled == enabled) return;
        _rainEnabled = enabled;

        if (_rainEnabled)
        {
            // Create rain nodes if they don't exist yet
            if (_rainRig == null || _rainParticles == null)
            {
                CreateRainNodes();
                ConfigureRainBasics();
                ConfigureRainMaterial();
                ConfigureRainDrawPass();

                // Place rain rig immediately at the camera
                if (_followTarget != null)
                    _rainRig.GlobalPosition = _followTarget.GlobalPosition + _rainOffset;
            }
            else
            {
                _rainParticles.Emitting = true;
            }
        }
        else
        {
            // Stop emitting (keep nodes for fast re-enable)
            if (_rainParticles != null)
                _rainParticles.Emitting = false;
        }
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
                // Clear sky: minimal high clouds, no fog
                Preset = "Clear",

                CirrusCoverage = 0.10f,
                CirrusThickness = 0.60f,

                CumulusCoverage = 0.00f,
                CumulusThickness = 0.00f,

                WindSpeed = 0.6f,
                AtmDarkness = 0.45f,
                Exposure = 1.05f,

                // No fog in clear weather
                FogVisible = false,
                FogDensity = null,
                FogStart = null,
                FogEnd = null,
                FogFalloff = null,

                RainEnabled = false
            },

            "stormy" => new LocalSkySettings
            {
                // Stormy weather: heavy rain haze, reduced visibility (not thick fog)
                Preset = "Stormy",

                CirrusCoverage = 0.60f,
                CirrusThickness = 1.60f,

                CumulusCoverage = 0.92f,
                CumulusThickness = 0.06f,

                WindSpeed = 2.0f,
                AtmDarkness = 0.70f,
                Exposure = 0.85f,

                // Rain-induced haze: visibility reduced by rain, not fog wall
                FogVisible = true,
                FogDensity = 0.0035f,
                FogStart = 0f,
                FogEnd = 250f,
                FogFalloff = 2.0f,

                RainEnabled = true
            },

            _ => new LocalSkySettings
            {
                // Cloudy baseline: light atmospheric haze
                Preset = "Cloudy",

                CirrusCoverage = 0.45f,
                CirrusThickness = 1.20f,

                CumulusCoverage = 0.60f,
                CumulusThickness = 0.03f,

                WindSpeed = 1.2f,
                AtmDarkness = 0.55f,
                Exposure = 0.95f,

                // Light humidity haze, clear near camera
                FogVisible = true,
                FogDensity = 0.0015f,
                FogStart = 50f,
                FogEnd = 700f,
                FogFalloff = 1.2f,

                RainEnabled = false
            }
        };
    }

    // ---------------------------------------------------------------------
// Applies LocalSkySettings to Sky3D's SkyDome.
// - Loads preset defaults via GetLocalPreset()
// - Overwrites defaults with any non-null JSON overrides
// - Writes values to SkyDome via Set("property_name", value)
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
        if (_skyDome != null)
            GD.Print($"[Sky] SkyDome resolved to: {_skyDome.GetPath()} ({_skyDome.GetType().Name})");
        GD.Print($"[Sky] SkyDome type: {_skyDome?.GetType().Name}");

    }

    private static void MergeLocalSkySettings(LocalSkySettings target, LocalSkySettings overrides)
    {
        // Preset name itself is not important after merging; it is only used for lookup.

        if (overrides.CirrusCoverage.HasValue) target.CirrusCoverage = overrides.CirrusCoverage.Value;
        if (overrides.CirrusThickness.HasValue) target.CirrusThickness = overrides.CirrusThickness.Value;

        if (overrides.CumulusCoverage.HasValue) target.CumulusCoverage = overrides.CumulusCoverage.Value;
        if (overrides.CumulusThickness.HasValue) target.CumulusThickness = overrides.CumulusThickness.Value;

        if (overrides.WindSpeed.HasValue) target.WindSpeed = overrides.WindSpeed.Value;
        if (overrides.WindDirection.HasValue) target.WindDirection = overrides.WindDirection.Value;

        if (overrides.AtmDarkness.HasValue) target.AtmDarkness = overrides.AtmDarkness.Value;
        if (overrides.Exposure.HasValue) target.Exposure = overrides.Exposure.Value;

        if (overrides.FogVisible.HasValue) target.FogVisible = overrides.FogVisible.Value;
        if (overrides.FogDensity.HasValue) target.FogDensity = overrides.FogDensity.Value;
        if (overrides.FogStart.HasValue) target.FogStart = overrides.FogStart.Value;
        if (overrides.FogEnd.HasValue) target.FogEnd = overrides.FogEnd.Value;
        if (overrides.FogFalloff.HasValue) target.FogFalloff = overrides.FogFalloff.Value;
        
        if (overrides.RainEnabled.HasValue) target.RainEnabled = overrides.RainEnabled.Value;
        
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
        
        // Rain
        if (s.RainEnabled.HasValue)
            SetRainEnabled(s.RainEnabled.Value);

        // --- Optional: SkyDome fog ---
        if (s.FogVisible.HasValue || s.FogDensity.HasValue)
        {
            // Render fog on layer 1 so the default camera can see it.
            _skyDome.Set("fog_layers", 1);
            _skyDome.Set("fog_render_priority", 100);
        }
        
        if (s.FogVisible.HasValue)
            _skyDome.Set("fog_visible", s.FogVisible.Value);
        if (s.FogDensity.HasValue)
            _skyDome.Set("fog_density", s.FogDensity.Value);
        
        if (s.FogStart.HasValue)
            _skyDome.Set("fog_start", s.FogStart.Value);

        if (s.FogEnd.HasValue)
            _skyDome.Set("fog_end", s.FogEnd.Value);

        if (s.FogFalloff.HasValue)
            _skyDome.Set("fog_falloff", s.FogFalloff.Value);
        
        GD.Print($"[SkyFog] After Set -> end={_skyDome.Get("fog_end")}, falloff={_skyDome.Get("fog_falloff")}");



        // --- Optional: RainEnabled ---
        // Your current rain setup creates particles only in _Ready() if _rainEnabled is true.
        // Toggling rain at runtime requires additional logic (create/remove rain nodes).
        // For now, we only store the value (no behavior change).
        // if (s.RainEnabled.HasValue)
            // Keep this as a stored flag. Runtime toggling needs extra code.
            // _rainEnabled = s.RainEnabled.Value;
    }
}