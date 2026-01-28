﻿using System.Collections.Generic;
using Godot;
using Environment = System.Environment;

namespace SzeneGenerator;
/// <summary>
/// Generates terrain geometry, applies heightmaps and biome texturing,
/// and spawns environment objects based on region configuration.
/// Provides terrain height sampling and target spawn data for camera placement.
/// </summary>

public partial class TerrainManager : Node
{
    // Config import from WorldSettings
    private TerrainSettings _cfg;
    private float _heightFactor = 2.5f; // height multiplier (legacy/unused)

    // Height parameters
    private float[,] _heights01;

    private float _metersPerPixel, _scale;

    // Heightmap parameters (legacy/unused)
    private float _noiseScale = 0.5f; // structure, higher = rougher

    private GodotObject _terrainData;
    private Node3D _terrainNode;
    private float _terrainOffset; // map offset
    private int _worldBackground;

    // Targets for camera control
    public List<Node3D> SpawnTargets { get; private set; } = new();
    public int WorldSeed => _cfg?.Seed ?? 0;
    
    //Signal for Cameraspawning
    [Signal]
    public delegate void TargetsReadyEventHandler();

    /// <summary>
    /// Builds terrain data, applies assets and biome rules, and prepares spawn targets.
    /// Emits the TargetsReady signal when object spawning is completed.
    /// </summary>
    public void Initialize(Node parent)
    {
        // Seed configuration
        if (_cfg.Seed == 0) // 0 = "random"
            _cfg.Seed = Environment.TickCount;

        var worldSeed = _cfg.Seed;
        GD.Print($"Seed: {worldSeed}");

        static int SubSeed(int seed, string salt, string region)
        {
            unchecked
            {
                var h = 17;
                h = h * 31 + seed;
                h = h * 31 + salt.GetHashCode();
                h = h * 31 + region.GetHashCode();
                return h;
            }
        }

        // Validate region size once and reuse everywhere
        var size = _cfg.GetValidatedRegionSize();

        // Instantiate Terrain3D and add to parent
        _terrainNode = (Node3D)ClassDB.Instantiate("Terrain3D");
        parent.AddChild(_terrainNode);

        // Base settings
        _terrainNode.Name = "Map";
        _terrainNode.Position = Vector3.Zero;

        // World background (off = 0, flat = 1, noise = 2)
        var terrainMaterial = (GodotObject)_terrainNode.Get("material");
        terrainMaterial.Set("world_background", _cfg.WorldBackground);
        terrainMaterial.Call("update");

        // Region size (string workaround required by Terrain3D API)
        _terrainNode.Set("region_size", $"SIZE_{size}");

        // Path Terrain3D plugin
        _terrainNode.Set("data_directory", "res://");

        // Get Terrain data
        _terrainData = (GodotObject)_terrainNode.Get("data");

        // Load region assets
        var regionId = _cfg.RegionId;

        // Seed salting
        var heightSeed = SubSeed(worldSeed, "height", regionId);
        var biomeSeed = SubSeed(worldSeed, "biome", regionId);
        var objectSeed = SubSeed(worldSeed, "objects", regionId);

        GD.Print($"HeightSeed: {heightSeed}, BiomeSeed: {biomeSeed}, ObjectSeed: {objectSeed}");

        var assetPath = $"res://assets/regions/{regionId}/{regionId}_assets.tres";
        var rulesPath = $"res://assets/regions/{regionId}/{regionId}.rules.json";
        var assetSet = GD.Load<RegionAssetSet>(assetPath);
        var rules = RegionRulesLoader.Load(rulesPath);

        // Error catching
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

        var noiseScale = rules.Terrain.NoiseScale ?? _cfg.NoiseScale;
        var heightFactor = rules.Terrain.HeightFactor ?? _cfg.HeightFactor;
        _terrainOffset = rules.Terrain.OffSet ?? _cfg.Offset;

        GD.Print($"Terrain shape (region override): noiseScale={noiseScale}, heightFactor={heightFactor}");

        // Build mapping LayerId -> SlotIndex
        var layerToSlot = new Dictionary<string, int>();
        foreach (var s in assetSet.TerrainSlots)
            layerToSlot[s.LayerId] = s.SlotIndex;

        // Create and use heightmap
        var heightCreator = new HeightMapCreator(
            size,
            size,
            heightSeed,
            noiseScale,
            heightFactor,
            _terrainOffset,
            _cfg.Scale,
            Vector3.Zero,

            // Scale
            _cfg.MetersPerPixel,

            // Rules and textures
            rules.Terrain,
            layerToSlot
        );

        // Apply assets (textures/models) from TerrainAssetManager
        var assetsMgr = new TerrainAssetManager();
        assetsMgr.ApplyToTerrain(_terrainNode, assetSet);

        // Generate and apply heightmap
        var heights01 = heightCreator.GenerateAndApplyHeightMap(_terrainData);

        // Store parameters for height sampling
        _heights01 = heights01;
        _metersPerPixel = _cfg.MetersPerPixel;
        _scale = _cfg.Scale;

        // Biome map (optional 3-biome layout: field/road/forest)
        var useRoadBiome = false;
        if (rules.Terrain?.BiomeOverrides != null)
        {
            foreach (var bo in rules.Terrain.BiomeOverrides)
            {
                if (bo != null && bo.BiomeId == 2)
                {
                    useRoadBiome = true;
                    break;
                }
            }
        }

        int[,] biome;
        if (useRoadBiome)
        {
            // 0 = field, 1 = forest, 2 = road
            biome = BiomeMapGenerator.GenerateFieldForestWithRoad(
                size,
                size,
                biomeSeed,
                _cfg.MetersPerPixel,
                forestRatio: 0.65f,
                roadWidthMeters: 6.0f,
                roadWiggleMeters: 0.8f
            );
        }
        else
        {
            // Default behavior for existing regions
            biome = BiomeMapGenerator.Generate(size, size, biomeSeed);
        }
        
        // Debugging: count biome distribution (0=field, 1=forest, 2=road)
        int cField = 0, cForest = 0, cRoad = 0, cOther = 0;

        for (int x = 0; x < size; x++)
        for (int z = 0; z < size; z++)
        {
            var b = biome[x, z];
            if (b == 0) cField++;
            else if (b == 1) cForest++;
            else if (b == 2) cRoad++;
            else cOther++;
        }

        GD.Print($"Biome pixels: field(0)={cField}, forest(1)={cForest}, road(2)={cRoad}, other={cOther}");



        var edgeDist = BiomeMapGenerator.ComputeEdgeDistanceMeters(biome, _cfg.MetersPerPixel);

        // Apply biome-based control map
        heightCreator.ApplyBiomeTexturing(_terrainData, heights01, biome, edgeDist);

        // Object spawning
        var prefabMap = BuildPrefabMap(assetSet);
        var targets = SpawnObjects(parent, heights01, rules, prefabMap, biome, edgeDist, objectSeed, _terrainOffset);

        SpawnTargets.Clear();
        if (targets != null)
            SpawnTargets.AddRange(targets);
        GD.Print($"TerrainManager: SpawnTargets now contains {SpawnTargets.Count} entries");
        
        //Emit Signal
        EmitSignal(SignalName.TargetsReady);
        
        // Export target coordinates (optional)
        if (_cfg.ExportTargetCoordinates)
        {
            TargetCoordinateExporter.ExportTargetsToJson(
                SpawnTargets,
                _cfg.TargetCoordinatesPath,
                _cfg.RegionId,
                _cfg.Seed
            );
        }

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
        var size = _cfg.GetValidatedRegionSize();

        var spawner = new ObjectSpawner(
            size,
            size,
            _cfg.MetersPerPixel,
            _cfg.Scale,
            offset
        );

        var targets = spawner.Spawn(
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

        var size = _cfg.GetValidatedRegionSize();

        // Convert world position (meters) to heightmap indices (pixels)
        var fx = worldPos.X / _metersPerPixel;
        var fz = worldPos.Z / _metersPerPixel;

        // If outside the heightmap bounds, return -Infinity so callers can reject the position
        if (fx < 0 || fz < 0 || fx > (size - 1) || fz > (size - 1))
            return float.NegativeInfinity;

        var x = Mathf.RoundToInt(fx);
        var z = Mathf.RoundToInt(fz);

        // Safe clamp for numerical edge cases
        x = Mathf.Clamp(x, 0, size - 1);
        z = Mathf.Clamp(z, 0, size - 1);

        return _heights01[x, z] * _scale + _terrainOffset;
    }
}
