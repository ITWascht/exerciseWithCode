using Godot;
using System;

namespace SzeneGenerator;

public static class BiomeMapGenerator
{
    // 0 = field, 1 = forest (minimal)
    public static int[,] Generate(int width, int depth, int seed)
    {
        var map = new int[width, depth];

        // super simpel: straight inie between bioms
        int split = width / 2;

        for (int x = 0; x < width; x++)
        for (int z = 0; z < depth; z++)
            map[x, z] = (x < split) ? 1 : 0;

        return map;
    }
    
    public static float[,] ComputeEdgeDistanceMeters(int[,] biome, float metersPerPixel, int maxRadiusPx = 32)
    {
        int w = biome.GetLength(0);
        int d = biome.GetLength(1);
        var dist = new float[w, d];

        for (int x = 0; x < w; x++)
        for (int z = 0; z < d; z++)
        {
            int b = biome[x, z];
            float best = float.PositiveInfinity;

            // check in square (x,z) for other Biome-ID
            for (int rx = -maxRadiusPx; rx <= maxRadiusPx; rx++)
            for (int rz = -maxRadiusPx; rz <= maxRadiusPx; rz++)
            {
                int xx = x + rx;
                int zz = z + rz;
                if (xx < 0 || zz < 0 || xx >= w || zz >= d) continue;
                if (biome[xx, zz] == b) continue;

                float dd = Mathf.Sqrt(rx * rx + rz * rz);
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
    float forestLineMeters,      // z.B. 40f: oberhalb wird Wald seltener/kein Wald
    float maxForestSlopeDeg,     // z.B. 25f: oberhalb kein Wald
    float noiseStrength = 0.25f, // 0..1: wie “wellig”/organisch die Grenze wird
    float noiseScale = 0.02f     // Frequenz der Noise
)
{
    var biome = new int[width, depth];

    var noise = new FastNoiseLite();
    noise.Seed = seed;
    noise.NoiseType = FastNoiseLite.NoiseTypeEnum.Simplex;
    noise.Frequency = noiseScale;

    for (int x = 0; x < width; x++)
    for (int z = 0; z < depth; z++)
    {
        float hM = heights01[x, z] * scale + offset;
        float slopeDeg = ComputeSlopeDeg(heights01, x, z, width, depth, metersPerPixel, scale, offset);

        // harte Ausschlüsse
        if (slopeDeg > maxForestSlopeDeg)
        {
            biome[x, z] = 0; // field
            continue;
        }

        // Noise verschiebt Schwelle organisch
        float n = noise.GetNoise2D(x, z); // -1..1
        float shiftedForestLine = forestLineMeters + (n * noiseStrength * 20f); // 20m “Bandbreite”, anpassbar

        // Score: unterhalb eher Wald, oberhalb eher Feld
        // (weicher Übergang um forestLine herum)
        float heightT = 1f - Mathf.Clamp((hM - (shiftedForestLine - 10f)) / 20f, 0f, 1f); // 1..0 über ~20m
        float slopeT  = 1f - Mathf.Clamp(slopeDeg / maxForestSlopeDeg, 0f, 1f);            // 1..0 bis maxSlope

        float forestScore = 0.7f * heightT + 0.3f * slopeT;

        biome[x, z] = (forestScore > 0.5f) ? 1 : 0;
    }

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
    int x0 = Mathf.Max(x - 1, 0);
    int x1 = Mathf.Min(x + 1, width - 1);
    int z0 = Mathf.Max(z - 1, 0);
    int z1 = Mathf.Min(z + 1, depth - 1);

    float hL = heights01[x0, z] * scale + offset;
    float hR = heights01[x1, z] * scale + offset;
    float hD = heights01[x, z0] * scale + offset;
    float hU = heights01[x, z1] * scale + offset;

    float dhdx = (hR - hL) / (2f * metersPerPixel);
    float dhdz = (hU - hD) / (2f * metersPerPixel);

    float slopeRad = Mathf.Atan(Mathf.Sqrt(dhdx * dhdx + dhdz * dhdz));
    return slopeRad * 57.29578f;
}

}