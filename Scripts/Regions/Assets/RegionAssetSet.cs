using Godot;
using Godot.Collections;

namespace SzeneGenerator;
/// <summary>
/// Region-specific asset container used to build a scene.
/// Holds Terrain3D texture slots and prefab mappings referenced by region JSON rules.
/// </summary>
[GlobalClass]
public partial class RegionAssetSet : Resource
{
    [Export] public string RegionId { get; set; } = "tundra";

    // Terrain3D texture slots: SlotIndex ist 0..31
    [Export] public Array<TerrainSlotEntry> TerrainSlots { get; set; } = new();

    // Flora prefabs by id
    [Export] public Array<PrefabEntry> FloraPrefabs { get; set; } = new();
}