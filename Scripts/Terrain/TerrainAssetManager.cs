using Godot;

namespace SzeneGenerator;
/// <summary>
/// Creates and applies Terrain3D asset resources (textures) from a RegionAssetSet.
/// </summary>
public class TerrainAssetManager
{
    public GodotObject CreateAssetsFrom(RegionAssetSet set)
    {
        var terrainAssets = (GodotObject)ClassDB.Instantiate("Terrain3DAssets");

        foreach (var slot in set.TerrainSlots)
        {
            var textureAsset = (GodotObject)ClassDB.Instantiate("Terrain3DTextureAsset");
            textureAsset.Call("set_albedo_texture", slot.AlbedoHeight);
            textureAsset.Call("set_normal_texture", slot.NormalRoughness);
            terrainAssets.Call("set_texture", slot.SlotIndex, textureAsset);
        }

        terrainAssets.Call("update_texture_list");
        return terrainAssets;
    }

    public void ApplyToTerrain(Node3D terrainNode, RegionAssetSet set)
    {
        var assets = CreateAssetsFrom(set);
        terrainNode.Set("assets", assets);
    }
}