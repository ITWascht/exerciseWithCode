# SceneGenerator – Project Documentation


<p align="center">
  <img src="images/mainpage.png" width="700"/>
</p>



## Overview

**SceneGenerator** is a Godot-based system for procedurally building 3D scenes, generating rendered images, and exporting structured metadata about objects inside those scenes.

The system creates fully assembled environments based on JSON configuration files and produces:

- A rendered image (screenshot) of the generated scene  
- A JSON file containing the coordinates of defined target objects  

This enables automated scene creation and data extraction for further processing.

---

## Scene Construction System

Scenes are built using **regional JSON configuration files**.  
Each region can contain any number of **biomes**, allowing diverse environmental compositions.

### Regions
A region defines the overall environment structure and references an **asset package** that provides:

- 3D models  
- Textures  
- Scene objects  

The JSON configuration controls how these assets are placed and combined.

### Biomes
Biomes are sub-environment types within a region (e.g., vegetation, terrain style, object distribution).

---

## Weather System

Weather can be controlled through configurable modes:

| Category | Modes |
|---------|------|
| **Clouds** | `clear`, `cloudy`, `stormy` |
| **Snow** | `light` (default), `heavy`, `blizzard` |
| **Rain** | `light` (default), `heavy`, `storm` |

These settings influence the visual and environmental conditions of the generated scene.

---

## Target Object System

Certain 3D objects can be marked as: isTarget = true


During scene generation:

- Target objects are placed **within the camera’s field of view** (not necessarily centered).
- After rendering, the system exports:
  - The scene screenshot
  - A JSON file containing the **world coordinates of all targets**

This allows automated generation of image–annotation pairs.

---

## Output

Each generation cycle produces:

1. A rendered image of the scene  
2. A JSON file with coordinates of all target objects  

---

## Documentation Structure

- **API Reference** — automatically generated from the C# source code  
- **Project Overview** — high-level explanation of system components and behavior  

Use the navigation menu to explore the API details.


