# Key Configuration Settings

This page explains the most important configuration options that control scene generation behavior.

---

## Region Rules JSON

`res://assets/regions/{regionId}/{regionId}.rules.json`

This file defines how a region behaves during generation.

### Terrain Parameters

| Setting | Description |
|--------|-------------|
| **NoiseScale** | Controls terrain frequency. Higher values create more variation. |
| **HeightFactor** | Multiplier that scales terrain elevation. |
| **OffSet** | Vertical offset applied to terrain height. |

---

### Texture Layering

| Rule Type | Purpose |
|-----------|--------|
| **HeightBands** | Assigns terrain textures based on height ranges. |
| **SlopeOverrides** | Replaces base textures depending on terrain steepness. |
| **BiomeOverrides** | Overrides terrain textures based on biome ID. |

---

### Object Spawning (SpawnEntry)

| Setting | Description |
|--------|-------------|
| **AssetId** | References a prefab from the RegionAssetSet. |
| **DensityPerKm2** | Spawn density per square kilometer. |
| **MinCount** | Minimum number of objects that must spawn. |
| **MaxSlopeDeg** | Prevents spawning on steep surfaces. |
| **MinHeightMeters / MaxHeightMeters** | Height range filter for spawning. |
| **AllowedBiomes** | Restricts spawning to specific biome IDs. |
| **IsTarget** | Marks objects that must be visible to the camera and exported. |
| **MinDistanceMeters** | Minimum distance between objects. |
| **AlignToSlope** | Rotates object to match terrain slope. |

---

## Target System

Objects with: IsTarget = true

will:

- Be prioritized during spawning  
- Be placed within the camera’s view  
- Have their coordinates exported to JSON  

---

## Weather Settings (LocalSkySettings)

| Category | Settings |
|----------|---------|
| **Cloud Preset** | `clear`, `cloudy`, `stormy` |
| **Rain Modes** | `light`, `heavy`, `storm` |
| **Snow Modes** | `light`, `heavy`, `blizzard` |
| **Fog Settings** | Visibility, density, start/end distance |
| **Atmosphere** | Darkness, thickness, exposure |

These settings control the visual mood of the generated scene.

---

## Camera Settings

| Setting | Description |
|--------|-------------|
| **TargetPresets** | Defines camera distance, height, and framing relative to targets. |
| **EdgeMarginMeters** | Prevents camera from spawning near terrain borders. |

---

## Output Behavior

| Feature | Description |
|--------|-------------|
| **Screenshot Export** | Captures the generated scene image. |
| **TargetCoordinateExporter** | Exports world coordinates of all target objects to JSON. |

---

These settings allow region-specific behavior while keeping the generation system modular and reusable.


