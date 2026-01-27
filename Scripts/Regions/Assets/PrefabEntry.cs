using Godot;

namespace SzeneGenerator;

[GlobalClass]
public partial class PrefabEntry : Resource
{
    [Export] public string AssetId { get; set; } // "tundra_pine_a"
    [Export] public PackedScene Scene { get; set; }
}