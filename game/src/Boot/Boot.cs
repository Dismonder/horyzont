using Godot;

namespace Horyzont.Game.Boot;

/// <summary>Entry scene: picks the start mode from the user arguments after <c>--</c>.</summary>
public partial class Boot : Node
{
    public override void _Ready()
    {
        var args = OS.GetCmdlineUserArgs();
        if (args.Contains("--smoke"))
        {
            RunSmoke();
            return;
        }

        GD.Print("Horyzont modes (to do in K2): --server, --host, --connect <address>");
        GetTree().Quit(0);
    }

    private void RunSmoke()
    {
#if GODOT_REAL_T_IS_DOUBLE
        const string RealT = "double";
#else
        const string RealT = "float";
#endif
        var engine = (string)Engine.GetVersionInfo()["string"];
        GD.Print($"BOOT OK engine={engine} real_t={RealT} dotnet={System.Environment.Version}");
        GetTree().Quit(0);
    }
}
