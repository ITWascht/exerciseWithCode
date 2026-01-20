using Godot;
using System.Collections.Generic;

namespace SzeneGenerator;

public partial class TerrainManager : Node
{
	private int _worldBackground;
	// Heightmap-Parameter 
	private float _noiseScale = 0.5f; //structure, higher = rougher
	private float _heightFactor = 2.5f; //heightmultiplicator
	private float _terrainOffset = 0.0f; // map offset
	private Node3D _terrainNode;
	private GodotObject _terrainData;
	
	//Configimport from WorldSettings
	private TerrainSettings _cfg;
	
	//target for cameracontrol
	public List<Node3D> SpawnTargets { get; private set; } = new();
	public int WorldSeed => _cfg?.Seed ?? 0;
	
	//height parameters 
	private float[,] _heights01;
	private float _metersPerPixel, _scale;
	


	public void Initialize(Node parent)
	{
		//Seed configuration
		if (_cfg.Seed == 0) // 0 = "random"
			_cfg.Seed = System.Environment.TickCount;
		int worldSeed = _cfg.Seed; 
		GD.Print($"Seed: {worldSeed}");

		static int SubSeed(int seed, string salt, string region)
		{
			unchecked
			{
				int h = 17;
				h = h * 31 + seed;
				h = h * 31 + salt.GetHashCode();
				h = h * 31 + region.GetHashCode();
				return h;
			}
		}

		// Terrain3D instanziate and add
		_terrainNode = (Node3D)ClassDB.Instantiate("Terrain3D");
		parent.AddChild(_terrainNode);
		// Base-Settings
		_terrainNode.Name = "Map";
		_terrainNode.Position = Vector3.Zero;
		// Worldbackground (WorldBackground:  off = 0, flat=1, noise=2 (simuliertes Gebirge))
		var terrainMaterial = (GodotObject)_terrainNode.Get("material");
		terrainMaterial.Set("world_background", _cfg.WorldBackground);
		terrainMaterial.Call("update");
		// Region-Size (String-Workaround, no direkt c# support
		_terrainNode.Set("region_size", "SIZE_256");
		// Path Terrain3D-Plugin
		_terrainNode.Set("data_directory", "res://");
		// get Terrain-Data 
		_terrainData = (GodotObject)_terrainNode.Get("data");
		//GD.Print("Terrain data type: ", _terrainData.GetType());
		
		//load Region-Assets
		string regionId = _cfg.RegionId;
		// seed salting
		int heightSeed  = SubSeed(worldSeed, "height",  regionId);
		int biomeSeed   = SubSeed(worldSeed, "biome",   regionId);
		int objectSeed  = SubSeed(worldSeed, "objects", regionId);
		//debug
		GD.Print($"HeightSeed: {heightSeed}, BiomeSeed: {biomeSeed}, ObjectSeed: {objectSeed}");

		string assetPath = $"res://assets/regions/{regionId}/{regionId}_assets.tres";
		string rulesPath = $"res://assets/regions/{regionId}/{regionId}.rules.json";
		var assetSet = GD.Load<RegionAssetSet>(assetPath);
		var rules = RegionRulesLoader.Load(rulesPath);
		
		//Error catching
		if (assetSet == null)
		{
			GD.PushError($"RegionAssetSet not found: {assetPath}");
			return;
		}
		if (rules == null)
		{
			GD.PushError($"RegionRules not found: {rulesPath}");
			return;
		}
		float noiseScale = rules.Terrain.NoiseScale ?? _cfg.NoiseScale;
		float heightFactor = rules.Terrain.HeightFactor ?? _cfg.HeightFactor;
		_terrainOffset = rules.Terrain.OffSet ?? _cfg.Offset;
		GD.Print($"Terrain shape (region override): noiseScale={noiseScale}, heightFactor={heightFactor}");


		//Mapping LayerId --> SlotIndex bauen
		var layerToSlot = new Dictionary<string, int>();
		//Debug Textures
		// foreach (var kv in layerToSlot)
		// 	GD.Print($"Map: '{kv.Key}' -> slot {kv.Value}");
		// GD.Print($"DefaultLayer = '{rules.Terrain.DefaultLayer}'");
		
		foreach (var s in assetSet.TerrainSlots)
			layerToSlot[s.LayerId] = s.SlotIndex;
		
		// create and use Heightmap 
		var heightCreator = new HeightMapCreator(
			width: _cfg.Width,
			depth: _cfg.Depth,
			seed: heightSeed,
			noiseScale: noiseScale,
			heightFactor: heightFactor,
			offset: _terrainOffset,
			scale: _cfg.Scale,
			worldPos: Vector3.Zero,
			
			//Scale
			metersPerPixel: _cfg.MetersPerPixel,
			//Rules and textures
			terrainRules: rules.Terrain,
			layerToSlot: layerToSlot,
			biome: null
		);

		// Assets setzen (Texturen/Modelle kommen aus dem TerrainAssetManager)
		var assetsMgr = new TerrainAssetManager();
		assetsMgr.ApplyToTerrain(_terrainNode, assetSet);
		
		//Heightmap create and apply
		float[,] heights01 = heightCreator.GenerateAndApplyHeightMap(_terrainData);
		// set parameters for View check
		_heights01 = heights01;
		_metersPerPixel = _cfg.MetersPerPixel;
		_scale = _cfg.Scale;
		
		//Biom-Map
		int[,] biome = BiomeMapGenerator.GenerateFromHeightSlope(
			heights01,
			_cfg.Width,
			_cfg.Depth,
			_cfg.MetersPerPixel,
			_cfg.Scale,
			_terrainOffset,
			biomeSeed,
			forestLineMeters: 40f, //Forrest-Line
			maxForestSlopeDeg: 25f,
			noiseStrength: 0.25f,
			noiseScale: 0.02f
		);

		float[,] edgeDist = BiomeMapGenerator.ComputeEdgeDistanceMeters(biome, _cfg.MetersPerPixel);
		//apply biome-based control map
		heightCreator.ApplyBiomeTexturing(_terrainData, heights01, biome);
		
		//Object spawning
		var prefabMap = BuildPrefabMap(assetSet);
		List<Node3D> targets = SpawnObjects(parent, heights01, rules, prefabMap, biome, edgeDist, objectSeed,_terrainOffset);
		SpawnTargets = targets;
	}
	
	private static Dictionary<string, PackedScene> BuildPrefabMap(RegionAssetSet assetSet)
	{
		var map = new Dictionary<string, PackedScene>();
		if (assetSet?.FloraPrefabs == null)
			return map;
		foreach (var p in assetSet.FloraPrefabs)
		{
			if (p == null || string.IsNullOrEmpty(p.AssetId) || p.Scene == null)
				continue;
			map[p.AssetId] = p.Scene;
		}
		return map;
	}
	
	public void Configure(TerrainSettings cfg)
	{
		_cfg = cfg;
	}
	
	private List<Node3D> SpawnObjects(
		Node parent,
		float[,] heights01,
		RegionRules rules,
		Dictionary<string, PackedScene> prefabMap,
		int[,] biome,
		float[,] edgeDist,
		int objectSeed,
		float offset)
	{
		var spawner = new ObjectSpawner(
			_cfg.Width,
			_cfg.Depth,
			_cfg.MetersPerPixel,
			_cfg.Scale,
			offset
		);

		List<Node3D> targets = spawner.Spawn(
			parent,
			heights01,
			rules,
			prefabMap,
			objectSeed,
			biome,
			edgeDist
		);
		return targets ?? new List<Node3D>();
	}
	
	public float SampleTerrainHeightMeters(Vector3 worldPos)
	{
		if (_heights01 == null || _cfg == null) 
			return float.NegativeInfinity;

		// WorldPos (Meter) -> Heightmap-Index (Pixel)
		int x = Mathf.RoundToInt(worldPos.X / _metersPerPixel);
		int z = Mathf.RoundToInt(worldPos.Z / _metersPerPixel);

		x = Mathf.Clamp(x, 0, _cfg.Width - 1);
		z = Mathf.Clamp(z, 0, _cfg.Depth - 1);

		return _heights01[x, z] * _scale + _terrainOffset;
	}


}
