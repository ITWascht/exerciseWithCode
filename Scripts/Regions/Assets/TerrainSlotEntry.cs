using Godot;

namespace SzeneGenerator;

[GlobalClass]
public partial class TerrainSlotEntry : Resource
{
    [Export] public int SlotIndex { get; set; } // 0..31
    [Export] public string LayerId { get; set; } // "grass", "rock", "snow" etc.
    [Export] public Texture2D AlbedoHeight { get; set; }
    [Export] public Texture2D NormalRoughness { get; set; }
}