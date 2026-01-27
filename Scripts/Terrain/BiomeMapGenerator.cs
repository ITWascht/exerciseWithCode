using Godot;

namespace SzeneGenerator;

public static class BiomeMapGenerator
{
    // 0 = field, 1 = forest (minimal)
    public static int[,] Generate(int width, int depth, int seed)
    {
        var map = new int[width, depth];

        // super simpel: straight inie between bioms
        var split = width / 2;

        for (var x = 0; x < width; x++)
        for (var z = 0; z < depth; z++)
            map[x, z] = x < split ? 1 : 0;

        return map;
    }

    public static float[,] ComputeEdgeDistanceMeters(int[,] biome, float metersPerPixel, int maxRadiusPx = 32)
    {
        var w = biome.GetLength(0);
        var d = biome.GetLength(1);
        var dist = new float[w, d];

        for (var x = 0; x < w; x++)
        for (var z = 0; z < d; z++)
        {
            var b = biome[x, z];
            var best = float.PositiveInfinity;

            // check in square (x,z) for other Biome-ID
            for (var rx = -maxRadiusPx; rx <= maxRadiusPx; rx++)
            for (var rz = -maxRadiusPx; rz <= maxRadiusPx; rz++)
            {
                var xx = x + rx;
                var zz = z + rz;
                if (xx < 0 || zz < 0 || xx >= w || zz >= d) continue;
                if (biome[xx, zz] == b) continue;

                var dd = Mathf.Sqrt(rx * rx + rz * rz);
                if (dd < best) best = dd;
            }

            // wenn keine Grenze im Radius gefunden, setz “weit weg”
            if (float.IsPositiveInfinity(best))
                best = maxRadiusPx;

            dist[x, z] = best * metersPerPixel;
        }

        return dist;
    }

// NEW: Realistische Biome-Verteilung aus Height + Slope (+ Noise für organische Grenzen)
// 0 = field, 1 = forest
    public static int[,] GenerateFromHeightSlope(
        float[,] heights01,
        int width,
        int depth,
        float metersPerPixel,
        float scale,
        float offset,
        int seed,
        float forestLineMeters, // z.B. 40f: oberhalb wird Wald seltener/kein Wald
        float maxForestSlopeDeg, // z.B. 25f: oberhalb kein Wald
        float noiseStrength = 0.25f, // 0..1: wie “wellig”/organisch die Grenze wird
        float noiseScale = 0.02f // Frequenz der Noise
    )
    {
        var biome = new int[width, depth];

        var noise = new FastNoiseLite();
        noise.Seed = seed;
        noise.NoiseType = FastNoiseLite.NoiseTypeEnum.Simplex;
        noise.Frequency = noiseScale;

        for (var x = 0; x < width; x++)
        for (var z = 0; z < depth; z++)
        {
            var hM = heights01[x, z] * scale + offset;
            var slopeDeg = ComputeSlopeDeg(heights01, x, z, width, depth, metersPerPixel, scale, offset);

            // harte Ausschlüsse
            if (slopeDeg > maxForestSlopeDeg)
            {
                biome[x, z] = 0; // field
                continue;
            }

            // Noise verschiebt Schwelle organisch
            var n = noise.GetNoise2D(x, z); // -1..1
            var shiftedForestLine = forestLineMeters + n * noiseStrength * 20f; // 20m “Bandbreite”, anpassbar

            // Score: unterhalb eher Wald, oberhalb eher Feld
            // (weicher Übergang um forestLine herum)
            var heightT = 1f - Mathf.Clamp((hM - (shiftedForestLine - 10f)) / 20f, 0f, 1f); // 1..0 über ~20m
            var slopeT = 1f - Mathf.Clamp(slopeDeg / maxForestSlopeDeg, 0f, 1f); // 1..0 bis maxSlope

            var forestScore = 0.7f * heightT + 0.3f * slopeT;

            biome[x, z] = forestScore > 0.5f ? 1 : 0;
        }
        // Smooth small speckles: make biomes more contiguous.
        biome = SmoothMajority(biome, passes: 2); //passes higher means clearer seperation
        return biome;
    }

// NEW: Slope helper (kopiert aus deiner Terrain-Logik, aber lokal gehalten)
    private static float ComputeSlopeDeg(
        float[,] heights01,
        int x,
        int z,
        int width,
        int depth,
        float metersPerPixel,
        float scale,
        float offset)
    {
        var x0 = Mathf.Max(x - 1, 0);
        var x1 = Mathf.Min(x + 1, width - 1);
        var z0 = Mathf.Max(z - 1, 0);
        var z1 = Mathf.Min(z + 1, depth - 1);

        var hL = heights01[x0, z] * scale + offset;
        var hR = heights01[x1, z] * scale + offset;
        var hD = heights01[x, z0] * scale + offset;
        var hU = heights01[x, z1] * scale + offset;

        var dhdx = (hR - hL) / (2f * metersPerPixel);
        var dhdz = (hU - hD) / (2f * metersPerPixel);

        var slopeRad = Mathf.Atan(Mathf.Sqrt(dhdx * dhdx + dhdz * dhdz));
        return slopeRad * 57.29578f;
    }
    
    // Majority filter to remove tiny biome islands and create clearer region borders.
    private static int[,] SmoothMajority(int[,] src, int passes)
    {
        var w = src.GetLength(0);
        var d = src.GetLength(1);

        var a = src;
        var b = new int[w, d];

        for (int pass = 0; pass < passes; pass++)
        {
            for (int x = 0; x < w; x++)
            for (int z = 0; z < d; z++)
            {
                int count0 = 0, count1 = 0;

                for (int dx = -1; dx <= 1; dx++)
                for (int dz = -1; dz <= 1; dz++)
                {
                    int xx = x + dx;
                    int zz = z + dz;
                    if (xx < 0 || zz < 0 || xx >= w || zz >= d) continue;

                    if (a[xx, zz] == 1) count1++;
                    else count0++;
                }

                b[x, z] = (count1 > count0) ? 1 : 0;
            }

            // swap buffers
            var tmp = a;
            a = b;
            b = tmp;
        }

        return a;
    }
    
    // 0 = field, 1 = forest, 2 = road
    public static int[,] GenerateFieldForestWithRoad(
        int width,
        int depth,
        int seed,
        float metersPerPixel,
        float forestRatio = 0.5f,          // e.g. 0.65f => 65% forest, 35% field
        float roadWidthMeters = 6.0f,      // total road width in meters (e.g. 4..8)
        float roadWiggleMeters = 1.0f,     // 0 = perfectly straight, >0 = slightly organic
        float roadNoiseScale = 0.02f       // how fast the wiggle changes along Z
    )
    {
        var map = new int[width, depth];

        // Clamp ratio to safe range
        forestRatio = Mathf.Clamp(forestRatio, 0.05f, 0.95f);

        // Split position in pixels (forest on the left, field on the right)
        var splitX = Mathf.RoundToInt(width * forestRatio);

        // Road half width in pixels
        var halfRoadPx = Mathf.Max(1, Mathf.RoundToInt((roadWidthMeters * 0.5f) / metersPerPixel));

        // Optional wiggle for a more natural road line
        var noise = new FastNoiseLite();
        noise.Seed = seed;
        noise.NoiseType = FastNoiseLite.NoiseTypeEnum.Simplex;
        noise.Frequency = roadNoiseScale;

        var wigglePx = roadWiggleMeters / metersPerPixel;

        for (var z = 0; z < depth; z++)
        {
            // Boundary center may wiggle slightly along Z
            var offset = noise.GetNoise2D(0, z) * wigglePx; // -wigglePx..+wigglePx
            var boundaryX = splitX + offset;

            for (var x = 0; x < width; x++)
            {
                // Base biomes (big contiguous regions)
                map[x, z] = x < splitX ? 1 : 0;

                // Overlay road as a thin band around the boundary
                if (Mathf.Abs(x - boundaryX) <= halfRoadPx)
                    map[x, z] = 2;
            }
        }

        return map;
    }


}