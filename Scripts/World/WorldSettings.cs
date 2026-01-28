using System.Collections.Generic;
using Godot;

namespace SzeneGenerator;
/// <summary>
/// Root configuration container for terrain, sky, and camera settings.
/// </summary>
public class WorldSettings
{
    public TerrainSettings Terrain { get; set; } = new();
    public GlobalSkySettings Sky { get; set; } = new();
    public CameraSettings Camera { get; set; } = new();
}

public class TerrainSettings
{
    // Terrain3D region size (must match Terrain3D region_size options: 128, 256, 512, ...)
    public int RegionSize { get; set; } = 128;

    // Heightmap defaults (used as fallback if region rules / JSON import fails)
    public float NoiseScale { get; set; } = 3.5f;    // structure: higher = rougher
    public float HeightFactor { get; set; } = 1.5f;  // height multiplier
    public float Offset { get; set; } = -8.0f;        // height offset

    // Vertical scale factor for heightmap -> world meters
    public float Scale { get; set; } = 15.0f;
    
    // World background mode: off = 0, flat = 1, noise = 2 (simulated mountains)
    public int WorldBackground { get; set; } = 1;

    // World scale in meters per heightmap pixel
    public float MetersPerPixel { get; set; } = 1.0f;

    // Terrain texture and object presets
    public string RegionId { get; set; } = "field_road_forest";

    // Seed for object spawning and heightmap generation (0 = random)
    public int Seed { get; set; } = 0;

    // Ensures the region size is a supported value (power of two, >= 128)
    public int GetValidatedRegionSize()
    {
        var s = RegionSize;

        // Minimum supported by our Terrain3D setup
        if (s < 128) s = 128;

        // Snap to next power-of-two (128, 256, 512, ...)
        var p = 128;
        while (p < s) p <<= 1;

        return p;
    }
    
    // Export for spawned target positions
    public bool ExportTargetCoordinates { get; set; } = true;
    //path for export
    public string TargetCoordinatesPath { get; set; }
        = @"C:\GodotProjects\Screenshots\target_coordinates\target_coordinates.json";

}

public class GlobalSkySettings
{
    // Rain on/off
    public bool RainEnabled { get; set; } = false;

    // Rain box (position offset and extents)
    public Vector3 RainOffset { get; set; } = new(0, 8, 0);
    public Vector3 RainBoxExtents { get; set; } = new(20, 1, 20);

    // Rain fog adjustments
    public bool FogAdjustForRain { get; set; } = true;
    public float RainFogDensity { get; set; } = 0.012f;
    //public float RainFogDensityMultiplier { get; set; } = 1.0f;
    public float RainFogSunAmountMultiplier { get; set; } = 0.5f;
    public Color RainFogLightTint { get; set; } = new(0.85f, 0.9f, 1.0f);

    // Time of day (hours)
    public float TimeOfDay { get; set; } = 12.0f;
}

public class CameraSettings
{
    public float Speed { get; set; } = 20.0f;
    public float MouseSensitivity { get; set; } = 0.1f;

    // Default camera start preset (absolute view preset, not target-orbit)
    public int StartPresetId { get; set; } = 1;// View preset-_-_-_-_-_-_-_-_-_-_-_-_-_-_-_-_-_-_

    // Absolute camera pose presets (position + rotation)
    public Dictionary<int, (Vector3 pos, Vector3 rot)> Presets { get; set; } = new()
    {
        { 1, (new Vector3(0, 1.8f, 50), new Vector3(0, -90, 0)) },//Ego
        { 2, (new Vector3(0, 40, 50), new Vector3(-25, -90, 0)) },//Multicopter
        { 3, (new Vector3(0, 150, 50), new Vector3(-40, -90, 0)) }// fixed Wing
    };

    // Target-orbit presets (random camera spawn around a target)
    public Dictionary<int, CameraTargetPreset> TargetPresets { get; set; } = new()
    {
        // 1: "Ego person"
        {
            1,
            new CameraTargetPreset
            {
                DistanceMin = 10.0f, DistanceMax = 46.0f,
                ElevationMinDeg = -2f, ElevationMaxDeg = 8f,

                // Aim around head/upper body height for natural framing
                FocusHeight = 1.7f,

                AutoLookAtTarget = false,
                RequireLineOfSight = true,
                Attempts = 300, //to find a spot with view on the Target

                FocusOffsetRightMeters = 1.0f,
                FocusOffsetUpMeters = 0.2f,
                LineOfSightToleranceMeters = 0.75f,

                EdgeMarginMeters = 8f
            }
        },

        // 2: "Multicopter"
        {
            2,
            new CameraTargetPreset
            {
                DistanceMin = 12f, DistanceMax = 35f,
                ElevationMinDeg = 20f, ElevationMaxDeg = 45f,

                // Still aim at the subject, not at ground level
                FocusHeight = 1.7f,

                AutoLookAtTarget = false,
                RequireLineOfSight = true,
                Attempts = 120,//to find a spot with view on the Target

                FocusOffsetRightMeters = 2.0f,
                FocusOffsetUpMeters = 0.6f,
                LineOfSightToleranceMeters = 1.25f,

                EdgeMarginMeters = 15f
            }
        },

        // 3: "Fixed wing"
        {
            3,
            new CameraTargetPreset
            {
                DistanceMin = 60f, DistanceMax = 140f,
                ElevationMinDeg = 55f, ElevationMaxDeg = 80f,

                // Slightly above subject for flyover feel
                FocusHeight = 2.0f,

                AutoLookAtTarget = false,
                RequireLineOfSight = true,
                Attempts = 150,//to find a spot with view on the Target

                FocusOffsetRightMeters = 6.0f,
                FocusOffsetUpMeters = 2.0f,
                LineOfSightToleranceMeters = 2.5f,

                EdgeMarginMeters = 25f
            }
        },
    };
}

public class CameraTargetPreset
{
    // Master switch for target-based spawn/focus behavior
    public bool EnableTargetFocus { get; set; } = true;

    // If enabled, the camera would keep looking at the target in _Process (not recommended here)
    public bool AutoLookAtTarget { get; set; } = true;

    // Look-at offset on the target (e.g. center / roof / above ground)
    public float FocusHeight { get; set; } = 1.0f;

    public float DistanceMin { get; set; } = 6f;
    public float DistanceMax { get; set; } = 18f;

    public float ElevationMinDeg { get; set; } = 10f;
    public float ElevationMaxDeg { get; set; } = 30f;

    // Optional visibility constraints
    public bool RequireLineOfSight { get; set; } = false;
    public int Attempts { get; set; } = 60;

    // Off-center framing offsets (meters in camera right/up directions)
    // Horizontal: positive => target appears more to the left
    public float FocusOffsetRightMeters { get; set; } = 2.0f;

    // Vertical: positive => target appears lower in the frame
    public float FocusOffsetUpMeters { get; set; } = 0.5f;

    // Allows small foreground obstructions near the target
    public float LineOfSightToleranceMeters { get; set; } = 0.75f;

    // Minimum distance to the heightmap border (in world meters)
    // Used to prevent the terrain edge/cliff from becoming visible.
    public float EdgeMarginMeters { get; set; } = 10f;
}
