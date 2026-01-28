namespace SzeneGenerator;
/// <summary>
/// Region-specific rules loaded from JSON controlling terrain layers,
/// spawning behavior, and local sky settings.
/// </summary>
public class RegionRules
{
    public string RegionId { get; set; }
    public TerrainRules Terrain { get; set; }
    public SpawningRules Spawning { get; set; }
    public LocalSkySettings LocalSky { get; set; }
}

public class TerrainRules
{
    // Order = Priority 
    public HeightBand[] HeightBands { get; set; } // height-only
    public string DefaultLayer { get; set; } = "grass";
    public SlopeOverride[] SlopeOverrides { get; set; } //height and slope depending
    public BiomeOverride[] BiomeOverrides { get; set; } // optional
    public float? NoiseScale { get; set; }
    public float? HeightFactor { get; set; }
    public float? OffSet { get; set; }
}

public class HeightBand
{
    public float MinMeters { get; set; }
    public float MaxMeters { get; set; }
    public string LayerId { get; set; } // matches AssetSet.TerrainSlots.LayerId
}

public class SlopeOverride
{
    public float MinDeg { get; set; }
    public float MaxDeg { get; set; }
    public string OverrideLayerId { get; set; } // e.g. "rock"
    public string[] AppliesTo { get; set; } // e.g. ["grass"] oder ["snow"]
    public float? BlendStartDeg { get; set; }
    public float? BlendEndDeg { get; set; }
}

public class SpawningRules
{
    public SpawnEntry[] Entries { get; set; }
}

public class SpawnEntry
{
    public string AssetId { get; set; }
    public float Weight { get; set; } = 1;
    public float DensityPerKm2 { get; set; } = 0; //0 = off
    public int MinCount { get; set; } = 1;
    public int MaxCount { get; set; } = 3;
    public float MinHeightMeters { get; set; } = -999999;
    public float MaxHeightMeters { get; set; } = 999999;
    public float MaxSlopeDeg { get; set; } = 90;
    public bool IsTarget { get; set; } = false;
    public int[] AllowedBiomes { get; set; } // optional
    public float? EdgeMinMeters { get; set; } // optional
    public float? EdgeMaxMeters { get; set; } // optional
    public float? MinDistanceMeters { get; set; } // optional
    public float? JitterStrength { get; set; } // optional
    public bool? AlignToSlope { get; set; } // optional
    public float? MaxTiltDeg { get; set; } // optional
}

public class BiomeOverride
{
    public int BiomeId { get; set; } // e.g. 0 field, 1 forest
    public string LayerId { get; set; } // e.g. "field_ground"/ "forest_floor"
}

public class LocalSkySettings
{
    // clear | cloudy | stormy
    public string Preset { get; set; }

    // Clouds – Cirrus (high)
    public float? CirrusCoverage { get; set; }
    public float? CirrusThickness { get; set; }
    public float? CirrusIntensity { get; set; }

    // Clouds – Cumulus (low)
    public float? CumulusCoverage { get; set; }
    public float? CumulusThickness { get; set; }
    public float? CumulusAbsorption { get; set; }
    public float? CumulusIntensity { get; set; }

    // Wind
    public float? WindSpeed { get; set; }
    public float? WindDirection { get; set; }

    // Atmosphere / Mood
    public float? AtmDarkness { get; set; }
    public float? AtmThickness { get; set; }
    public float? Exposure { get; set; }

    // Fog
    public bool? FogVisible { get; set; }
    public float? FogDensity { get; set; }
    public float? FogStart { get; set; }
    public float? FogEnd { get; set; }
    public float? FogFalloff { get; set; }

    // Optional: Region forces Rain
    public bool? RainEnabled { get; set; }
    // Rain intensity in range [0..1]. Used to scale particle amount and fall feel.
    public float? RainIntensity { get; set; }
    // Enable snow particles (visual only).
    public bool? SnowEnabled { get; set; }
    // Snow intensity in range [0..1]. Used to scale particle amount.
    public float? SnowIntensity { get; set; } 
    // Snow visual profile: "light", "heavy", "blizzard"
    public string SnowProfile { get; set; }
    // Rain visual profile: "light", "heavy", "storm" (optional)
    public string RainProfile { get; set; }


}