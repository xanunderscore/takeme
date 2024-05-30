using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;

namespace TakeMe;

public sealed class Plugin : IDalamudPlugin
{
    public static string Name => "takeme";

    private const string CommandName = "/takeme";

    public readonly WindowSystem WindowSystem = new("TakeMe");
    private readonly ConfigWindow configWindow;
    private readonly Overlay overlayWindow;
    private readonly Zodiac zodiacWindow;

    private static readonly List<(string, string)> HelpCommands =
    [
        ("new <name>", "Create a new waypoint at your current location called <name>"),
        ("newtarget <name>", "Create a new waypoint at your current target's location called <name>"),
        ("<waypoint>", "Go to the specified waypoint"),
        ("[target|mob] <name>", "Move to the nearest enemy/NPC called <name>"),
        ("quest", "Go to quest objective"),
        ("[c|cfg|config]", "Open configuration window")
    ];

    public Plugin([RequiredVersion("1.0")] DalamudPluginInterface pluginInterface)
    {
        pluginInterface.Create<Service>();
        Service.Init();

        var helpmess = "Open configuration\n";
        foreach ((var args, var desc) in HelpCommands)
            helpmess += $"/{Name} {args} → {desc}\n";

        Service.CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand) { HelpMessage = helpmess });
        zodiacWindow = new Zodiac();
        configWindow = new ConfigWindow();
        overlayWindow = new Overlay();

        WindowSystem.AddWindow(zodiacWindow);
        WindowSystem.AddWindow(configWindow);
        WindowSystem.AddWindow(overlayWindow);

        pluginInterface.UiBuilder.Draw += DrawUI;
        pluginInterface.UiBuilder.OpenConfigUi += () =>
        {
            configWindow.IsOpen = true;
        };
        pluginInterface.UiBuilder.OpenMainUi += () =>
        {
            overlayWindow.IsOpen = true;
        };
    }

    public void Dispose()
    {
        WindowSystem.RemoveAllWindows();

        Service.CommandManager.RemoveHandler(CommandName);

        Service.Config.Save();
    }

    private void OnCommand(string command, string args)
    {
        var arguments = args.Split(" ", 2);

        switch (arguments[0])
        {
            case "config":
            case "cfg":
            case "c":
                configWindow.IsOpen = !configWindow.IsOpen;
                break;
            case "mob":
            case "enemy":
            case "target":
                MoveTarget(arguments[1]);
                break;
            case "new":
                NewWaypoint(arguments[1]);
                break;
            case "newtarget":
                NewWaypointFromTarget(arguments[1]);
                break;
            case "":
                overlayWindow.IsOpen = true;
                break;
            case "cancel":
                Service.IPC.PathfindCancel();
                break;
            default:
                MoveWaypoint(args);
                break;
        }
    }

    public static void MoveTarget(string name)
    {
        if (!TryMoveTarget(name))
            Service.Toast.ShowError("No target found with that name.");
    }

    public static bool TryMoveTarget(string name)
    {
        var nearest = Service
            .ObjectTable
            .Where(
                x => x.IsTargetable && x.Name.ToString().Equals(name, StringComparison.OrdinalIgnoreCase) && !x.IsDead
            )
            .MinBy(x => (x.Position - Service.Player!.Position).Length());
        if (nearest == null)
        {
            return false;
        }

        Service.IPC.PathfindAndMoveTo(nearest.Position, false);
        return true;
    }

    public static void MoveWaypoint(string label)
    {
        var wp = Service.Config.Waypoints.FirstOrDefault(x => x.Label == label);
        if (wp == null)
        {
            Service.Toast.ShowError("Unrecognized waypoint name.");
            return;
        }

        Service.IPC.PathfindAndMoveTo(wp.Position, false);
    }

    public static void MoveWaypoint(Waypoint wp) => Service.IPC.PathfindAndMoveTo(wp.Position, false);

    public static void NewWaypoint(string label)
    {
        if (Service.Player is null)
            return;

        if (Service.Config.Waypoints.Any(x => x.Label == label && x.Zone == Service.ClientState.TerritoryType))
        {
            Service.Toast.ShowError("You already have a waypoint with that name.");
            return;
        }

        Service
            .Config
            .Waypoints
            .Add(
                new Waypoint
                {
                    Zone = Service.ClientState.TerritoryType,
                    Position = Service.Player.Position,
                    Label = label
                }
            );
    }

    public static void NewWaypointFromTarget(string label)
    {
        if (Service.Player is null)
            return;

        if (Service.TargetManager.Target is null)
        {
            Service.Toast.ShowError("No target.");
            return;
        }

        if (Service.Config.Waypoints.Any(x => x.Label == label && x.Zone == Service.ClientState.TerritoryType))
        {
            Service.Toast.ShowError("You already have a waypoint with that name.");
            return;
        }

        Service
            .Config
            .Waypoints
            .Add(
                new Waypoint
                {
                    Zone = Service.ClientState.TerritoryType,
                    Position = Service.TargetManager.Target.Position,
                    Label = label
                }
            );
    }

    private void DrawUI()
    {
        WindowSystem.Draw();
    }
}
