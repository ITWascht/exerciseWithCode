using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Godot;

namespace SzeneGenerator;

public class ObjectSpawner
{
    private readonly int _depth;
    private readonly float _metersPerPixel;
    private readonly float _offset;
    private readonly float _scale;
    private readonly int _width;

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
    //GD.Print($"Spawner: metersPerPixel={_metersPerPixel}, width={_width}, depth={_depth}");

    var targets = new List<Node3D>();
    var spawning = rules?.Spawning;
    if (spawning?.Entries == null || spawning.Entries.Length == 0)
    {
        GD.Print("No spawning entries found");
        return targets;
    }

    // Spawn density base: area_km2 = (w*mpp * d*mpp)/1e6
    var areaM2 = _width * _metersPerPixel * (_depth * _metersPerPixel);
    var areaKm2 = areaM2 / 1_000_000f;

    // Simple random
    var rng = new Random(seed);

    // Global defaults (can be overridden per entry via JSON)
    var defaultJitterStrength = 1.0f;
    var defaultMinDistanceMeters = 1.5f;
    var defaultMaxTiltDeg = 50f;
    var defaultAlignToSlope = true;

    // Spatial hash for min distance (shared across all entries)
    // compute the smallest configured distance ONCE, so the grid stays consistent.
    var baseCellSize = Mathf.Max(0.01f, defaultMinDistanceMeters);
    
    // Minimum Margin for Targets to Map Edge
    var defaultTargetEdgeMarginMeters = 10.0f; // Keep targets away from map borders.

    // Find global minimum minDistance across all entries (if any)
    foreach (var e in spawning.Entries)
    {
        var d = e.MinDistanceMeters ?? defaultMinDistanceMeters;
        if (d > 0f)
            baseCellSize = Mathf.Min(baseCellSize, Mathf.Max(0.01f, d));
    }

    var grid = new Dictionary<long, List<Vector2>>();
    
    // MArginCheck for Targets
    bool IsInsideMapWithMargin(float worldX, float worldZ, float marginMeters)
    {
        var minX = marginMeters;
        var maxX = (_width - 1) * _metersPerPixel - marginMeters;
        var minZ = marginMeters;
        var maxZ = (_depth - 1) * _metersPerPixel - marginMeters;

        return worldX >= minX && worldX <= maxX && worldZ >= minZ && worldZ <= maxZ;
    }


    var totalSpawned = 0;

    // Two-pass spawn: targets first, then everything else.
    // This keeps the existing logic intact and avoids LINQ.
    for (int pass = 0; pass < 2; pass++)
    {
        var spawnTargets = (pass == 0);

        foreach (var entry in spawning.Entries)
        {
            if (entry.IsTarget != spawnTargets)
                continue;

            // Skip disabled entries
            if (entry.DensityPerKm2 <= 0 && entry.MinCount <= 0)
                continue;

            // Prefab check
            if (!prefabMap.TryGetValue(entry.AssetId, out var scene) || scene == null)
                continue;

            // Per-entry spawn parameters (optional overrides)
            var entryJitterStrength = entry.JitterStrength ?? defaultJitterStrength;
            var entryMinDistance = entry.MinDistanceMeters ?? defaultMinDistanceMeters;
            var entryMaxTiltDeg = entry.MaxTiltDeg ?? defaultMaxTiltDeg;
            var entryAlignToSlope = entry.AlignToSlope ?? defaultAlignToSlope;

            var target = Mathf.RoundToInt(entry.DensityPerKm2 * areaKm2);

            // Minimum count (e.g. special objects)
            target = Mathf.Max(target, entry.MinCount);
            if (target <= 0)
                continue;

            var spawned = 0;
            var attempts = 0;
            var maxAttempts = target * 30; // buffer for filter losses

            var enforceDistance = true;

            while (spawned < target && attempts < maxAttempts)
            {
                attempts++;

                var x = rng.Next(0, _width);
                var z = rng.Next(0, _depth);

                // World positioning with jitter (break grid look)
                var jitter = _metersPerPixel * 0.5f * entryJitterStrength;
                var worldX = x * _metersPerPixel + (float)(rng.NextDouble() * 2.0 - 1.0) * jitter;
                var worldZ = z * _metersPerPixel + (float)(rng.NextDouble() * 2.0 - 1.0) * jitter;
                
                // Keep targets away from map borders.
                if (entry.IsTarget && !IsInsideMapWithMargin(worldX, worldZ, defaultTargetEdgeMarginMeters))
                    continue;

                // Height / slope at jittered position
                var fx = worldX / _metersPerPixel;
                var fz = worldZ / _metersPerPixel;

                var hM = SampleHeight01Bilinear(heights01, fx, fz) * _scale + _offset;

                var ix = Mathf.Clamp((int)Mathf.Round(fx), 0, _width - 1);
                var iz = Mathf.Clamp((int)Mathf.Round(fz), 0, _depth - 1);
                var slopeDeg = ComputeSlopeDeg(heights01, ix, iz);

                // Height / slope filters
                if (hM < entry.MinHeightMeters || hM > entry.MaxHeightMeters)
                    continue;
                if (slopeDeg > entry.MaxSlopeDeg)
                    continue;

                // Biome/edge rules are only active if JSON provides them
                var wantsBiomeRules =
                    (entry.AllowedBiomes != null && entry.AllowedBiomes.Length > 0) ||
                    entry.EdgeMinMeters.HasValue ||
                    entry.EdgeMaxMeters.HasValue;

                if (wantsBiomeRules)
                {
                    // If rules require maps, but maps are missing -> reject this spawn position
                    if (biome == null || edgeDist == null)
                        continue;

                    var biomeId = biome[ix, iz];
                    var distToEdge = edgeDist[ix, iz];

                    // Allowed biomes
                    if (entry.AllowedBiomes != null && entry.AllowedBiomes.Length > 0)
                    {
                        var ok = false;
                        for (var i = 0; i < entry.AllowedBiomes.Length; i++)
                            if (entry.AllowedBiomes[i] == biomeId)
                            {
                                ok = true;
                                break;
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
                    var n = ComputeNormal(heights01, fx, fz);
                    var tilt = Mathf.RadToDeg(Mathf.Acos(Mathf.Clamp(n.Dot(Vector3.Up), -1f, 1f)));
                    if (tilt <= entryMaxTiltDeg)
                    {
                        var yaw = (float)(rng.NextDouble() * Math.Tau);
                        var forward = new Vector3(Mathf.Cos(yaw), 0f, Mathf.Sin(yaw));
                        forward = (forward - n * forward.Dot(n)).Normalized();
                        var right = forward.Cross(n).Normalized();
                        forward = n.Cross(right).Normalized();
                        instance.Basis = new Basis(right, n, forward);
                    }
                }
                // Physics-based overlap check (prevents spawning inside other colliders / objects).
                if (!IsPlacementFree(parent, instance, extraMarginMeters: 0.10f))
                {
                    instance.QueueFree();
                    continue;
                }

                parent.AddChild(instance);
                if (entry.IsTarget)
                {
                    // Store identifying info so exports can include the spawned vehicle type
                    instance.SetMeta("asset_id", entry.AssetId);

                    targets.Add(instance);
                }

                // Add to spatial hash (still useful even if fallback ignores distance)
                if (entryMinDistance > 0f)
                    AddToGrid(grid, baseCellSize, posXZ);

                spawned++;
                totalSpawned++;
            }

            // Fallback: if MinCount not reached, ignore minimum distance (keeps height/slope/biome filters!)
            if (spawned < entry.MinCount)
            {
                var missing = entry.MinCount - spawned;
                var fallbackPlaced = 0;
                var fallbackAttempts = 0;
                var fallbackMaxAttempts = missing * 50;

                if (entry.IsTarget)
                    enforceDistance = true;
                else
                    enforceDistance = false;

                while (fallbackPlaced < missing && fallbackAttempts < fallbackMaxAttempts)
                {
                    fallbackAttempts++;

                    var x = rng.Next(0, _width);
                    var z = rng.Next(0, _depth);

                    var jitter = _metersPerPixel * 0.5f * entryJitterStrength;
                    var worldX = x * _metersPerPixel + (float)(rng.NextDouble() * 2.0 - 1.0) * jitter;
                    var worldZ = z * _metersPerPixel + (float)(rng.NextDouble() * 2.0 - 1.0) * jitter;

                    var fx = worldX / _metersPerPixel;
                    var fz = worldZ / _metersPerPixel;

                    var hM = SampleHeight01Bilinear(heights01, fx, fz) * _scale + _offset;

                    var ix = Mathf.Clamp((int)Mathf.Round(fx), 0, _width - 1);
                    var iz = Mathf.Clamp((int)Mathf.Round(fz), 0, _depth - 1);
                    var slopeDeg = ComputeSlopeDeg(heights01, ix, iz);

                    if (hM < entry.MinHeightMeters || hM > entry.MaxHeightMeters) continue;
                    if (slopeDeg > entry.MaxSlopeDeg) continue;

                    // Biome/edge rules (same as main loop)
                    var wantsBiomeRules =
                        (entry.AllowedBiomes != null && entry.AllowedBiomes.Length > 0) ||
                        entry.EdgeMinMeters.HasValue ||
                        entry.EdgeMaxMeters.HasValue;

                    if (wantsBiomeRules)
                    {
                        if (biome == null || edgeDist == null)
                            continue;

                        var biomeId = biome[ix, iz];
                        var distToEdge = edgeDist[ix, iz];

                        if (entry.AllowedBiomes != null && entry.AllowedBiomes.Length > 0)
                        {
                            var ok = false;
                            for (var i = 0; i < entry.AllowedBiomes.Length; i++)
                                if (entry.AllowedBiomes[i] == biomeId)
                                {
                                    ok = true;
                                    break;
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
                        var n = ComputeNormal(heights01, fx, fz);
                        var tilt = Mathf.RadToDeg(Mathf.Acos(Mathf.Clamp(n.Dot(Vector3.Up), -1f, 1f)));
                        if (tilt <= entryMaxTiltDeg)
                        {
                            var yaw = (float)(rng.NextDouble() * Math.Tau);
                            var forward = new Vector3(Mathf.Cos(yaw), 0f, Mathf.Sin(yaw));
                            forward = (forward - n * forward.Dot(n)).Normalized();
                            var right = forward.Cross(n).Normalized();
                            forward = n.Cross(right).Normalized();
                            instance.Basis = new Basis(right, n, forward);
                        }
                    }
                    // Physics-based overlap check (prevents spawning inside other colliders / objects).
                    if (!IsPlacementFree(parent, instance, extraMarginMeters: 0.10f))
                    {
                        instance.QueueFree();
                        continue;
                    }
                    parent.AddChild(instance);
                    if (entry.IsTarget)
                    {
                        // Store identifying info so exports can include the spawned vehicle type
                        instance.SetMeta("asset_id", entry.AssetId);

                        targets.Add(instance);
                    }

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
    }

    GD.Print($"totalSpawned: {totalSpawned} spawned");
    return targets;
}


    private float ComputeSlopeDeg(float[,] heights01, int x, int z)
    {
        var x0 = Mathf.Max(x - 1, 0);
        var x1 = Mathf.Min(x + 1, _width - 1);
        var z0 = Mathf.Max(z - 1, 0);
        var z1 = Mathf.Min(z + 1, _depth - 1);

        var hL = heights01[x0, z] * _scale + _offset;
        var hR = heights01[x1, z] * _scale + _offset;
        var hD = heights01[x, z0] * _scale + _offset;
        var hU = heights01[x, z1] * _scale + _offset;

        var dhdx = (hR - hL) / (2f * _metersPerPixel);
        var dhdz = (hU - hD) / (2f * _metersPerPixel);

        var slopeRad = Mathf.Atan(Mathf.Sqrt(dhdx * dhdx + dhdz * dhdz));
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
        for (var tries = 0; tries < 8; tries++)
        {
            var r = (float)(rng.NextDouble() * totalWeight);
            var acc = 0f;

            SpawnEntry chosen = null;
            foreach (var e in entries)
            {
                acc += Mathf.Max(0.001f, e.Weight);
                if (r <= acc)
                {
                    chosen = e;
                    break;
                }
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

        var x0 = (int)Mathf.Floor(fx);
        var z0 = (int)Mathf.Floor(fz);
        var x1 = Mathf.Min(x0 + 1, _width - 1);
        var z1 = Mathf.Min(z0 + 1, _depth - 1);

        var tx = fx - x0;
        var tz = fz - z0;

        var a = Mathf.Lerp(h[x0, z0], h[x1, z0], tx);
        var b = Mathf.Lerp(h[x0, z1], h[x1, z1], tx);
        return Mathf.Lerp(a, b, tz);
    }

// Normalenvektor from heightmap-gradient (in worldscale)
    private Vector3 ComputeNormal(float[,] heights01, float fx, float fz)
    {
        var hL = SampleHeight01Bilinear(heights01, fx - 1f, fz) * _scale + _offset;
        var hR = SampleHeight01Bilinear(heights01, fx + 1f, fz) * _scale + _offset;
        var hD = SampleHeight01Bilinear(heights01, fx, fz - 1f) * _scale + _offset;
        var hU = SampleHeight01Bilinear(heights01, fx, fz + 1f) * _scale + _offset;

        var dhdx = (hR - hL) / (2f * _metersPerPixel);
        var dhdz = (hU - hD) / (2f * _metersPerPixel);

        // Surface: y = h(x,z) => Normal ~ (-dhdx, 1, -dhdz)
        return new Vector3(-dhdx, 1f, -dhdz).Normalized();
    }

    private bool PassesMinDistance(
        Dictionary<long, List<Vector2>> grid,
        float cellSize,
        Vector2 posXZ,
        float minDist)
    {
        var cx = (int)Mathf.Floor(posXZ.X / cellSize);
        var cz = (int)Mathf.Floor(posXZ.Y / cellSize);

        var minDist2 = minDist * minDist;

        for (var gx = cx - 1; gx <= cx + 1; gx++)
        for (var gz = cz - 1; gz <= cz + 1; gz++)
        {
            var key = ((long)gx << 32) ^ (uint)gz;
            if (!grid.TryGetValue(key, out var list)) continue;

            for (var i = 0; i < list.Count; i++)
                if (posXZ.DistanceSquaredTo(list[i]) < minDist2)
                    return false;
        }

        return true;
    }

    private void AddToGrid(Dictionary<long, List<Vector2>> grid, float cellSize, Vector2 posXZ)
    {
        var cx = (int)Mathf.Floor(posXZ.X / cellSize);
        var cz = (int)Mathf.Floor(posXZ.Y / cellSize);
        var key = ((long)cx << 32) ^ (uint)cz;

        if (!grid.TryGetValue(key, out var list))
        {
            list = new List<Vector2>();
            grid[key] = list;
        }

        list.Add(posXZ);
    }

// Poisson-Disk light: Spatial Hash
    private struct CellKey
    {
        public int X;
        public int Z;

        public CellKey(int x, int z)
        {
            X = x;
            Z = z;
        }
    }
    
    // Checks if the placement area is free using physics (and falls back to AABB box if needed).
private bool IsPlacementFree(Node parent, Node3D instance, float extraMarginMeters = 0.05f)
{
    var world3D = instance.GetWorld3D();
    if (world3D == null)
        return true; // If no world exists yet, do not block spawning.

    var spaceState = world3D.DirectSpaceState;

    // Build an approximate shape from the instance bounds (AABB). This works even if the prefab has no colliders.
    if (!TryGetMergedLocalAabb(instance, out var localAabb))
        return true; // No geometry found -> don't block.

    var box = new BoxShape3D();

    // AABB size -> box extents (half size). Inflate slightly with margin to avoid near-touching overlaps.
    var half = localAabb.Size * 0.5f;
    half += new Vector3(extraMarginMeters, extraMarginMeters, extraMarginMeters);
    box.Size = half * 2f;

    // Place the query box at the AABB center in the instance local space, then into world space.
    var centerLocal = localAabb.Position + localAabb.Size * 0.5f;
    var centerWorld = instance.GlobalTransform * centerLocal;

    var queryXform = instance.GlobalTransform;
    queryXform.Origin = centerWorld;
    
    // Exclude the instance itself (and any collision children) from the query to avoid self-hits.
    var exclude = new Godot.Collections.Array<Rid>();
    CollectCollisionRids(instance, exclude);

    var query = new PhysicsShapeQueryParameters3D
    {
        Shape = box,
        Transform = queryXform,
        // Optional: set collision mask if you want to only test against certain layers.
        // CollisionMask = 0xFFFFFFFF,
        Exclude = exclude,
        Margin = 0.0f
    };

    // If anything overlaps, reject.
    var hits = spaceState.IntersectShape(query, maxResults: 1);
    return hits.Count == 0;
}

// Merges AABBs of all MeshInstance3D children into one local-space AABB.
    private bool TryGetMergedLocalAabb(Node3D root, out Aabb merged)
    {
        var found = false;
        var mergedLocal = default(Aabb);

        void Visit(Node n)
        {
            if (n is MeshInstance3D mi && mi.Mesh != null)
            {
                var a = mi.GetAabb();

                // Transform the mesh-local AABB into root-local space.
                var xform = mi.Transform;

                var pts = new Vector3[8];
                var p = a.Position;
                var s = a.Size;

                pts[0] = p;
                pts[1] = p + new Vector3(s.X, 0, 0);
                pts[2] = p + new Vector3(0, s.Y, 0);
                pts[3] = p + new Vector3(0, 0, s.Z);
                pts[4] = p + new Vector3(s.X, s.Y, 0);
                pts[5] = p + new Vector3(s.X, 0, s.Z);
                pts[6] = p + new Vector3(0, s.Y, s.Z);
                pts[7] = p + s;

                for (int i = 0; i < 8; i++)
                    pts[i] = xform * pts[i];

                var ta = new Aabb(pts[0], Vector3.Zero);
                for (int i = 1; i < 8; i++)
                    ta = ta.Expand(pts[i]);

                mergedLocal = found ? mergedLocal.Merge(ta) : ta;
                found = true;
            }

            foreach (var c in n.GetChildren())
                Visit((Node)c);
        }

        Visit(root);

        merged = mergedLocal;
        return found;
    }
    
    // Collects RID(s) of all CollisionObject3D nodes in the instance tree.
// This is used to exclude self-collisions in physics queries.
    private void CollectCollisionRids(Node root, Godot.Collections.Array<Rid> outExclude)
    {
        if (root is CollisionObject3D co)
            outExclude.Add(co.GetRid());

        foreach (var c in root.GetChildren())
            CollectCollisionRids((Node)c, outExclude);
    }



}