using Godot;

namespace SzeneGenerator;

public class SimpleNoiseGenerator
{
    private readonly FastNoiseLite _noise;

    public SimpleNoiseGenerator(int seed = 0, float frequency = 0.01f)
    {
        _noise = new FastNoiseLite();
        _noise.Seed = seed;
        _noise.Frequency = frequency;
        _noise.NoiseType = FastNoiseLite.NoiseTypeEnum.Perlin;
        _noise.FractalType = FastNoiseLite.FractalTypeEnum.Fbm;
        _noise.FractalOctaves = 4;
    }

    public float[,] GenerateHeightMap(int width, int depth, float noiseScale = 1.0f, float heightFactor = 1.0f)
    {
        var heights = new float[width, depth];
        for (var x = 0; x < width; x++)
        for (var z = 0; z < depth; z++)
        {
            //Noise Koordinaten skalieren
            var nx = x * noiseScale;
            var nz = z * noiseScale;

            //FastNoiseLite liefert Werte zwischen -1 und 1
            var noiseValue = _noise.GetNoise2D(nx, nz);

            // in 0..1 mappen
            var normalized = (noiseValue + 1f) * 0.5f;

            //Höhenfaktor
            heights[x, z] = normalized * heightFactor;
        }

        return heights;
    }
}