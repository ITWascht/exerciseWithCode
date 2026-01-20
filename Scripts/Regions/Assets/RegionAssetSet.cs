using Godot;

namespace SzeneGenerator;

[GlobalClass]
public partial class RegionAssetSet : Resource
{
    [Export] public string RegionId { get; set; } = "tundra";

    // Terrain3D texture slots: SlotIndex ist 0..31
    [Export] public Godot.Collections.Array<TerrainSlotEntry> TerrainSlots { get; set; } = new();

    // Flora prefabs by id
    [Export] public Godot.Collections.Array<PrefabEntry> FloraPrefabs { get; set; } = new();
}



