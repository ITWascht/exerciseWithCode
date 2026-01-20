using System;
using System.Collections.Generic;
using Godot;

namespace SzeneGenerator;

public class HeightMapCreator
{
    private readonly TerrainRules _terrainRules;
    private readonly Dictionary<string, int> _layerToSlot;
    private readonly int _width;
    private readonly int _depth;
    private readonly int _seed;
    private readonly float _noiseScale;
    private readonly float _heightFactor;
    private readonly float _offset;
    private readonly float _scale;
    private readonly Vector3 _worldPos;
    //"Meter per Pixel" for Slope-Calculation
    private readonly float _metersPerPixel;
    
    //Biome
    private int[,] _biome; 

    public HeightMapCreator(
        int width,
        int depth,
        int seed,
        float noiseScale,
        float heightFactor,
        float offset,
        float scale,
        Vector3 worldPos,
        float metersPerPixel,
        TerrainRules terrainRules,
        Dictionary<string, int> layerToSlot,
        int[,] biome = null
    )
    {
        _width = width;
        _depth = depth;
        _seed = seed;
        _noiseScale = noiseScale;
        _heightFactor = heightFactor;
        _offset = offset;
        _scale = scale;
        _worldPos = worldPos;
        _metersPerPixel = metersPerPixel;
        _terrainRules = terrainRules;
        _layerToSlot = layerToSlot;
        _biome =  biome;
    }

    public float[,] GenerateAndApplyHeightMap(GodotObject terrainData)
    {
        // instanziate Noise-Generator 
        var noiseGen = new SimpleNoiseGenerator(seed: _seed, frequency: 0.01f);
        // generate Heightmap 
        float[,] heights01 = noiseGen.GenerateHeightMap(
            _width,
            _depth,
            noiseScale: _noiseScale,
            heightFactor: _heightFactor
        );
        
        // --- Height image ---
        var heightImg = Image.CreateEmpty(_width, _depth, false, Image.Format.Rf);
        for (int x = 0; x < _width; x++)
        for (int z = 0; z < _depth; z++)
            heightImg.SetPixel(x, z, new Color(heights01[x, z], 0f, 0f, 1f));
        var controlImg = BuildControlMap(heights01);
        var images = new Godot.Collections.Array<Image> { heightImg, controlImg, null };

        // Import to Terrain3DData
        terrainData.Call("import_images", images, _worldPos, _offset, _scale);
        terrainData.Call("calc_height_range", false);
        terrainData.Call("update_maps");
        
        return heights01;
    }
    
    public void ApplyBiomeTexturing(GodotObject terrainData, float[,] heights01, int[,] biome)
    {
        _biome = biome;
        // Rebuild height image (safe, even if unchanged)
        var heightImg = Image.CreateEmpty(_width, _depth, false, Image.Format.Rf);
        for (int x = 0; x < _width; x++)
        for (int z = 0; z < _depth; z++)
            heightImg.SetPixel(x, z, new Color(heights01[x, z], 0f, 0f, 1f));
        // Rebuild control map with biome-aware PickLayerBlend()
        var controlImg = BuildControlMap(heights01);
        var images = new Godot.Collections.Array<Image> { heightImg, controlImg, null };

        terrainData.Call("import_images", images, _worldPos, _offset, _scale);
        terrainData.Call("calc_height_range", false);
        terrainData.Call("update_maps");
    }


    private static float PackControlToFloat(int baseId, int overlayId, byte blend = 0, bool autoshader = false)
    {
        uint x = 0;
        x |= (uint)(baseId & 0x1F) << 27;
        x |= (uint)(overlayId & 0x1F)<< 22;
        x |= (uint)(blend & 0xFF) << 14;
        
        if (autoshader) x |= 1u;
        return BitConverter.Int32BitsToSingle(unchecked((int)x));
    }
    
    private static LayerBlend PickLayerBlend(float heightMeters, float slopeDeg, int biomeId, TerrainRules rules)
    {
        // 1) Baselayer over Height
        string baseLayer = rules?.DefaultLayer ?? "grass";
        if (rules?.HeightBands != null)
        {
            foreach (var b in rules.HeightBands)
            {
                if (heightMeters >= b.MinMeters && heightMeters < b.MaxMeters)
                {
                    baseLayer = b.LayerId;
                    break;
                }
            }
        }
        
        // Biome base-layer override (only if provided)
        if (biomeId >= 0 && rules?.BiomeOverrides != null)
        {
            for (int i = 0; i < rules.BiomeOverrides.Length; i++)
            {
                var b = rules.BiomeOverrides[i];
                if (b != null && b.BiomeId == biomeId && !string.IsNullOrEmpty(b.LayerId))
                {
                    baseLayer = b.LayerId;
                    break;
                }
            }
        }

        // Default: no Blend
        var result = new LayerBlend(baseLayer, baseLayer, 0f);

        // 2) Conditional Slope-Overrides
        if (rules?.SlopeOverrides == null)
            return result;

        foreach (var o in rules.SlopeOverrides)
        {
            // apply Override-Rules for baseLayer?
            if (o.AppliesTo != null && o.AppliesTo.Length > 0)
            {
                bool ok = false;
                for (int i = 0; i < o.AppliesTo.Length; i++)
                    if (o.AppliesTo[i] == baseLayer) { ok = true; break; }
                if (!ok) continue;
            }

            // Blend-Range existing?
            if (o.BlendStartDeg.HasValue && o.BlendEndDeg.HasValue)
            {
                float a = o.BlendStartDeg.Value;
                float b = o.BlendEndDeg.Value;

                if (slopeDeg < a) continue; 
                if (slopeDeg >= b) return new LayerBlend(o.OverrideLayerId, o.OverrideLayerId, 0f); // full override

                // in Borderregion: base -> override
                float t = (slopeDeg - a) / (b - a);  // 0..1
                t = Mathf.Clamp(t, 0f, 1f);
                return new LayerBlend(baseLayer, o.OverrideLayerId, t);
            }
            else
            {
                // hard border
                if (slopeDeg >= o.MinDeg && slopeDeg < o.MaxDeg)
                    return new LayerBlend(o.OverrideLayerId, o.OverrideLayerId, 0f);
            }
        }

        return result;
    }
        
    private readonly struct LayerBlend
    {
        public readonly string BaseLayer;
        public readonly string OverlayLayer;
        public readonly float Blend01; // 0..1
        public LayerBlend(string baseLayer, string overlayLayer, float blend01)
        {
            BaseLayer = baseLayer;
            OverlayLayer = overlayLayer;
            Blend01 = blend01;
        }
    }
    
    private Image BuildControlMap(float[,] heights01)
    {
        var control = Image.CreateEmpty(_width, _depth, false, Image.Format.Rf);
        for (int x = 0; x < _width; x++)
        {
            for (int z = 0; z < _depth; z++)
            {
                // Height in meter (scale + offset as in Import)
                float hM = heights01[x, z] * _scale + _offset;
                // --- Slope (Degrees) over Height-gradient ---
                int x0 = Mathf.Max(x - 1, 0);
                int x1 = Mathf.Min(x + 1, _width - 1);
                int z0 = Mathf.Max(z - 1, 0);
                int z1 = Mathf.Min(z + 1, _depth - 1);

                float hL = heights01[x0, z] * _scale + _offset;
                float hR = heights01[x1, z] * _scale + _offset;
                float hD = heights01[x, z0] * _scale + _offset;
                float hU = heights01[x, z1] * _scale + _offset;

                float dhdx = (hR - hL) / (2f * _metersPerPixel);
                float dhdz = (hU - hD) / (2f * _metersPerPixel);

                float slopeRad = Mathf.Atan(Mathf.Sqrt(dhdx * dhdx + dhdz * dhdz));
                float slopeDeg = slopeRad * 57.29578f;
                
                int biomeId = (_biome != null) ? _biome[x, z] : -1;

                // --- Rulebased + Blend: Baselayer + Overlaylayer + Blend + biome---
                var lb = PickLayerBlend(hM, slopeDeg, biomeId, _terrainRules);

// BaseLayer -> Slot
                int baseSlot;
                if (!_layerToSlot.TryGetValue(lb.BaseLayer, out baseSlot))
                {
                    if (_terrainRules?.DefaultLayer != null && _layerToSlot.TryGetValue(_terrainRules.DefaultLayer, out var def))
                        baseSlot = def;
                    else
                        baseSlot = 0;
                }

// OverlayLayer -> Slot (if not found, baseslot)
                int overlaySlot;
                if (!_layerToSlot.TryGetValue(lb.OverlayLayer, out overlaySlot))
                    overlaySlot = baseSlot;
                
                //Blend change
                byte blendByte = (byte)Mathf.RoundToInt(Mathf.Clamp(lb.Blend01, 0f, 1f) * 255f);
                
// integrate Blend  (0..1 or 0..255 depending on PackControlToFloat)
                float packed = PackControlToFloat(baseSlot, overlaySlot, blend: blendByte, autoshader: false);
                control.SetPixel(x, z, new Color(packed, 0f, 0f, 1f));
            }
        }
        return control;
    }
    
}
