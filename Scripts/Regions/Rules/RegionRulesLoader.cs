using Godot;
using System.Text.Json;

namespace SzeneGenerator;

public static class RegionRulesLoader
{
	public static RegionRules Load(string path)
	{
		using var f = FileAccess.Open(path, FileAccess.ModeFlags.Read);
		var json = f.GetAsText();

		return JsonSerializer.Deserialize<RegionRules>(json, new JsonSerializerOptions
		{
			PropertyNameCaseInsensitive = true
		});
	}
}

