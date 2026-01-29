# Class Diagram

```mermaid
classDiagram
direction LR

class WorldManager {
  +string RegionId
  +void Initialize()
  +void Generate()
}

class TerrainManager {
  +void Initialize(Node parent)
  +float GetHeightAt(float x, float z)
}

class GlobalSkyManager {
  +void ApplyPreset(string preset)
  +void ApplyWeather()
}

class SimpleCamera {
  +float Speed
  +float MouseSensitivity
  +bool SpawnRandomFromPreset(int seed, int presetId)
}

class HeightMapCreator {
  +float[,] GenerateAndApplyHeightMap(GodotObject terrainData)
}

class SimpleNoiseGenerator {
  +float[,] GenerateHeightMap(int width, int height)
}

class ObjectSpawner {
  +IEnumerable~Node3D~ SpawnAll()
  +IEnumerable~Node3D~ GetTargets()
}

class TargetCoordinateExporter {
  +void Export(string path, IEnumerable~Node3D~ targets)
}

class RegionRulesLoader {
  +RegionRules Load(string path)
}

class RegionRules {
  +string RegionId
  +TerrainRules Terrain
  +SpawningRules Spawning
  +LocalSkySettings LocalSky
}

class TerrainRules {
  +float? NoiseScale
  +float? HeightFactor
  +float? OffSet
  +string DefaultLayer
}

class SpawningRules {
  +SpawnEntry[] Entries
}

class SpawnEntry {
  +string AssetId
  +float DensityPerKm2
  +bool IsTarget
  +int MinCount
  +int MaxCount
}

class LocalSkySettings {
  +string Preset
  +bool? RainEnabled
  +float? RainIntensity
  +string RainProfile
  +bool? SnowEnabled
  +string SnowProfile
  +float? SnowIntensity
}

class RegionAssetSet {
  +TerrainSlotEntry[] TerrainSlots
  +PrefabEntry[] Prefabs
}

class TerrainAssetManager {
  +void ApplyAssets(RegionAssetSet assetSet)
}

%% Relationships (pipeline)
WorldManager --> RegionRulesLoader : loads
RegionRulesLoader --> RegionRules : returns
WorldManager --> RegionAssetSet : uses
WorldManager --> GlobalSkyManager : configures
WorldManager --> TerrainManager : initializes
WorldManager --> SimpleCamera : frames targets
TerrainManager --> HeightMapCreator : generates
HeightMapCreator --> SimpleNoiseGenerator : uses
TerrainManager --> TerrainAssetManager : applies textures
TerrainAssetManager --> RegionAssetSet : reads
TerrainManager --> ObjectSpawner : spawns
ObjectSpawner --> SpawnEntry : uses rules
ObjectSpawner --> TargetCoordinateExporter : exports targets

%% Rules structure
RegionRules --> TerrainRules
RegionRules --> SpawningRules
RegionRules --> LocalSkySettings
SpawningRules --> SpawnEntry
