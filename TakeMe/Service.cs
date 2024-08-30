using Dalamud.Game;
using Dalamud.Game.ClientState.Objects;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Lumina.Excel;

namespace TakeMe;

public class Service
{
    public static IPlayerCharacter? Player => ClientState.LocalPlayer;

#nullable disable
    public static Plugin Plugin { get; private set; }
    public static Configuration Config { get; private set; }
    public static IPC IPC { get; private set; }

    [PluginService] public static IDalamudPluginInterface PluginInterface { get; private set; }
    [PluginService] public static ICommandManager CommandManager { get; private set; }
    [PluginService] public static IToastGui Toast { get; private set; }
    [PluginService] public static ISigScanner SigScanner { get; private set; }
    [PluginService] public static IClientState ClientState { get; private set; }
    [PluginService] public static IPluginLog Log { get; private set; }
    [PluginService] public static IObjectTable ObjectTable { get; private set; }
    [PluginService] public static IGameGui GameGui { get; private set; }
    [PluginService] public static ITargetManager TargetManager { get; private set; }
    [PluginService] public static IGameInteropProvider InteropProvider { get; private set; }
    [PluginService] public static ITextureProvider TextureProvider { get; private set; }
    [PluginService] public static IDataManager Data { get; private set; }
    [PluginService] public static ICondition Condition { get; private set; }
    [PluginService] public static IFramework Framework { get; private set; }
    [PluginService] public static IGameInteropProvider Hook { get; private set; }
#nullable enable

    public static void Init(Plugin p)
    {
        Config = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        IPC = new();
        Plugin = p;
    }

    public static T? ExcelRow<T>(uint rowid) where T : ExcelRow => Data.GetExcelSheet<T>()?.GetRow(rowid);
}
