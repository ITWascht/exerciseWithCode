using System.Collections.Generic;
using Godot;

namespace SzeneGenerator;

public class WorldSettings
{
 public TerrainSettings Terrain { get; set; } = new();
 public GlobalSkySettings Sky { get; set; } = new();
 public CameraSettings Camera { get; set; } = new();
}

public class TerrainSettings
{
 //Szeneriegröße
 public int Width { get; set; } = 256;
 public int Depth { get; set; } = 256;
 //Heightmap, fallback if jsonimport fails
 public float NoiseScale { get; set; } = 3.5f;//structure, higher = rougher
 public float HeightFactor { get; set; } = 1.5f;//height multiplikator
 public float Offset { get; set; } = -8.0f; //height offset
 public float Scale { get; set; } = 15.0f;
 //Autoshader Terrain Parameter -- old
 public float SnowLineMeters { get; set; } = 25.0f;
 public float SteepSlopeDeg { get; set; } = 35.0f;
 
 //WorldBackground   aus = 0, flat=1, noise=2 (simulated mountains)
 public int WorldBackground { get; set; } = 1;
 
 //Scale 
 public float MetersPerPixel { get; set; } = 1.0f;
 
 //Terrain Texture and Object presets
 public string RegionId { get; set; } = "tundra"; //choose region
 
 //Seed for Objectspawning and Heightmapgenerating
 public int Seed { get; set; } = 0;// Seed will be overriden in following classes, 0 = no fixed seed
}

public class GlobalSkySettings
{
 //Rain on/off
 public bool RainEnabled { get; set; } = false;
 //Rainbox, size and offset
 public Vector3 RainOffset { get; set; } = new(0, 8, 0);
 public Vector3 RainBoxExtents { get; set; } = new(20, 1, 20);
 //Rainfog
 public bool FogAdjustForRain { get; set; } = true;
 public float RainFogDensity { get; set; } = 0.012f;
 public float RainFogDensityMultiplier { get; set; } = 1.0f;
 public float RainFogSunAmountMultiplier { get; set; } = 0.5f;
 public Color RainFogLightTint { get; set; } = new(0.85f, 0.9f, 1.0f, 1.0f);
 //daytime
 public float TimeOfDay { get; set; } = 12.0f;
}

public class CameraSettings
{
 public float Speed { get; set; } = 20.0f;
 public float MouseSensitivity { get; set; } = 0.1f;
 //Presets view
 public int StartPresetId { get; set; } = 2; //Preset view
 public Dictionary<int, (Vector3 pos, Vector3 rot)> Presets { get; set; } = new()
 {
  { 1, (new Vector3(0, 20, 50),  new Vector3(0, -90, 0)) },
  { 2, (new Vector3(0, 40, 50),  new Vector3(-25, -90, 0)) },
  { 3, (new Vector3(0, 150, 50), new Vector3(-40, -90, 0)) },
 };
 public Dictionary<int, CameraTargetPreset> TargetPresets { get; set; } = new()
 {
  // 1: "Egoperson"
  { 1, new CameraTargetPreset { DistanceMin = 8f, DistanceMax = 40f, ElevationMinDeg = 10f, ElevationMaxDeg = 30f, FocusHeight = 1.0f } },

  // 2: "multicopter"
  { 2, new CameraTargetPreset { DistanceMin = 10f, DistanceMax = 60f, ElevationMinDeg = 30f, ElevationMaxDeg = 60f, FocusHeight = 0.0f } },

  // 3: "fixed wing"
  { 3, new CameraTargetPreset { DistanceMin = 60f, DistanceMax = 100f, ElevationMinDeg = 80f, ElevationMaxDeg = 89f, FocusHeight = 0.0f } },
 };
}

public class CameraTargetPreset
{
 public bool EnableTargetFocus { get; set; } = true;  // master switch
 public bool AutoLookAtTarget { get; set; } = true;   // keep looking at target in _Process
 public float FocusHeight { get; set; } = 1.0f;       // look-at offset (center of object)

 public float DistanceMin { get; set; } = 6f;
 public float DistanceMax { get; set; } = 18f;

 public float ElevationMinDeg { get; set; } = 10f;
 public float ElevationMaxDeg { get; set; } = 30f;

 public bool RequireLineOfSight { get; set; } = false; // optional later
 public int Attempts { get; set; } = 60;               // tries to find a valid view
}

