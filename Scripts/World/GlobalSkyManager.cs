using Godot;

namespace SzeneGenerator;

/// <summary>
/// Manages global sky, atmosphere, fog, and weather effects such as rain and snow.
/// Follows the camera position and applies region-specific sky presets and weather profiles.
/// </summary>
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

    // anker point Camera
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

    // Rain intensity scaling (0..1)
    [Export] private int _rainAmountMin = 800;
    [Export] private int _rainAmountMax = 7000;
    [Export] private float _rainGravityMin = 18.0f;
    [Export] private float _rainGravityMax = 45.0f;
    [Export] private float _rainVelMin = 8.0f;
    [Export] private float _rainVelMax = 24.0f;

    private float _rainIntensity01 = 1.0f;

    private GpuParticles3D _rainParticles;
    private Node3D _rainRig;

    // Snow (visual only)
    [Export] private Vector3 _snowOffset = new(0, 10, 0);

    // Snow intensity scaling (0..1)
    [Export] private int _snowAmountMin = 250;
    [Export] private int _snowAmountMax = 2500;
    [Export] private float _snowGravityMin = 1.0f;
    [Export] private float _snowGravityMax = 5.0f;

    private bool _snowEnabled;
    private float _snowIntensity01 = 0.0f;

    private Node3D _snowRig;
    private GpuParticles3D _snowParticles;

    // Region rules (contains LocalSkySettings loaded from JSON)
    private RegionRules _rules;

    // Cached SkyDome node (Sky3D)
    private Node _skyDome;

    // Sky instance
    private Node _skyInstance;

    // Sky anchor
    private Node3D _skyRoot3D;

    // Time of day (0.0 – <24.0)
    private float _timeOfDay = 12.0f;

    // Fog / environment
    private WorldEnvironment _worldEnv;

    // Cached profiles (so they can be applied after nodes are created)
    private string _currentRainProfile = "light";
    private string _currentSnowProfile = "light";

    public override void _Ready()
    {
        // Debug methodcall
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

        // Camera follow target (may be added after this manager)
        _followTarget = GetTree().CurrentScene.FindChild("MainCamera", true, false) as Node3D;
        if (_followTarget == null)
            GD.PushWarning("[Sky] Follow target not found (MainCamera).");

        // Rain setup (baseline)
        if (_rainEnabled)
        {
            CreateRainNodes();
            ConfigureRainBasics();
            ConfigureRainMaterial();
            ConfigureRainDrawPass();

            // Apply profile first (controls ranges/material feel), then intensity
            ApplyRainProfile(_currentRainProfile);
            SetRainIntensity(_rainIntensity01);
        }

        // Snow setup is created lazily when enabled

        // Fog / environment setup
        EnsureWorldEnvironment();
    }

    public override void _Process(double delta)
    {
        // Late-resolve the follow target because the camera may be added after this manager's _Ready()
        if (_followTarget == null)
        {
            _followTarget = GetTree().CurrentScene.FindChild("MainCamera", true, false) as Node3D;
            if (_followTarget == null)
                return;
        }

        // Sky root follows camera position (no rotation on purpose)
        if (_skyRoot3D != null)
            _skyRoot3D.GlobalPosition = _followTarget.GlobalPosition;

        // Rain follows camera
        if (_rainEnabled && _rainRig != null)
        {
            _rainRig.GlobalPosition = _followTarget.GlobalPosition + _rainOffset;

            // Keep yaw alignment only (optional). If you don't want any rotation, remove this block.
            var t = _rainRig.GlobalTransform;
            t.Basis = new Basis(new Vector3(0, 1, 0), _followTarget.GlobalBasis.GetEuler().Y);
            _rainRig.GlobalTransform = t;
        }

        // Snow follows camera
        if (_snowEnabled && _snowRig != null)
        {
            _snowRig.GlobalPosition = _followTarget.GlobalPosition + _snowOffset;
        }
    }

    // Import settings
    public void Configure(GlobalSkySettings cfg) => _cfg = cfg;

    // Configure region rules (loaded from JSON)
    public void Configure(RegionRules rules) => _rules = rules;

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

    // -------------------------
    // Rain implementation
    // -------------------------

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

        // Particle count and lifetime (amount gets overridden by intensity)
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

                // Apply cached profile first, then intensity
                ApplyRainProfile(_currentRainProfile);
                SetRainIntensity(_rainIntensity01);

                // Place rain rig immediately at the camera
                if (_followTarget != null)
                    _rainRig.GlobalPosition = _followTarget.GlobalPosition + _rainOffset;
            }
            else
            {
                _rainParticles.Emitting = true;

                // Ensure the cached profile is applied after re-enable
                ApplyRainProfile(_currentRainProfile);
            }
        }
        else
        {
            // Stop emitting (keep nodes for fast re-enable)
            if (_rainParticles != null)
                _rainParticles.Emitting = false;
        }
    }

    // Scales rain visuals based on intensity in range [0..1].
    /// <summary>
    /// Applies rain intensity scaling in the range [0..1] and updates particle behavior.
    /// </summary>
    private void SetRainIntensity(float intensity01)
    {
        _rainIntensity01 = Mathf.Clamp(intensity01, 0f, 1f);

        if (_rainParticles == null)
            return;

        _rainParticles.Amount = Mathf.RoundToInt(Mathf.Lerp(_rainAmountMin, _rainAmountMax, _rainIntensity01));

        if (_rainParticles.ProcessMaterial is ParticleProcessMaterial mat)
        {
            var g = Mathf.Lerp(_rainGravityMin, _rainGravityMax, _rainIntensity01);
            var v = Mathf.Lerp(_rainVelMin, _rainVelMax, _rainIntensity01);

            mat.Gravity = new Vector3(0, -g, 0);
            mat.InitialVelocityMin = v * 0.7f;
            mat.InitialVelocityMax = v * 1.15f;

            // Keep the emission box from exports
            mat.EmissionBoxExtents = _rainBoxExtents;
        }
    }

    // Applies a discrete rain visual profile ("light", "heavy", "storm").
    // Profiles modify amount ranges and some material parameters to create clearly different looks.
    /// <summary>
    /// Applies a discrete rain visual profile ("light", "heavy", "storm").
    /// Profiles control particle density, velocity, gravity, and appearance.
    /// </summary>
    private void ApplyRainProfile(string profile)
    {
        _currentRainProfile = (profile ?? "light").Trim().ToLowerInvariant();

        if (_rainParticles == null)
            return;

        if (!(_rainParticles.ProcessMaterial is ParticleProcessMaterial mat))
            return;

        switch (_currentRainProfile)
        {
            case "storm":
                _rainAmountMin = 2500;
                _rainAmountMax = 14000;

                mat.Spread = 7.5f;
                // Slightly longer streak feeling: faster fall
                _rainGravityMin = 28.0f;
                _rainGravityMax = 70.0f;
                _rainVelMin = 14.0f;
                _rainVelMax = 40.0f;

                if (_rainParticles.MaterialOverride is StandardMaterial3D smS)
                    smS.AlbedoColor = new Color(1f, 1f, 1f, 0.35f);
                break;

            case "heavy":
                _rainAmountMin = 1400;
                _rainAmountMax = 9000;

                mat.Spread = 6.0f;
                _rainGravityMin = 22.0f;
                _rainGravityMax = 55.0f;
                _rainVelMin = 10.0f;
                _rainVelMax = 30.0f;

                if (_rainParticles.MaterialOverride is StandardMaterial3D smH)
                    smH.AlbedoColor = new Color(1f, 1f, 1f, 0.30f);
                break;

            default: // "light"
                _rainAmountMin = 600;
                _rainAmountMax = 4500;

                mat.Spread = 5.0f;
                _rainGravityMin = 18.0f;
                _rainGravityMax = 45.0f;
                _rainVelMin = 8.0f;
                _rainVelMax = 24.0f;

                if (_rainParticles.MaterialOverride is StandardMaterial3D smL)
                    smL.AlbedoColor = new Color(1f, 1f, 1f, 0.22f);
                break;
        }
    }

    // -------------------------
    // Snow implementation (visual only)
    // -------------------------

    private void CreateSnowNodes()
    {
        _snowRig = new Node3D { Name = "SnowRig" };
        AddChild(_snowRig);

        _snowParticles = new GpuParticles3D { Name = "SnowParticles" };
        _snowRig.AddChild(_snowParticles);
    }

    private void ConfigureSnowBasics()
    {
        _snowParticles.Emitting = true;
        _snowParticles.OneShot = false;

        _snowParticles.Amount = 1200;
        _snowParticles.Lifetime = 6.0f;

        _snowParticles.VisibilityAabb = new Aabb(
            new Vector3(-200, -200, -200),
            new Vector3(400, 400, 400));
    }

    private void ConfigureSnowMaterial()
    {
        var mat = new ParticleProcessMaterial();
        _snowParticles.ProcessMaterial = mat;

        // Emission shape: box (snow generally needs a larger area than rain)
        mat.EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Box;
        mat.EmissionBoxExtents = new Vector3(_rainBoxExtents.X * 1.3f, _rainBoxExtents.Y + 2.0f, _rainBoxExtents.Z * 1.3f);

        // Gentle downward direction with slight spread
        mat.Direction = new Vector3(0, -1, 0);
        mat.Spread = 25.0f;

        // Slow fall
        mat.InitialVelocityMin = 0.6f;
        mat.InitialVelocityMax = 1.6f;

        // Drift-like gravity
        mat.Gravity = new Vector3(0, -3.5f, 0);

        // Flake size variation
        mat.ScaleMin = 0.6f;
        mat.ScaleMax = 1.4f;

        // Add rotation randomness
        mat.AngularVelocityMin = -2.0f;
        mat.AngularVelocityMax = 2.0f;
    }

    private void ConfigureSnowDrawPass()
    {
        // Simple quad as a snow flake (unshaded white)
        var quad = new QuadMesh
        {
            Size = new Vector2(0.06f, 0.06f)
        };

        _snowParticles.DrawPass1 = quad;

        var mat = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            DepthDrawMode = BaseMaterial3D.DepthDrawModeEnum.Disabled,
            NoDepthTest = true,
            BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled,
            AlbedoColor = new Color(1f, 1f, 1f, 0.35f)
        };

        _snowParticles.MaterialOverride = mat;
    }

    private void SetSnowEnabled(bool enabled)
    {
        if (_snowEnabled == enabled) return;
        _snowEnabled = enabled;

        if (_snowEnabled)
        {
            if (_snowRig == null || _snowParticles == null)
            {
                CreateSnowNodes();
                ConfigureSnowBasics();
                ConfigureSnowMaterial();
                ConfigureSnowDrawPass();

                // Apply cached profile first, then intensity
                ApplySnowProfile(_currentSnowProfile);
                SetSnowIntensity(_snowIntensity01);

                if (_followTarget != null)
                    _snowRig.GlobalPosition = _followTarget.GlobalPosition + _snowOffset;
            }
            else
            {
                _snowParticles.Emitting = true;

                // Ensure the cached profile is applied after re-enable
                ApplySnowProfile(_currentSnowProfile);
            }
        }
        else
        {
            if (_snowParticles != null)
                _snowParticles.Emitting = false;
        }
    }

    // Scales snow visuals based on intensity in range [0..1].
    private void SetSnowIntensity(float intensity01)
    {
        _snowIntensity01 = Mathf.Clamp(intensity01, 0f, 1f);

        if (_snowParticles == null)
            return;

        _snowParticles.Amount = Mathf.RoundToInt(Mathf.Lerp(_snowAmountMin, _snowAmountMax, _snowIntensity01));

        if (_snowParticles.ProcessMaterial is ParticleProcessMaterial mat)
        {
            var g = Mathf.Lerp(_snowGravityMin, _snowGravityMax, _snowIntensity01);
            mat.Gravity = new Vector3(0, -g, 0);
        }
    }

    // Applies a discrete snow visual profile ("light", "heavy", "blizzard").
    // Profiles primarily change emission volume, flake size, drift, and amount range.
    private void ApplySnowProfile(string profile)
    {
        _currentSnowProfile = (profile ?? "light").Trim().ToLowerInvariant();

        if (_snowParticles == null)
            return;

        if (!(_snowParticles.ProcessMaterial is ParticleProcessMaterial mat))
            return;

        switch (_currentSnowProfile)
        {
            case "blizzard":
                _snowAmountMin = 1800;
                _snowAmountMax = 9000;

                // Smaller volume -> higher perceived density
                mat.EmissionBoxExtents = new Vector3(10f, 6f, 10f);

                // Bigger flakes + higher alpha -> more visible
                mat.ScaleMin = 1.8f;
                mat.ScaleMax = 3.4f;

                // More drift
                mat.Direction = new Vector3(0.25f, -1f, 0.15f);
                mat.Spread = 55f;

                // Slightly stronger fall
                mat.Gravity = new Vector3(0, -4.8f, 0);

                if (_snowParticles.MaterialOverride is StandardMaterial3D smB)
                    smB.AlbedoColor = new Color(1f, 1f, 1f, 0.65f);
                break;

            case "heavy":
                _snowAmountMin = 900;
                _snowAmountMax = 4500;

                mat.EmissionBoxExtents = new Vector3(12f, 6f, 12f);

                mat.ScaleMin = 1.3f;
                mat.ScaleMax = 2.6f;

                mat.Direction = new Vector3(0.15f, -1f, 0.08f);
                mat.Spread = 40f;

                mat.Gravity = new Vector3(0, -3.8f, 0);

                if (_snowParticles.MaterialOverride is StandardMaterial3D smH)
                    smH.AlbedoColor = new Color(1f, 1f, 1f, 0.55f);
                break;

            default: // "light"
                _snowAmountMin = 250;
                _snowAmountMax = 1500;

                mat.EmissionBoxExtents = new Vector3(14f, 7f, 14f);

                mat.ScaleMin = 0.9f;
                mat.ScaleMax = 1.8f;

                mat.Direction = new Vector3(0.08f, -1f, 0.04f);
                mat.Spread = 28f;

                mat.Gravity = new Vector3(0, -3.2f, 0);

                if (_snowParticles.MaterialOverride is StandardMaterial3D smL)
                    smL.AlbedoColor = new Color(1f, 1f, 1f, 0.45f);
                break;
        }
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
    // ---------------------------------------------------------------------
    private static LocalSkySettings GetLocalPreset(string preset)
    {
        // Normalize input so JSON can use "clear", "Clear", "CLEAR", etc.
        preset = (preset ?? "Cloudy").Trim().ToLowerInvariant();

        return preset switch
        {
            "clear" => new LocalSkySettings
            {
                Preset = "Clear",

                CirrusCoverage = 0.10f,
                CirrusThickness = 0.60f,

                CumulusCoverage = 0.00f,
                CumulusThickness = 0.00f,

                WindSpeed = 0.6f,
                AtmDarkness = 0.45f,
                Exposure = 1.05f,

                FogVisible = false,
                FogDensity = null,
                FogStart = null,
                FogEnd = null,
                FogFalloff = null,

                RainEnabled = false,
                RainIntensity = 0.0f,
                RainProfile = "light",

                SnowEnabled = false,
                SnowIntensity = 0.0f,
                SnowProfile = "light"
            },

            "stormy" => new LocalSkySettings
            {
                Preset = "Stormy",

                CirrusCoverage = 0.60f,
                CirrusThickness = 1.60f,

                CumulusCoverage = 0.92f,
                CumulusThickness = 0.06f,

                WindSpeed = 2.0f,
                AtmDarkness = 0.70f,
                Exposure = 0.85f,

                FogVisible = true,
                FogDensity = 0.0035f,
                FogStart = 0f,
                FogEnd = 250f,
                FogFalloff = 2.0f,

                RainEnabled = true,
                RainIntensity = 0.85f,
                RainProfile = "storm",

                SnowEnabled = false,
                SnowIntensity = 0.0f,
                SnowProfile = "heavy"
            },

            _ => new LocalSkySettings
            {
                Preset = "Cloudy",

                CirrusCoverage = 0.45f,
                CirrusThickness = 1.20f,

                CumulusCoverage = 0.60f,
                CumulusThickness = 0.03f,

                WindSpeed = 1.2f,
                AtmDarkness = 0.55f,
                Exposure = 0.95f,

                FogVisible = true,
                FogDensity = 0.0015f,
                FogStart = 50f,
                FogEnd = 700f,
                FogFalloff = 1.2f,

                RainEnabled = false,
                RainIntensity = 0.0f,
                RainProfile = "light",

                SnowEnabled = false,
                SnowIntensity = 0.0f,
                SnowProfile = "light"
            }
        };
    }

    // Applies LocalSkySettings to Sky3D's SkyDome (preset + JSON overrides).
    public void ApplyLocalSkySettings(LocalSkySettings settings)
    {
        if (settings == null) return;

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
        if (overrides.RainIntensity.HasValue) target.RainIntensity = overrides.RainIntensity.Value;
        if (!string.IsNullOrWhiteSpace(overrides.RainProfile)) target.RainProfile = overrides.RainProfile;

        if (overrides.SnowEnabled.HasValue) target.SnowEnabled = overrides.SnowEnabled.Value;
        if (overrides.SnowIntensity.HasValue) target.SnowIntensity = overrides.SnowIntensity.Value;
        if (!string.IsNullOrWhiteSpace(overrides.SnowProfile)) target.SnowProfile = overrides.SnowProfile;
    }

    private void ApplyToSkyDome(LocalSkySettings s)
    {
        // Cirrus (high thin clouds)
        if (s.CirrusCoverage.HasValue)
        {
            _skyDome.Set("cirrus_visible", s.CirrusCoverage.Value > 0.001f);
            _skyDome.Set("cirrus_coverage", s.CirrusCoverage.Value);
        }

        if (s.CirrusThickness.HasValue)
            _skyDome.Set("cirrus_thickness", s.CirrusThickness.Value);

        // Cumulus (low clouds)
        if (s.CumulusCoverage.HasValue)
        {
            _skyDome.Set("cumulus_visible", s.CumulusCoverage.Value > 0.001f);
            _skyDome.Set("cumulus_coverage", s.CumulusCoverage.Value);
        }

        if (s.CumulusThickness.HasValue)
            _skyDome.Set("cumulus_thickness", s.CumulusThickness.Value);

        // Wind
        if (s.WindSpeed.HasValue)
            _skyDome.Set("wind_speed", s.WindSpeed.Value);
        if (s.WindDirection.HasValue)
            _skyDome.Set("wind_direction", s.WindDirection.Value);

        // Atmosphere / exposure
        if (s.AtmDarkness.HasValue)
            _skyDome.Set("atm_darkness", s.AtmDarkness.Value);
        if (s.Exposure.HasValue)
            _skyDome.Set("exposure", s.Exposure.Value);

        // Fog
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

        // ---- Rain: profile -> enable -> intensity ----
        if (!string.IsNullOrWhiteSpace(s.RainProfile))
        {
            _currentRainProfile = s.RainProfile;
            ApplyRainProfile(_currentRainProfile);
        }

        if (s.RainEnabled.HasValue)
            SetRainEnabled(s.RainEnabled.Value);

        // Apply profile again after enabling (important when nodes are created lazily)
        if (_rainEnabled)
            ApplyRainProfile(_currentRainProfile);

        if (s.RainIntensity.HasValue)
            SetRainIntensity(s.RainIntensity.Value);

        // ---- Snow: profile -> enable -> intensity ----
        if (!string.IsNullOrWhiteSpace(s.SnowProfile))
        {
            _currentSnowProfile = s.SnowProfile;
            ApplySnowProfile(_currentSnowProfile);
        }

        if (s.SnowEnabled.HasValue)
            SetSnowEnabled(s.SnowEnabled.Value);

        // Apply profile again after enabling (important when nodes are created lazily)
        if (_snowEnabled)
            ApplySnowProfile(_currentSnowProfile);

        if (s.SnowIntensity.HasValue)
            SetSnowIntensity(s.SnowIntensity.Value);
    }
}
