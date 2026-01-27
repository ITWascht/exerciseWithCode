using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using System.IO;

namespace SzeneGenerator;

public partial class SimpleCamera : Camera3D
{
    private Node3D _currentTarget;
    private bool _mouseLookActive;

    // World bounds used to keep random camera spawns inside the heightmap area
    private bool _hasWorldBounds;
    private float _minX, _maxX, _minZ, _maxZ;
    private float _edgeMarginMeters;

    // Camera presets (absolute start poses)
    private Dictionary<int, (Vector3 pos, Vector3 rot)> _presets = new()
    {
        { 1, (new Vector3(0, 20, 50), new Vector3(0, -90, 0)) },   // Ego
        { 2, (new Vector3(0, 40, 50), new Vector3(-25, -90, 0)) }, // Multicopter
        { 3, (new Vector3(0, 150, 50), new Vector3(-40, -90, 0)) } // Fixed-Wing
    };

    private Func<Vector3, float> _sampleHeightMeters;

    private int _startPresetId = 1;
    private List<Node3D> _targets = new();

    public bool AutoLookAtTarget = true;
    public float FocusHeight = 1.0f;
    public float MouseSensitivity = 0.1f;
    public float Speed = 20f;

    public override void _Ready()
    {
        // Activate this camera
        Current = true;

        // Apply an initial absolute preset pose (can be overridden later by FocusRandomTarget)
        ApplyPreset(_startPresetId);
    }

    public override void _Process(double delta)
    {
        // Keyboard movement (free-fly)
        var d = (float)delta;
        var direction = Vector3.Zero;

        if (Input.IsActionPressed("move_forward")) direction -= Transform.Basis.Z;
        if (Input.IsActionPressed("move_back")) direction += Transform.Basis.Z;
        if (Input.IsActionPressed("move_left")) direction -= Transform.Basis.X;
        if (Input.IsActionPressed("move_right")) direction += Transform.Basis.X;
        if (Input.IsActionPressed("move_up")) direction += Vector3.Up;
        if (Input.IsActionPressed("move_down")) direction += Vector3.Down;

        if (direction != Vector3.Zero)
        {
            direction = direction.Normalized();
            GlobalTranslate(direction * Speed * d);
        }

        // Target lock is intentionally disabled:
        // We only aim once during spawn selection, then the camera remains freely movable.
        // if (AutoLookAtTarget && _currentTarget != null && IsInstanceValid(_currentTarget))
        // {
        //     var targetPos = _currentTarget.GlobalPosition + new Vector3(0, FocusHeight, 0);
        //     LookAt(targetPos, Vector3.Up);
        // }
    }

    public override void _Input(InputEvent @event)
    {
        // Right mouse button toggles mouse-look (capture/release cursor)
        if (@event is InputEventMouseButton mouseButtonEvent)
        {
            if (mouseButtonEvent.ButtonIndex == MouseButton.Right && mouseButtonEvent.Pressed)
            {
                _mouseLookActive = !_mouseLookActive;
                Input.MouseMode = _mouseLookActive
                    ? Input.MouseModeEnum.Captured
                    : Input.MouseModeEnum.Visible;
            }
        }

        // While mouse-look is active, rotate camera by mouse motion
        if (_mouseLookActive && @event is InputEventMouseMotion mouseMotionEvent)
        {
            var rel = mouseMotionEvent.Relative;

            // Yaw (turn left/right)
            RotateY(Mathf.DegToRad(-rel.X * MouseSensitivity));

            // Pitch (look up/down)
            RotateObjectLocal(Vector3.Right, Mathf.DegToRad(-rel.Y * MouseSensitivity));

            // Clamp pitch to avoid flipping the camera
            var rot = RotationDegrees;
            rot.X = Mathf.Clamp(rot.X, -89f, 89f);
            RotationDegrees = rot;
        }
    }

    public void Configure(CameraSettings cfg)
    {
        if (cfg == null) return;

        Speed = cfg.Speed;
        MouseSensitivity = cfg.MouseSensitivity;

        // Presets are provided by settings (fallback to empty dictionary if missing)
        _presets = cfg.Presets ?? new Dictionary<int, (Vector3 pos, Vector3 rot)>();
        _startPresetId = cfg.StartPresetId;
    }

    public void ApplyPreset(int id)
    {
        if (!_presets.TryGetValue(id, out var p))
        {
            GD.PushWarning($"Camera preset {id} not found!");
            return;
        }

        GlobalPosition = p.pos;
        RotationDegrees = p.rot;
        DebugPrintCameraPosition($"ApplyPreset({id})");

    }

    public void SetTargets(List<Node3D> targets)
    {
        _targets = targets ?? new List<Node3D>();
    }

    public void SetHeightSampler(Func<Vector3, float> sampler)
    {
        _sampleHeightMeters = sampler;
    }

    // Configure allowed spawn area (in world meters)
    public void SetWorldBounds(float minX, float maxX, float minZ, float maxZ, float edgeMarginMeters)
    {
        _hasWorldBounds = true;
        _minX = minX;
        _maxX = maxX;
        _minZ = minZ;
        _maxZ = maxZ;
        _edgeMarginMeters = Mathf.Max(0, edgeMarginMeters);
    }

    private bool IsInsideBoundsWithMargin(Vector3 pos)
    {
        if (!_hasWorldBounds) return true;

        return pos.X >= _minX + _edgeMarginMeters &&
               pos.X <= _maxX - _edgeMarginMeters &&
               pos.Z >= _minZ + _edgeMarginMeters &&
               pos.Z <= _maxZ - _edgeMarginMeters;
    }
    
    public bool SpawnRandomFromPreset(int seed, int presetId, CameraTargetPreset focusPreset)
{
    if (focusPreset == null) return false;
    if (_targets == null || _targets.Count == 0) return false;
    if (!_hasWorldBounds) return false;
    if (_sampleHeightMeters == null) return false;

    if (!_presets.TryGetValue(presetId, out var presetPose))
        return false;

    var rng = new Random(seed);

    // Treat the preset's Y as "height above ground" (keeps 3 camera styles).
    var heightAboveGround = presetPose.pos.Y;

    // Off-center framing comes from target preset (unchanged).
    AutoLookAtTarget = focusPreset.AutoLookAtTarget;
    FocusHeight = focusPreset.FocusHeight;

    var rightOffset = focusPreset.FocusOffsetRightMeters;
    var upOffset = focusPreset.FocusOffsetUpMeters;

    var attempts = Math.Max(1, focusPreset.Attempts);

    for (int i = 0; i < attempts; i++)
    {
        // Pick a random target each attempt
        var t = _targets[rng.Next(_targets.Count)];
        if (t == null || !IsInstanceValid(t))
            continue;

        var baseTargetPos = t.GlobalPosition + new Vector3(0, FocusHeight, 0);

        // Random X/Z inside bounds + edge margin
        var x = (float)(rng.NextDouble() * ((_maxX - _edgeMarginMeters) - (_minX + _edgeMarginMeters)) + (_minX + _edgeMarginMeters));
        var z = (float)(rng.NextDouble() * ((_maxZ - _edgeMarginMeters) - (_minZ + _edgeMarginMeters)) + (_minZ + _edgeMarginMeters));

        var candidatePos = new Vector3(x, 0f, z);

        // Terrain height at candidate position
        var ground = _sampleHeightMeters(candidatePos);
        if (float.IsNegativeInfinity(ground))
            continue;

        candidatePos.Y = ground + heightAboveGround;
        // Ensure the camera never spawns inside/below the terrain surface.
        candidatePos.Y = Mathf.Max(candidatePos.Y, ground + 0.25f);


        // Must be inside bounds (safety check)
        if (!IsInsideBoundsWithMargin(candidatePos))
            continue;

        // Apply position
        GlobalPosition = candidatePos;

        // Aim so the target is visible (not necessarily centered)
        var aimPos = ComputeAimPoint(baseTargetPos, rightOffset, upOffset);
        LookAt(aimPos, Vector3.Up);

        // Line-of-sight: reject only if terrain blocks the target
        if (focusPreset.RequireLineOfSight)
        {
            if (!HasGoodLineOfSight(baseTargetPos, focusPreset.LineOfSightToleranceMeters))
                continue;
        }

        _currentTarget = t;
        GD.Print(
            $"[CameraDebug] SpawnRandomFromPreset success | " +
            $"pos={GlobalPosition} rotY={GlobalRotationDegrees.Y:F1}"
        );


        return true;
    }
    GD.Print("[CameraDebug] SpawnRandomFromPreset failed (no valid candidate found).");
    return false;
}


    // Focus a random target by selecting a random orbit position around it.
    //  target will be visible but not centered (off-center framing).
    public void FocusRandomTarget(int seed, CameraTargetPreset preset)
    {
        GD.Print($"Camera: FocusRandomTarget called | targets={_targets?.Count ?? 0}, focusEnabled={(preset != null && preset.EnableTargetFocus)}");
        if (_targets == null || _targets.Count == 0) return;
        
        // Important: do NOT keep the camera locked to the target per frame.
        // We only aim once during spawn selection.
        AutoLookAtTarget = preset.AutoLookAtTarget;
        FocusHeight = preset.FocusHeight;

        var rng = new Random(seed);
        Vector3 baseTargetPos;

        if (_targets != null && _targets.Count > 0)
        {
            _currentTarget = _targets[rng.Next(_targets.Count)];
            if (_currentTarget == null || !IsInstanceValid(_currentTarget)) return;
            baseTargetPos = _currentTarget.GlobalPosition + new Vector3(0, FocusHeight, 0);
        }
        else
        {
            baseTargetPos = new Vector3(0, FocusHeight, 0);
        }

        // Base point we "care about" on the target (e.g. around subject height)
        //baseTargetPos = _currentTarget.GlobalPosition + new Vector3(0, FocusHeight, 0);

        // Off-center framing parameters (configured via preset)
        var rightOffset = preset.FocusOffsetRightMeters;
        var upOffset = preset.FocusOffsetUpMeters;

        // Clamp distances to the available terrain area so presets still work on small maps
        var dMin = preset.DistanceMin;
        var dMax = preset.DistanceMax;

        if (_hasWorldBounds)
        {
            // Compute max usable radius around the target inside bounds (including edge margin)
            var left = baseTargetPos.X - (_minX + _edgeMarginMeters);
            var right = (_maxX - _edgeMarginMeters) - baseTargetPos.X;
            var down = baseTargetPos.Z - (_minZ + _edgeMarginMeters);
            var up = (_maxZ - _edgeMarginMeters) - baseTargetPos.Z;

            var maxRadius = Mathf.Max(0f, Mathf.Min(Mathf.Min(left, right), Mathf.Min(down, up)));

            // Cap max distance; also ensure min <= max
            dMax = Mathf.Min(dMax, maxRadius);
            dMin = Mathf.Min(dMin, dMax);
        }

        var attempts = Math.Max(1, preset.Attempts);
        //debug counter
        int failBounds = 0;
        int failHeightmap = 0;
        int failBelowGround = 0;
        int failLineOfSight = 0;
        int success = 0;


        for (var i = 0; i < attempts; i++)
        {
            // If dMax becomes 0 (tiny maps or target near the edge), we cannot find a valid orbit position
            if (dMax <= 0.001f)
                break;

            // Random spherical position around the target (orbit-like)
            var dist = Mathf.Lerp(dMin, dMax, (float)rng.NextDouble());
            var az = (float)(rng.NextDouble() * Math.Tau);
            var el = Mathf.DegToRad(Mathf.Lerp(preset.ElevationMinDeg, preset.ElevationMaxDeg, (float)rng.NextDouble()));

            var offset = new Vector3(
                Mathf.Cos(az) * Mathf.Cos(el),
                Mathf.Sin(el),
                Mathf.Sin(az) * Mathf.Cos(el)
            ) * dist;

            var candidatePos = baseTargetPos + offset;

            // Reject positions outside the allowed terrain area (including edge margin)
            if (!IsInsideBoundsWithMargin(candidatePos))
            {
                failBounds++;
                continue;
            }


            // Terrain validation: reject outside heightmap and avoid spawning into the ground
            if (_sampleHeightMeters != null)
            {
                var groundHeight = _sampleHeightMeters(candidatePos);

                // Outside heightmap -> reject
                if (float.IsNegativeInfinity(groundHeight))
                {
                    failHeightmap++;
                    continue;
                }


                // Avoid placing the camera into/below the terrain surface
                if (candidatePos.Y < groundHeight + 0.5f)
                {
                    failBelowGround++;
                    continue;
                }

            }

            // Apply candidate position
            GlobalPosition = candidatePos;

            // Aim slightly beside the target so it stays visible but not centered
            var aimPos = ComputeAimPoint(baseTargetPos, rightOffset, upOffset);
            LookAt(aimPos, Vector3.Up);
            
            //final camera position
            DebugPrintCameraPosition("FocusRandomTarget success");


            // Optional line-of-sight validation (allows small overlaps near the target)
            if (preset.RequireLineOfSight)
            {
                if (!HasGoodLineOfSight(baseTargetPos, preset.LineOfSightToleranceMeters))
                {
                    failLineOfSight++;
                    continue;
                }
            }

            // Valid camera position found
            success++;
            GD.Print(
                $"Camera focus success | attempts={attempts}, " +
                $"bounds={failBounds}, heightmap={failHeightmap}, " +
                $"belowGround={failBelowGround}, los={failLineOfSight}"
            );

            return;
        }
        GD.Print(
            $"[CameraDebug] FocusRandomTarget FAILED | attempts={attempts}, " +
            $"failBounds={failBounds}, failHeightmap={failHeightmap}, " +
            $"failBelowGround={failBelowGround}, failLOS={failLineOfSight}, " +
            $"dMin={dMin:F1}, dMax={dMax:F1}, edgeMargin={_edgeMarginMeters:F1}"
        );



        // Fallback: keep a reasonable framing even if no valid candidate was found
        // Fallback: place the camera at a safe position near the target instead of keeping the preset pose.
        var fallbackDist = Mathf.Max(1f, dMin);
        var fallbackPos = baseTargetPos + new Vector3(fallbackDist, Mathf.Max(2f, fallbackDist * 0.3f), fallbackDist);

    // Clamp fallback into bounds if required
        if (_hasWorldBounds)
        {
            fallbackPos.X = Mathf.Clamp(fallbackPos.X, _minX + _edgeMarginMeters, _maxX - _edgeMarginMeters);
            fallbackPos.Z = Mathf.Clamp(fallbackPos.Z, _minZ + _edgeMarginMeters, _maxZ - _edgeMarginMeters);
        }

    // Keep above terrain if possible
        if (_sampleHeightMeters != null)
        {
            var ground = _sampleHeightMeters(fallbackPos);
            if (!float.IsNegativeInfinity(ground))
                fallbackPos.Y = Mathf.Max(fallbackPos.Y, ground + 1.0f);
        }

        GlobalPosition = fallbackPos;

        var fallbackAim = ComputeAimPoint(baseTargetPos, rightOffset, upOffset);
        LookAt(fallbackAim, Vector3.Up);

    }

    // Takes a screenshot from the current viewport and saves it as a PNG.
    // Optionally exports a JSON file with the same base filename (same folder, ".json" extension).
    public async Task<string> TakeScreenshotAsync(
        string fileNameWithoutExt = "",
        bool exportTargetCoordinates = false,
        string regionId = "",
        int seed = 0)
    {
        // 1) Wait a few frames to ensure everything is rendered (e.g., particles/weather)
        var framesToWait = 10; // 5–15 is typically enough
        for (var i = 0; i < framesToWait; i++)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

        // 2) Capture image from viewport
        var img = GetViewport().GetTexture().GetImage();
        // img.FlipY(); // enable if required by your pipeline

        // 3) Build file path
        if (string.IsNullOrWhiteSpace(fileNameWithoutExt))
            fileNameWithoutExt = $"screenshot_{Time.GetDatetimeStringFromSystem().Replace(':', '-')}";

        var dir = @"C:\GodotProjects\Screenshots\SzeneGenerator";
        DirAccess.MakeDirRecursiveAbsolute(dir);

        var pngPath = $"{dir}/{fileNameWithoutExt}.png";

        // 4) Save PNG
        var err = img.SavePng(pngPath);
        if (err != Error.Ok)
        {
            GD.PushError($"Screenshot save failed: {err} ({pngPath})");
            return "";
        }

        GD.Print($"Saved screenshot: {pngPath}");

        // 5) Optional: export target coordinates with the same base filename
        if (exportTargetCoordinates && _targets != null && _targets.Count > 0)
        {
            // Keep the filename aligned: screenshot.png -> screenshot.json
            var jsonPath = Path.ChangeExtension(pngPath, ".json");

            TargetCoordinateExporter.ExportTargetsToJson(
                _targets,
                jsonPath,
                regionId,
                seed
            );
        }

        return pngPath;
    }


    // Computes an off-center aim point so the target is visible but not centered in the frame.
    // rightMeters > 0 => aim more to the right => target appears more to the left in the image.
    private Vector3 ComputeAimPoint(Vector3 targetPos, float rightMeters, float upMeters)
    {
        // Godot convention: X = right, Y = up, -Z = forward
        var right = GlobalTransform.Basis.X.Normalized();
        var up = GlobalTransform.Basis.Y.Normalized();

        return targetPos + right * rightMeters + up * upMeters;
    }

    // Checks whether the target is sufficiently visible from the camera.
    // Small foreground overlaps are allowed using a distance tolerance (in meters).
    private bool HasGoodLineOfSight(Vector3 targetPos, float toleranceMeters)
    {
        var space = GetWorld3D().DirectSpaceState;
        var query = PhysicsRayQueryParameters3D.Create(GlobalPosition, targetPos);

        var hit = space.IntersectRay(query);

        // No hit at all -> unobstructed
        if (hit.Count == 0)
            return true;

        // Allow partial overlaps:
        // Accept if the first obstruction is very close to the target point.
        var hitPos = (Vector3)hit["position"];
        var distHit = GlobalPosition.DistanceTo(hitPos);
        var distTarget = GlobalPosition.DistanceTo(targetPos);

        return distHit >= distTarget - toleranceMeters;
    }
    
    //Debug camera position
    private void DebugPrintCameraPosition(string reason)
    {
        GD.Print(
            $"[CameraDebug] {reason} | " +
            $"pos={GlobalPosition} rotY={GlobalRotationDegrees.Y:F1}"
        );
    }

}
