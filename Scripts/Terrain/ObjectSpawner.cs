using Godot;
using System;
using System.Collections.Generic;

namespace SzeneGenerator;

public partial class ObjectSpawner
{
	private readonly int _width;
	private readonly int _depth;
	private readonly float _metersPerPixel;
	private readonly float _scale;
	private readonly float _offset;

	public ObjectSpawner(int width, int depth, float metersPerPixel, float scale, float offset)
	{
		_width = width;
		_depth = depth;
		_metersPerPixel = metersPerPixel;
		_scale = scale;
		_offset = offset;
	}

    //Spawnlogic and return target position
	public List<Node3D> Spawn(
    Node parent,
    float[,] heights01,
    RegionRules rules,
    Dictionary<string, PackedScene> prefabMap,
    int seed,
    int[,] biome = null,
    float[,] edgeDist = null)
{
    GD.Print($"Spawner: metersPerPixel={_metersPerPixel}, width={_width}, depth={_depth}");
    
    var targets = new List<Node3D>();
    var spawning = rules?.Spawning;
    if (spawning?.Entries == null || spawning.Entries.Length == 0)
    {
        GD.Print("No spawning entries found");
        return targets;
    }

    // Spawn density base: area_km2 = (w*mpp * d*mpp)/1e6
    float areaM2 = (_width * _metersPerPixel) * (_depth * _metersPerPixel);
    float areaKm2 = areaM2 / 1_000_000f;

    // Simple random
    var rng = new Random(seed);

    // Global defaults (can be overridden per entry via JSON)
    float defaultJitterStrength = 1.0f;
    float defaultMinDistanceMeters = 1.5f;
    float defaultMaxTiltDeg = 50f;
    bool defaultAlignToSlope = true;

    // Spatial hash for min distance (shared across all entries)
    // We compute the smallest configured distance ONCE, so the grid stays consistent.
    float baseCellSize = Mathf.Max(0.01f, defaultMinDistanceMeters);

    // Find global minimum minDistance across all entries (if any)
    foreach (var e in spawning.Entries)
    {
        float d = e.MinDistanceMeters ?? defaultMinDistanceMeters;
        if (d > 0f)
            baseCellSize = Mathf.Min(baseCellSize, Mathf.Max(0.01f, d));
    }

    var grid = new Dictionary<long, List<Vector2>>();

    int totalSpawned = 0;

    foreach (var entry in spawning.Entries)
    {
        // Skip disabled entries
        if (entry.DensityPerKm2 <= 0 && entry.MinCount <= 0)
            continue;

        // Prefab check
        if (!prefabMap.TryGetValue(entry.AssetId, out var scene) || scene == null)
            continue;

        // Per-entry spawn parameters (optional overrides)
        float entryJitterStrength = entry.JitterStrength ?? defaultJitterStrength;
        float entryMinDistance = entry.MinDistanceMeters ?? defaultMinDistanceMeters;
        float entryMaxTiltDeg = entry.MaxTiltDeg ?? defaultMaxTiltDeg;
        bool entryAlignToSlope = entry.AlignToSlope ?? defaultAlignToSlope;

        int target = Mathf.RoundToInt(entry.DensityPerKm2 * areaKm2);

        // Minimum count (e.g. special objects)
        target = Mathf.Max(target, entry.MinCount);
        if (target <= 0)
            continue;

        int spawned = 0;
        int attempts = 0;
        int maxAttempts = target * 30; // buffer for filter losses

        bool enforceDistance = true;

        while (spawned < target && attempts < maxAttempts)
        {
            attempts++;

            int x = rng.Next(0, _width);
            int z = rng.Next(0, _depth);

            // World positioning with jitter (break grid look)
            float jitter = _metersPerPixel * 0.5f * entryJitterStrength;
            float worldX = (x * _metersPerPixel) + (float)(rng.NextDouble() * 2.0 - 1.0) * jitter;
            float worldZ = (z * _metersPerPixel) + (float)(rng.NextDouble() * 2.0 - 1.0) * jitter;

            // Height / slope at jittered position
            float fx = worldX / _metersPerPixel;
            float fz = worldZ / _metersPerPixel;

            float hM = SampleHeight01Bilinear(heights01, fx, fz) * _scale + _offset;

            int ix = Mathf.Clamp((int)Mathf.Round(fx), 0, _width - 1);
            int iz = Mathf.Clamp((int)Mathf.Round(fz), 0, _depth - 1);
            float slopeDeg = ComputeSlopeDeg(heights01, ix, iz);

            // Height / slope filters
            if (hM < entry.MinHeightMeters || hM > entry.MaxHeightMeters)
                continue;
            if (slopeDeg > entry.MaxSlopeDeg)
                continue;

            // Biome/edge rules are only active if JSON provides them
            bool wantsBiomeRules =
                (entry.AllowedBiomes != null && entry.AllowedBiomes.Length > 0) ||
                entry.EdgeMinMeters.HasValue ||
                entry.EdgeMaxMeters.HasValue;

            if (wantsBiomeRules)
            {
                // If rules require maps, but maps are missing -> reject this spawn position
                if (biome == null || edgeDist == null)
                    continue;

                int biomeId = biome[ix, iz];
                float distToEdge = edgeDist[ix, iz];

                // Allowed biomes
                if (entry.AllowedBiomes != null && entry.AllowedBiomes.Length > 0)
                {
                    bool ok = false;
                    for (int i = 0; i < entry.AllowedBiomes.Length; i++)
                    {
                        if (entry.AllowedBiomes[i] == biomeId) { ok = true; break; }
                    }
                    if (!ok) continue;
                }

                // Edge distance constraints
                if (entry.EdgeMinMeters.HasValue && distToEdge < entry.EdgeMinMeters.Value) continue;
                if (entry.EdgeMaxMeters.HasValue && distToEdge > entry.EdgeMaxMeters.Value) continue;
            }

            // Minimum distance (Poisson-disk light via spatial hash)
            var posXZ = new Vector2(worldX, worldZ);
            if (enforceDistance && entryMinDistance > 0f &&
                !PassesMinDistance(grid, baseCellSize, posXZ, entryMinDistance))
                continue;

            // Instantiate
            var instance = scene.Instantiate() as Node3D;
            if (instance == null)
                continue;

            instance.Position = new Vector3(worldX, hM, worldZ);

            // Align to slope (optional, with tilt limit)
            if (entryAlignToSlope)
            {
                Vector3 n = ComputeNormal(heights01, fx, fz);
                float tilt = Mathf.RadToDeg(Mathf.Acos(Mathf.Clamp(n.Dot(Vector3.Up), -1f, 1f)));
                if (tilt <= entryMaxTiltDeg)
                {
                    float yaw = (float)(rng.NextDouble() * Math.Tau);
                    Vector3 forward = new Vector3(Mathf.Cos(yaw), 0f, Mathf.Sin(yaw));
                    forward = (forward - n * forward.Dot(n)).Normalized();
                    Vector3 right = forward.Cross(n).Normalized();
                    forward = n.Cross(right).Normalized();
                    instance.Basis = new Basis(right, n, forward);
                }
            }

            parent.AddChild(instance);
            if (entry.IsTarget)
                targets.Add(instance);

            // Add to spatial hash (still useful even if fallback ignores distance)
            if (entryMinDistance > 0f)
                AddToGrid(grid, baseCellSize, posXZ);

            spawned++;
            totalSpawned++;
        }

        // Fallback: if MinCount not reached, ignore minimum distance (keeps height/slope/biome filters!)
        if (spawned < entry.MinCount)
        {
            int missing = entry.MinCount - spawned;
            int fallbackPlaced = 0;
            int fallbackAttempts = 0;
            int fallbackMaxAttempts = missing * 50;

            if (entry.IsTarget == true)
            {enforceDistance = true;}
            else {enforceDistance = false;}
            
            while (fallbackPlaced < missing && fallbackAttempts < fallbackMaxAttempts)
            {
                fallbackAttempts++;

                int x = rng.Next(0, _width);
                int z = rng.Next(0, _depth);

                float jitter = _metersPerPixel * 0.5f * entryJitterStrength;
                float worldX = (x * _metersPerPixel) + (float)(rng.NextDouble() * 2.0 - 1.0) * jitter;
                float worldZ = (z * _metersPerPixel) + (float)(rng.NextDouble() * 2.0 - 1.0) * jitter;

                float fx = worldX / _metersPerPixel;
                float fz = worldZ / _metersPerPixel;

                float hM = SampleHeight01Bilinear(heights01, fx, fz) * _scale + _offset;

                int ix = Mathf.Clamp((int)Mathf.Round(fx), 0, _width - 1);
                int iz = Mathf.Clamp((int)Mathf.Round(fz), 0, _depth - 1);
                float slopeDeg = ComputeSlopeDeg(heights01, ix, iz);

                if (hM < entry.MinHeightMeters || hM > entry.MaxHeightMeters) continue;
                if (slopeDeg > entry.MaxSlopeDeg) continue;

                // Biome/edge rules (same as main loop)
                bool wantsBiomeRules =
                    (entry.AllowedBiomes != null && entry.AllowedBiomes.Length > 0) ||
                    entry.EdgeMinMeters.HasValue ||
                    entry.EdgeMaxMeters.HasValue;

                if (wantsBiomeRules)
                {
                    if (biome == null || edgeDist == null)
                        continue;

                    int biomeId = biome[ix, iz];
                    float distToEdge = edgeDist[ix, iz];

                    if (entry.AllowedBiomes != null && entry.AllowedBiomes.Length > 0)
                    {
                        bool ok = false;
                        for (int i = 0; i < entry.AllowedBiomes.Length; i++)
                        {
                            if (entry.AllowedBiomes[i] == biomeId) { ok = true; break; }
                        }
                        if (!ok) continue;
                    }

                    if (entry.EdgeMinMeters.HasValue && distToEdge < entry.EdgeMinMeters.Value) continue;
                    if (entry.EdgeMaxMeters.HasValue && distToEdge > entry.EdgeMaxMeters.Value) continue;
                }

                // Minimum distance intentionally NOT checked in fallback
                var posXZ = new Vector2(worldX, worldZ);
                if (enforceDistance && entryMinDistance > 0f &&
                    !PassesMinDistance(grid, baseCellSize, posXZ, entryMinDistance))
                    continue;

                var instance = scene.Instantiate() as Node3D;
                if (instance == null) continue;

                instance.Position = new Vector3(worldX, hM, worldZ);

                if (entryAlignToSlope)
                {
                    Vector3 n = ComputeNormal(heights01, fx, fz);
                    float tilt = Mathf.RadToDeg(Mathf.Acos(Mathf.Clamp(n.Dot(Vector3.Up), -1f, 1f)));
                    if (tilt <= entryMaxTiltDeg)
                    {
                        float yaw = (float)(rng.NextDouble() * Math.Tau);
                        Vector3 forward = new Vector3(Mathf.Cos(yaw), 0f, Mathf.Sin(yaw));
                        forward = (forward - n * forward.Dot(n)).Normalized();
                        Vector3 right = forward.Cross(n).Normalized();
                        forward = n.Cross(right).Normalized();
                        instance.Basis = new Basis(right, n, forward);
                    }
                }

                parent.AddChild(instance);

            // Still add to grid (optional; helps later entries)
                if (entryMinDistance > 0f)
                    AddToGrid(grid, baseCellSize, posXZ);

                spawned++;
                totalSpawned++;
                fallbackPlaced++;
            }

            GD.Print($"{entry.AssetId} fallback placed {fallbackPlaced}/{missing} (attempts {fallbackAttempts})");
        }

        GD.Print($"{entry.AssetId} spawned {spawned}/{target} (attempts {attempts})");
    }

    GD.Print($"totalSpawned: {totalSpawned} spawned");
    return targets;
}


	private float ComputeSlopeDeg(float[,] heights01, int x, int z)
	{
		int x0 = Mathf.Max(x - 1, 0);
		int x1 = Mathf.Min(x + 1, _width - 1);
		int z0 = Mathf.Max(z - 1, 0);
		int z1 = Mathf.Min(z + 1, _depth - 1);

		float hL = heights01[x0, z] * _scale + _offset;
		float hR = heights01[x1, z] * _scale + _offset;
		float hD = heights01[x, z0] * _scale + _offset;
		float hU = heights01[x,z1] * _scale + _offset;

		float dhdx = (hR - hL) / (2f * _metersPerPixel);
		float dhdz = (hU - hD) / (2f * _metersPerPixel);
		
		float slopeRad = Mathf.Atan(Mathf.Sqrt(dhdx * dhdx + dhdz * dhdz));
		return slopeRad * 57.29578f;
	}

	private SpawnEntry PickWeightedEntry(
		SpawnEntry[] entries,
		float totalWeight,
		Random rng,
		float hM,
		float slopeDeg,
		Dictionary<string, PackedScene> prefabMap)
	{
		//check for matching, if not --> null
		for (int tries = 0; tries < 8; tries++)
		{
			float r = (float)(rng.NextDouble() * totalWeight);
			float acc = 0f;

			SpawnEntry chosen = null;
			foreach (var e in entries)
			{
				acc += Mathf.Max(0.001f, e.Weight);
				if (r <= acc)
				{ chosen = e; break; }
			}

			if (chosen == null) chosen = entries[^1];
			
			// Fiter
			if (hM < chosen.MinHeightMeters || hM > chosen.MaxHeightMeters)
				continue;
			if (slopeDeg > chosen.MaxSlopeDeg)
				continue;
			
			//Prefab?
			if (!prefabMap.ContainsKey(chosen.AssetId))
				continue;

			return chosen;
		}

		return null;
	}
	
	// Bilinear-Sampling for height01 at random (fx,fz) in Pixel-Coordinats
private float SampleHeight01Bilinear(float[,] h, float fx, float fz)
{
    fx = Mathf.Clamp(fx, 0, _width - 1);
    fz = Mathf.Clamp(fz, 0, _depth - 1);

    int x0 = (int)Mathf.Floor(fx);
    int z0 = (int)Mathf.Floor(fz);
    int x1 = Mathf.Min(x0 + 1, _width - 1);
    int z1 = Mathf.Min(z0 + 1, _depth - 1);

    float tx = fx - x0;
    float tz = fz - z0;

    float a = Mathf.Lerp(h[x0, z0], h[x1, z0], tx);
    float b = Mathf.Lerp(h[x0, z1], h[x1, z1], tx);
    return Mathf.Lerp(a, b, tz);
}

// Normalenvektor from heightmap-gradient (in worldscale)
private Vector3 ComputeNormal(float[,] heights01, float fx, float fz)
{
    float hL = SampleHeight01Bilinear(heights01, fx - 1f, fz) * _scale + _offset;
    float hR = SampleHeight01Bilinear(heights01, fx + 1f, fz) * _scale + _offset;
    float hD = SampleHeight01Bilinear(heights01, fx, fz - 1f) * _scale + _offset;
    float hU = SampleHeight01Bilinear(heights01, fx, fz + 1f) * _scale + _offset;

    float dhdx = (hR - hL) / (2f * _metersPerPixel);
    float dhdz = (hU - hD) / (2f * _metersPerPixel);

    // Surface: y = h(x,z) => Normal ~ (-dhdx, 1, -dhdz)
    return new Vector3(-dhdx, 1f, -dhdz).Normalized();
}

// Poisson-Disk light: Spatial Hash
private struct CellKey
{
    public int X;
    public int Z;
    public CellKey(int x, int z) { X = x; Z = z; }
}

private bool PassesMinDistance(
    Dictionary<long, List<Vector2>> grid,
    float cellSize,
    Vector2 posXZ,
    float minDist)
{
    int cx = (int)Mathf.Floor(posXZ.X / cellSize);
    int cz = (int)Mathf.Floor(posXZ.Y / cellSize);

    float minDist2 = minDist * minDist;

    for (int gx = cx - 1; gx <= cx + 1; gx++)
    for (int gz = cz - 1; gz <= cz + 1; gz++)
    {
        long key = (((long)gx) << 32) ^ (uint)gz;
        if (!grid.TryGetValue(key, out var list)) continue;

        for (int i = 0; i < list.Count; i++)
        {
            if (posXZ.DistanceSquaredTo(list[i]) < minDist2)
                return false;
        }
    }

    return true;
}

private void AddToGrid(Dictionary<long, List<Vector2>> grid, float cellSize, Vector2 posXZ)
{
    int cx = (int)Mathf.Floor(posXZ.X / cellSize);
    int cz = (int)Mathf.Floor(posXZ.Y / cellSize);
    long key = (((long)cx) << 32) ^ (uint)cz;

    if (!grid.TryGetValue(key, out var list))
    {
        list = new List<Vector2>();
        grid[key] = list;
    }
    list.Add(posXZ);
}

}
	


