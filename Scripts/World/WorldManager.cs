using Godot;
using System.Threading.Tasks;


namespace SzeneGenerator;

/// <summary>
/// Central coordinator responsible for building the procedural world.
/// Initializes sky, terrain, spawning, camera placement, and triggers
/// screenshot and metadata export.
/// </summary>

public partial class WorldManager : Node3D
{
    private GlobalSkyManager _skyManager;
    private TerrainManager _terrainManager;
    private SimpleCamera _mainCamera;
    private Node3D _skyRoot;


    //configimport Worlsettings
    /// <summary>
    /// Entry point of the world generation pipeline.
    /// Loads region rules, initializes environment systems, spawns terrain and objects,
    /// positions the camera, and produces the final screenshot and JSON output.
    /// </summary>
    public override async void _Ready()
    {
        GD.Print("=== WorldManager BUILD MARKER 2026-01-23 A ===");
        var settings = new WorldSettings();
        
        // Load region rules (for LocalSkySettings)
        var regionId = settings.Terrain.RegionId;
        var rulesPath = $"res://assets/regions/{regionId}/{regionId}.rules.json";
        var rules = RegionRulesLoader.Load(rulesPath);
        //Sky
        _skyManager = new GlobalSkyManager();
        _skyManager.Configure(settings.Sky); // existing global config
        _skyManager.Configure(rules); // NEW: region rules (LocalSkySettings)
        GD.Print("WorldManager: Adding GlobalSkyManager...");
        AddChild(_skyManager);
        GD.Print($"WorldManager: GlobalSkyManager added, inTree={_skyManager.IsInsideTree()}");

        _skyRoot = GetTree().Root.FindChild("Sky", recursive: true, owned: false) as Node3D;
        GD.Print($"WorldManager: Sky root found = {(_skyRoot != null)}");

        //Terrain
        _terrainManager = new TerrainManager();
        _terrainManager.Configure(settings.Terrain);
        AddChild(_terrainManager);
        var tcsTargetsReady = new TaskCompletionSource();
        //Wait for signal
        void OnTargetsReady()
        {
            tcsTargetsReady.TrySetResult();
        }
        _terrainManager.TargetsReady += OnTargetsReady;
        _terrainManager.Initialize(this);
        await tcsTargetsReady.Task;
        _terrainManager.TargetsReady -= OnTargetsReady;


        // camera
        var camera = new SimpleCamera();
        _mainCamera = camera;
        camera.Name = "MainCamera";
        camera.Configure(settings.Camera);

    // Resolve the target preset once so all related parameters come from a single place
        settings.Camera.TargetPresets.TryGetValue(settings.Camera.StartPresetId, out var tp);

    // Terrain bounds in world meters (heightmap space)
        var t = settings.Terrain;
        var size = t.GetValidatedRegionSize();

        var minX = 0f;
        var minZ = 0f;
        var maxX = (size - 1) * t.MetersPerPixel;
        var maxZ = (size - 1) * t.MetersPerPixel;


    // Use preset-specific edge margin (fallback if missing)
        var edgeMargin = tp?.EdgeMarginMeters ?? 10f;
        camera.SetWorldBounds(minX, maxX, minZ, maxZ, edgeMarginMeters: edgeMargin);

        AddChild(camera);

// Wait until spawn targets are actually available (not just a frame delay).
        
        camera.SetTargets(_terrainManager.SpawnTargets);
        camera.SetHeightSampler(_terrainManager.SampleTerrainHeightMeters);

        if (tp != null)
        {
            GD.Print($"[CameraDebug] Calling SpawnRandomFromPreset | seed={_terrainManager.WorldSeed}, startPreset={settings.Camera.StartPresetId}, tp={(tp != null)}");

            var ok = camera.SpawnRandomFromPreset(_terrainManager.WorldSeed, settings.Camera.StartPresetId, tp);

            GD.Print($"[CameraDebug] SpawnRandomFromPreset returned {ok}");

        }
        //take Screenshot
        await camera.TakeScreenshotAsync(
            exportTargetCoordinates: true,
            regionId: settings.Terrain.RegionId,
            seed: _terrainManager.WorldSeed
        );


    }
}