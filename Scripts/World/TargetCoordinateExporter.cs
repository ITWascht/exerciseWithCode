using Godot;
using System.Collections.Generic;

namespace SzeneGenerator;

public static class TargetCoordinateExporter
{
    // Exports target transforms (and optional metadata) into a JSON file.
    // Use "user://..." paths for write access in exported builds.
    public static void ExportTargetsToJson(
        IReadOnlyList<Node3D> targets,
        string outputPath,
        string regionId,
        int seed)
    {
        if (targets == null)
        {
            GD.PushWarning("TargetCoordinateExporter: targets list is null.");
            return;
        }

        if (string.IsNullOrWhiteSpace(outputPath))
        {
            GD.PushWarning("TargetCoordinateExporter: outputPath is empty.");
            return;
        }

        // Build JSON using Godot collections so Json.Stringify works reliably
        var root = new Godot.Collections.Dictionary
        {
            ["region_id"] = regionId ?? "",
            ["seed"] = seed,
            ["count"] = targets.Count
        };

        var arr = new Godot.Collections.Array();

        for (var i = 0; i < targets.Count; i++)
        {
            var t = targets[i];
            if (t == null || !GodotObject.IsInstanceValid(t))
                continue;

            // Optional metadata (set in ObjectSpawner for targets)
            var assetId = t.HasMeta("asset_id") ? t.GetMeta("asset_id").AsString() : "";

            var p = t.GlobalPosition;
            var r = t.GlobalRotationDegrees;

            var entry = new Godot.Collections.Dictionary
            {
                ["index"] = i,
                ["name"] = t.Name,
                ["asset_id"] = assetId,
                ["position"] = new Godot.Collections.Dictionary
                {
                    ["x"] = p.X, ["y"] = p.Y, ["z"] = p.Z
                },
                ["rotation_deg"] = new Godot.Collections.Dictionary
                {
                    ["x"] = r.X, ["y"] = r.Y, ["z"] = r.Z
                }
            };

            arr.Add(entry);
        }

        root["targets"] = arr;

        // Ensure directory exists
        EnsureDirectoryExists(outputPath);

        // Write file
        var json = Json.Stringify(root, "\t");
        using var f = FileAccess.Open(outputPath, FileAccess.ModeFlags.Write);

        if (f == null)
        {
            GD.PushError($"TargetCoordinateExporter: failed to write file: {outputPath}");
            return;
        }

        f.StoreString(json);
        GD.Print($"TargetCoordinateExporter: wrote target coordinates: {outputPath}");
    }

    private static void EnsureDirectoryExists(string filePath)
    {
        // Convert to absolute OS path so recursive create works reliably
        var dir = filePath.GetBaseDir();
        if (string.IsNullOrEmpty(dir))
            return;

        var abs = ProjectSettings.GlobalizePath(dir);
        DirAccess.MakeDirRecursiveAbsolute(abs);
    }
}
