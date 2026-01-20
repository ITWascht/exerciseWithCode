using System;
using System.Collections.Generic;
using Godot;
using System.Threading.Tasks;

namespace SzeneGenerator;

public partial class SimpleCamera : Camera3D
{
    
    public float Speed = 20f;
    public float MouseSensitivity = 0.1f;
    private List<Node3D> _targets = new();
    private Node3D _currentTarget;
    private Func<Vector3, float> _sampleHeightMeters;
    public bool AutoLookAtTarget = true;
    public float FocusHeight = 1.0f;



    //CameraPresets
    private Dictionary<int, (Vector3 pos, Vector3 rot)> _presets = new()
    {
        { 1, (new Vector3(0, 20, 50),  new Vector3(0, -90, 0)) }, //Ego
        { 2, (new Vector3(0, 40, 50),  new Vector3(-25, -90, 0)) }, // Multicopter
        { 3, (new Vector3(0, 150, 50), new Vector3(-40, -90, 0)) } // fixed-Wing
    };
    
    private int _startPresetId = 1;
    private bool _mouseLookActive = false;
    
    public override void _Ready()
    {
        //activate camera 
        Current = true;
        ApplyPreset(_startPresetId);
    }

    public override void _Process(double delta)
    {
     // camera movement
     float d = (float)delta;
     var direction = Vector3.Zero;
     if (Input.IsActionPressed("move_forward"))
     {
         direction -= Transform.Basis.Z;
     }
     if (Input.IsActionPressed("move_back"))
     {
      direction += Transform.Basis.Z;   
     }
     if (Input.IsActionPressed("move_left"))
     {
         direction -= Transform.Basis.X;
     }
     if (Input.IsActionPressed("move_right"))
     {
         direction += Transform.Basis.X;
     }
     if (Input.IsActionPressed("move_up"))
     {
         direction += Vector3.Up;
     }
     if (Input.IsActionPressed("move_down"))
     {
         direction += Vector3.Down;
     }
     
     // only move when movement true
     if (direction != Vector3.Zero)
     {
         direction  = direction.Normalized();
         GlobalTranslate(direction * Speed * d);
     }
     
     //stay fokused on target
     if (AutoLookAtTarget && _currentTarget != null && IsInstanceValid(_currentTarget))
     {
         Vector3 targetPos = _currentTarget.GlobalPosition + new Vector3(0, FocusHeight, 0);
         LookAt(targetPos, Vector3.Up);
     }
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
    }
    
    //mouseaiming for look around
    public override void _Input(InputEvent @event)
    {
        //Rechtsklick aktiviert/ deaktiviert Mauslook
        if (@event is InputEventMouseButton mouseButtonEvent)
        {
            if (mouseButtonEvent.ButtonIndex == MouseButton.Right && mouseButtonEvent.Pressed)
            {
                _mouseLookActive = !_mouseLookActive;

                Input.MouseMode = _mouseLookActive ? Input.MouseModeEnum.Captured : Input.MouseModeEnum.Visible;
            }
        }
        
        //rotate camera, when mouse active
        if (_mouseLookActive && @event is InputEventMouseMotion mouseMotionEvent)
        {
            //mousemovement
            Vector2 rel = mouseMotionEvent.Relative;
            
            //y turning
            RotateY(Mathf.DegToRad(-rel.X * MouseSensitivity));
            
            //up/down
            RotateObjectLocal(Vector3.Right, Mathf.DegToRad(-rel.Y * MouseSensitivity));
            
            // avoid headdown
            Vector3 rot = RotationDegrees;
            rot.X = Mathf.Clamp(rot.X, -89f, 89f);
            RotationDegrees = rot;
        }
    }
    
    public void Configure(CameraSettings cfg)
    {
        if (cfg == null) return;
        Speed = cfg.Speed;
        MouseSensitivity = cfg.MouseSensitivity;
        _presets = cfg.Presets ?? new();
        _startPresetId = cfg.StartPresetId;
    }
    
    //focus on target
    public void SetTargets(List<Node3D> targets)
    {
        _targets = targets ?? new List<Node3D>();
    }

    public void SetHeightSampler(Func<Vector3, float> sampler)
    {
        _sampleHeightMeters = sampler;
    }
    
    //focus random target if target>1
    public void FocusRandomTarget(int seed, CameraTargetPreset preset)
    {
        if (_targets == null || _targets.Count == 0)
            return;
        if (preset == null || !preset.EnableTargetFocus)
            return;
        AutoLookAtTarget = preset.AutoLookAtTarget;
        FocusHeight = preset.FocusHeight;
        var rng = new Random(seed);
        _currentTarget = _targets[rng.Next(_targets.Count)];
        Vector3 targetPos = _currentTarget.GlobalPosition + new Vector3(0, FocusHeight, 0);
        float dist = Mathf.Lerp(preset.DistanceMin, preset.DistanceMax, (float)rng.NextDouble());
        float az = (float)(rng.NextDouble() * Math.Tau);
        float el = Mathf.DegToRad(Mathf.Lerp(preset.ElevationMinDeg, preset.ElevationMaxDeg, (float)rng.NextDouble()));
        Vector3 offset = new Vector3(
            Mathf.Cos(az) * Mathf.Cos(el),
            Mathf.Sin(el),
            Mathf.Sin(az) * Mathf.Cos(el)
        ) * dist;
        GlobalPosition = targetPos + offset;
        LookAt(targetPos, Vector3.Up);
    }
    
    //Screenshot for image Export
    public async Task<string> TakeScreenshotAsync(string fileNameWithoutExt = "")
    {
        // 1) Wait for rendering
        int framesToWait = 10; // 5–15 realistic for rain to show up
        for (int i = 0; i < framesToWait; i++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }

        // 2) take picture from viewport
        Image img = GetViewport().GetTexture().GetImage();
        //img.FlipY(); // if necessary

        // 3) build path
        if (string.IsNullOrWhiteSpace(fileNameWithoutExt))
            fileNameWithoutExt = $"screenshot_{Time.GetDatetimeStringFromSystem().Replace(':','-')}";

        string dir = @"C:\GodotProjects\SzeneGenerator\Screenshots";
        DirAccess.MakeDirRecursiveAbsolute(dir);

        string path = $"{dir}/{fileNameWithoutExt}.png";

        // 4) safe report
        Error err = img.SavePng(path);
        if (err != Error.Ok)
        {
            GD.PushError($"Screenshot save failed: {err} ({path})");
            return "";
        }

        GD.Print($"Saved screenshot: {path}");
        return path;
    }



}