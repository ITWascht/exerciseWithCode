using Godot;

namespace SzeneGenerator;

public partial class WorldManager: Node3D
{
	private TerrainManager _terrainManager;
	private GlobalSkyManager _skyManager;
	
	//configimport Worlsettings
	public override async void _Ready()
	{
		var settings = new WorldSettings();
		
		// Terrain generieren
		_terrainManager = new TerrainManager();
		_terrainManager.Configure(settings.Terrain);
		AddChild(_terrainManager);
		_terrainManager.Initialize(this);
		
		
		
		//Kamera generieren, SimpleCamera
		var camera = new SimpleCamera();
		camera.Name = "MainCamera";
		camera.Configure(settings.Camera);
		AddChild(camera);
		camera.SetTargets(_terrainManager.SpawnTargets);
		if (settings.Camera.TargetPresets.TryGetValue(settings.Camera.StartPresetId, out var tp))
			camera.FocusRandomTarget(seed: _terrainManager.WorldSeed, preset: tp);
		// check Terrain height for "free" view
		camera.SetHeightSampler(_terrainManager.SampleTerrainHeightMeters);
		
		// Load region rules (for LocalSkySettings)
		string regionId = settings.Terrain.RegionId;
		string rulesPath = $"res://assets/regions/{regionId}/{regionId}.rules.json";
		var rules = RegionRulesLoader.Load(rulesPath);
		
		_skyManager = new GlobalSkyManager();
		_skyManager.Configure(settings.Sky); // existing global config
		_skyManager.Configure(rules);        // NEW: region rules (LocalSkySettings)
		AddChild(_skyManager);
		
		//take Screenshot
		//await camera.TakeScreenshotAsync();
	}
}
