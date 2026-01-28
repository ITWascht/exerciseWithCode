using System;
using Godot;
using Godot.Collections;

namespace SzeneGenerator;
/// <summary>
/// Creates procedural heightmaps and control maps for Terrain3D,
/// applying noise, terrain rules, biome blending, and slope-based layer selection.
/// </summary>
public class HeightMapCreator
{
    private readonly int _depth;
    private readonly float _heightFactor;

    private readonly System.Collections.Generic.Dictionary<string, int> _layerToSlot;

    //"Meter per Pixel" for Slope-Calculation
    private readonly float _metersPerPixel;
    private readonly float _noiseScale;
    private readonly float _offset;
    private readonly float _scale;
    private readonly int _seed;
    private readonly TerrainRules _terrainRules;
    private readonly int _width;
    private readonly Vector3 _worldPos;

    //Biome
    private int[,] _biome;
    
    // Distance to nearest biome edge (meters), used for soft transitions.
    private float[,] _edgeDist;


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
        TerrainRules terrainRules, System.Collections.Generic.Dictionary<string, int> layerToSlot,
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
        _biome = biome;
    }

    /// <summary>
    /// Generates the heightmap image, applies it to Terrain3D data,
    /// and returns normalized height values for further processing.
    /// </summary>
    public float[,] GenerateAndApplyHeightMap(GodotObject terrainData)
    {
        // instanziate Noise-Generator 
        var noiseGen = new SimpleNoiseGenerator(_seed);
        // generate Heightmap 
        var heights01 = noiseGen.GenerateHeightMap(
            _width,
            _depth,
            _noiseScale,
            _heightFactor
        );

        // --- Height image ---
        var heightImg = Image.CreateEmpty(_width, _depth, false, Image.Format.Rf);
        for (var x = 0; x < _width; x++)
        for (var z = 0; z < _depth; z++)
            heightImg.SetPixel(x, z, new Color(heights01[x, z], 0f, 0f));
        var controlImg = BuildControlMap(heights01);
        var images = new Array<Image> { heightImg, controlImg, null };

        // Import to Terrain3DData
        terrainData.Call("import_images", images, _worldPos, _offset, _scale);
        terrainData.Call("calc_height_range", false);
        terrainData.Call("update_maps");

        return heights01;
    }

    public void ApplyBiomeTexturing(GodotObject terrainData, float[,] heights01, int[,] biome, float[,] edgeDist)
    {
        _biome = biome;
        _edgeDist = edgeDist;
        // Rebuild height image (safe, even if unchanged)
        var heightImg = Image.CreateEmpty(_width, _depth, false, Image.Format.Rf);
        for (var x = 0; x < _width; x++)
        for (var z = 0; z < _depth; z++)
            heightImg.SetPixel(x, z, new Color(heights01[x, z], 0f, 0f));
        // Rebuild control map with biome-aware PickLayerBlend()
        var controlImg = BuildControlMap(heights01);
        var images = new Array<Image> { heightImg, controlImg, null };

        terrainData.Call("import_images", images, _worldPos, _offset, _scale);
        terrainData.Call("calc_height_range", false);
        terrainData.Call("update_maps");
    }


    private static float PackControlToFloat(int baseId, int overlayId, byte blend = 0, bool autoshader = false)
    {
        uint x = 0;
        x |= (uint)(baseId & 0x1F) << 27;
        x |= (uint)(overlayId & 0x1F) << 22;
        x |= (uint)(blend & 0xFF) << 14;

        if (autoshader) x |= 1u;
        return BitConverter.Int32BitsToSingle(unchecked((int)x));
    }

    private static LayerBlend PickLayerBlend(float heightMeters, float slopeDeg, int biomeId, TerrainRules rules)
    {
        // 1) Baselayer over Height
        var baseLayer = rules?.DefaultLayer ?? "grass";
        if (rules?.HeightBands != null)
            foreach (var b in rules.HeightBands)
                if (heightMeters >= b.MinMeters && heightMeters < b.MaxMeters)
                {
                    baseLayer = b.LayerId;
                    break;
                }

        // Biome base-layer override (only if provided)
        if (biomeId >= 0 && rules?.BiomeOverrides != null)
            for (var i = 0; i < rules.BiomeOverrides.Length; i++)
            {
                var b = rules.BiomeOverrides[i];
                if (b != null && b.BiomeId == biomeId && !string.IsNullOrEmpty(b.LayerId))
                {
                    baseLayer = b.LayerId;
                    break;
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
                var ok = false;
                for (var i = 0; i < o.AppliesTo.Length; i++)
                    if (o.AppliesTo[i] == baseLayer)
                    {
                        ok = true;
                        break;
                    }

                if (!ok) continue;
            }

            // Blend-Range existing?
            if (o.BlendStartDeg.HasValue && o.BlendEndDeg.HasValue)
            {
                var a = o.BlendStartDeg.Value;
                var b = o.BlendEndDeg.Value;

                if (slopeDeg < a) continue;
                if (slopeDeg >= b) return new LayerBlend(o.OverrideLayerId, o.OverrideLayerId, 0f); // full override

                // in Borderregion: base -> override
                var t = (slopeDeg - a) / (b - a); // 0..1
                t = Mathf.Clamp(t, 0f, 1f);
                return new LayerBlend(baseLayer, o.OverrideLayerId, t);
            }

            // hard border
            if (slopeDeg >= o.MinDeg && slopeDeg < o.MaxDeg)
                return new LayerBlend(o.OverrideLayerId, o.OverrideLayerId, 0f);
        }

        return result;
    }

    private Image BuildControlMap(float[,] heights01)
    {
        var control = Image.CreateEmpty(_width, _depth, false, Image.Format.Rf);
        var biomeBlendWidthMeters = 2.0f; // Soft transition band width.
        
        string GetBaseLayerForBiome(float heightMeters, int biomeId)
        {
            // Start with default + height bands (same logic as PickLayerBlend base selection).
            var baseLayer = _terrainRules?.DefaultLayer ?? "grass";

            if (_terrainRules?.HeightBands != null)
                foreach (var b in _terrainRules.HeightBands)
                    if (heightMeters >= b.MinMeters && heightMeters < b.MaxMeters)
                    {
                        baseLayer = b.LayerId;
                        break;
                    }

            // Apply biome override if configured.
            if (biomeId >= 0 && _terrainRules?.BiomeOverrides != null)
                for (var i = 0; i < _terrainRules.BiomeOverrides.Length; i++)
                {
                    var o = _terrainRules.BiomeOverrides[i];
                    if (o != null && o.BiomeId == biomeId && !string.IsNullOrEmpty(o.LayerId))
                    {
                        baseLayer = o.LayerId;
                        break;
                    }
                }

            return baseLayer;
        }

        for (var x = 0; x < _width; x++)
        for (var z = 0; z < _depth; z++)
        {
            // Height in meter (scale + offset as in Import)
            var hM = heights01[x, z] * _scale + _offset;
            // --- Slope (Degrees) over Height-gradient ---
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
            var slopeDeg = slopeRad * 57.29578f;

            var biomeId = _biome != null ? _biome[x, z] : -1;
            
            // --- Rulebased + Blend: Baselayer + Overlaylayer + Blend + biome---
            var lb = PickLayerBlend(hM, slopeDeg, biomeId, _terrainRules);
            
            // --- Soft biome transitions (keeps slope overrides intact) ---
            if (_biome != null && _edgeDist != null && biomeId >= 0)
            {
                var dist = _edgeDist[x, z];

                // Only blend close to a biome edge.
                if (dist >= 0f && dist < biomeBlendWidthMeters)
                {
                    // Find a different biome in 4-neighborhood.
                    var neighborBiome = biomeId;

                    if (x > 0 && _biome[x - 1, z] != biomeId) neighborBiome = _biome[x - 1, z];
                    else if (x < _width - 1 && _biome[x + 1, z] != biomeId) neighborBiome = _biome[x + 1, z];
                    else if (z > 0 && _biome[x, z - 1] != biomeId) neighborBiome = _biome[x, z - 1];
                    else if (z < _depth - 1 && _biome[x, z + 1] != biomeId) neighborBiome = _biome[x, z + 1];

                    // Keep roads crisp (biomeId 2 in your setup).
                    if (neighborBiome != biomeId) // && biomeId != 2 && neighborBiome != 2)
                    {
                        // Only apply biome blending if there is no slope blending already.
                        if (lb.Blend01 <= 0.001f)
                        {
                            var baseLayer = GetBaseLayerForBiome(hM, biomeId);
                            var overlayLayer = GetBaseLayerForBiome(hM, neighborBiome);

                            // Blend fades out with distance; at the edge it's 0.5 for a soft seam.
                            var t = Mathf.Clamp(dist / biomeBlendWidthMeters, 0f, 1f);
                            // Slight irregularity to avoid ruler-straight edges (subtle, sub-meter band).
                            var n = Mathf.Abs(Mathf.Sin((x * 12.9898f + z * 78.233f) * 0.01f) * 43758.5453f) % 1f;
                            var jitter = (n - 0.5f) * 0.12f; // keep subtle
                            var blend01 = Mathf.Clamp(0.5f * (1f - t) + jitter, 0f, 0.5f);

                            // Add small organic "intrusions" along biome borders (1m grid friendly).
                            var hash = Mathf.Abs(Mathf.Sin((x * 12.9898f + z * 78.233f) * 0.13f) * 43758.5453f) % 1f;
                            if (dist < 1.25f && hash < 0.25f)
                            {
                                // Swap base/overlay occasionally to create tiny edge inlets.
                                var tmp = baseLayer;
                                baseLayer = overlayLayer;
                                overlayLayer = tmp;
                            }

                            lb = new LayerBlend(baseLayer, overlayLayer, blend01);
                        }
                    }
                }
            }


            // BaseLayer -> Slot
            int baseSlot;
            if (!_layerToSlot.TryGetValue(lb.BaseLayer, out baseSlot))
            {
                if (_terrainRules?.DefaultLayer != null &&
                    _layerToSlot.TryGetValue(_terrainRules.DefaultLayer, out var def))
                    baseSlot = def;
                else
                    baseSlot = 0;
            }

            // OverlayLayer -> Slot (if not found, baseslot)
            int overlaySlot;
            if (!_layerToSlot.TryGetValue(lb.OverlayLayer, out overlaySlot))
                overlaySlot = baseSlot;

            //Blend change
            var blendByte = (byte)Mathf.RoundToInt(Mathf.Clamp(lb.Blend01, 0f, 1f) * 255f);

            // integrate Blend  (0..1 or 0..255 depending on PackControlToFloat)
            var packed = PackControlToFloat(baseSlot, overlaySlot, blendByte);
            control.SetPixel(x, z, new Color(packed, 0f, 0f));
        }

        return control;
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
}