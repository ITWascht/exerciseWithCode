# Example Region Configuration

This example shows how a region is fully defined using a JSON rules file.



---

## Region Identity

`"regionId": "field_road_forest"`
**File:** [`field_road_forest_exs.rules.json`](examples/field_road_forest_exs.rules.json)

Defines the region name and links this configuration to the matching asset set.
<p align="center">
  <img src="images/field_road_forest.png" width="850"/>
</p>
This example shows how a region is fully defined using a JSON rules file.
---

## Terrain Configuration

**Base parameters**

| Parameter | Value | Effect |
|-----------|------|--------|
| noiseScale | 2 | Controls terrain variation frequency |
| heightFactor | 0.5 | Scales elevation intensity |
| offSet | 0 | Vertical terrain shift |
| defaultLayer | grass | Base terrain texture |

---

### Biome Texture Overrides

| Biome ID | Texture Layer |
|----------|---------------|
| 0 | field_ground |
| 1 | forest_floor |
| 2 | road_ground |

These overrides replace the default terrain layer depending on biome classification.

---

## Object Spawning

Each entry defines how objects appear in the scene.

### Forest Trees

- **assetId:** pine_tree  
- **densityPerKm2:** 15000  
- **allowedBiomes:** [1]  

Pine trees spawn only in the forest biome.

---

### Wheat Fields

- **assetId:** wheat  
- **densityPerKm2:** 300000  
- **allowedBiomes:** [0]  

High-density crop spawning in field areas.

---

### Road Targets

- **assetId:** target  
- **densityPerKm2:** 1  
- **minCount:** 1  
- **isTarget:** true  
- **allowedBiomes:** [2]  

This object:

- Is treated as a **scene target**
- Spawns on the road biome
- Must be visible in the camera frame
- Has its coordinates exported to JSON

---

## Weather Configuration

| Setting | Value | Effect |
|--------|------|-------|
| preset | Stormy | Dark sky and heavy clouds |
| rainEnabled | true | Rain system active |
| rainProfile | storm | Intense rainfall visuals |
| snowEnabled | false | No snowfall |

---

## Resulting Scene

Using this configuration, the system generates:

- A mixed environment with fields, forest, and road areas  
- Region-specific textures  
- Biome-dependent object placement  
- Stormy weather conditions  
- At least one visible target object on the road  
- Screenshot + JSON export of target coordinates  

---
# Example Region Configuration – Winter Tundra

This configuration defines a cold, snow-dominated environment with rocky slopes and sparse vegetation.

**File:** [`winter_tundra_exs.rules.json`](examples/winter_tundra.rules_exs.json)

<p align="center">
  <img src="images/winter_tundra.png" width="850"/>
</p>

This example shows how a region is fully defined using a JSON rules file.
---

## Terrain Behavior

| Setting | Value | Effect |
|--------|------|-------|
| noiseScale | 1.5 | Medium terrain variation |
| heightFactor | 1.0 | Strong vertical elevation |
| defaultLayer | grass | Base layer before overrides |

### Height-Based Layers

| Height Range (m) | Layer |
|------------------|-------|
| -100 → 18 | snowGround |
| 18 → 25 | rock |
| 25 → 999 | snow |

Terrain becomes snow-covered at higher elevations.

---

### Slope Overrides

Steep terrain automatically transitions to rock:

| Slope Angle | Result |
|------------|-------|
| >45° | snowGround → rock |
| >50° | rock stays rock |
| >60° | snow blends into rock |

This prevents unrealistic snow on cliffs.

---

### Biome Overrides

| Biome ID | Layer |
|----------|------|
| 0 | field_ground |
| 1 | snowGround |

---

## Object Spawning

### Pine Trees
- Forest biome only  
- Moderate density  
- No slope alignment  

### Tundra Shrubs
- Spawn in multiple biomes  
- High density  
- Align with terrain slope  

### Rocks
- Low density  
- Large minimum distance  

### Targets
- **3 targets guaranteed**
- Spaced apart
- Must appear in camera view
- Exported to JSON

---

## Weather Setup

| Setting | Value | Effect |
|--------|------|-------|
| preset | Stormy | Dark sky and heavy cloud cover |
| rainEnabled | true | Rain system active |
| rainProfile | storm | Intense precipitation visuals |
| snowEnabled | false | Snow particles disabled |

Despite the snowy terrain, active precipitation is configured as storm rain for dramatic atmosphere.

---

## Resulting Scene

This configuration produces:

- Snow-covered landscape with rocky cliffs  
- Sparse cold-climate vegetation  
- High-contrast lighting  
- Multiple visible targets  
- Screenshot + coordinate export  

---

This example shows how terrain, slope rules, spawning density, and atmosphere combine to form a cold-region biome.

---

This example demonstrates how terrain, assets, objects, weather, and output behavior are fully controlled through region configuration.
