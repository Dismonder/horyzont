using System;
using System.Collections.Generic;
using Godot;

public partial class SmokeTest : Node
{
    // Overload resolution happens at compile time, so this checks the GodotSharp NuGet package.
    private static bool IsDouble(double _) => true;
    private static bool IsDouble(float _) => false;

    public override void _Ready()
    {
        var failures = new List<string>();

        // Double precision in the NuGet package the project compiles against (and exports with).
        if (!IsDouble(default(Vector3).X))
            failures.Add("NuGet GodotSharp built with single precision");

        // Double precision in the GodotSharp loaded at run time: a project run by the editor
        // gets GodotSharp from engine/out/<key>/GodotSharp/, not from the package.
        if (typeof(Vector3).GetField(nameof(Vector3.X))!.FieldType != typeof(double))
            failures.Add("Editor GodotSharp built with single precision");

        // The Voxel Tools module must be in the engine and in the C# glue (compiling this line is a test too).
        var terrain = new VoxelLodTerrain();
        terrain.Free();
        if (!ClassDB.ClassExists("VoxelLodTerrain"))
            failures.Add("Voxel Tools module missing in engine");

        if (Environment.Version.Major < 10)
            failures.Add($".NET runtime {Environment.Version} is older than 10");

        GD.Print(failures.Count == 0 ? "SMOKE OK" : "SMOKE FAIL: " + string.Join("; ", failures));
        GetTree().Quit(failures.Count == 0 ? 0 : 1);
    }
}
