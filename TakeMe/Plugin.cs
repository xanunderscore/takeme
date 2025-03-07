using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Fate;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace TakeMe;

public sealed unsafe class Plugin : IDalamudPlugin
{
    public static string Name => "takeme";

    private const string CommandName = "/takeme";

    public readonly WindowSystem WindowSystem = new("TakeMe");
    private readonly ConfigWindow configWindow;
    private readonly Overlay overlayWindow;
    private readonly Queue<Destination> nextDestination = [];
    private Vector3? highlightDestination = null;
    private uint DestinationFateId;

    public record struct Destination(Vector3 Point, bool Fly, uint FateId = 0);

    private static readonly List<(string, string)> HelpCommands =
    [
        ("new <name>", "Create a new waypoint at your current location called <name>"),
        ("newtarget <name>?", "Create a new waypoint at your current target's location called <name> - if no name provided, uses target's nameplate"),
        ("<waypoint>", "Go to the specified waypoint"),
        ("[target|mob] <name>", "Move to the nearest enemy/NPC called <name>"),
        ("quest", "Go to quest objective"),
        ("[c|cfg|config]", "Open configuration window")
    ];

    public Plugin(IDalamudPluginInterface pluginInterface)
    {
        pluginInterface.Create<Service>();
        Service.Init(this);
        Service.Hook.InitializeFromAttributes(this);

        var helpmessage = "Open configuration\n";
        foreach ((var args, var desc) in HelpCommands)
            helpmessage += $"/{Name} {args} → {desc}\n";

        Camera.Instance = new();

        Service.CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand) { HelpMessage = helpmessage });
        configWindow = new ConfigWindow();
        overlayWindow = new Overlay();

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
        Service.Framework.Update += Tick;
    }

    public void Dispose()
    {
        Service.Framework.Update -= Tick;
        WindowSystem.RemoveAllWindows();

        Service.CommandManager.RemoveHandler(CommandName);

        Service.Config.Save();
    }

    private void Tick(IFramework framework)
    {
        if (DestinationFateId > 0)
        {
            var fate = FateManager.Instance();
            if (fate->CurrentFate != null && fate->CurrentFate->FateId == DestinationFateId && Service.IPC.PathActive)
            {
                Service.IPC.PathfindCancel();
                DestinationFateId = 0;
            }
        }

        if (nextDestination.TryPeek(out var nextDest))
        {
            var nd = nextDest;
            var dest = nd.Point;
            var forceFly = nd.Fly;
            var playerPos = Service.Player!.Position;

            if (Vector3.Distance(dest, playerPos) > 20f && !forceFly && WaitMount())
                return;

            nextDestination.Dequeue();

            DestinationFateId = nd.FateId;

            Service.Log.Debug($"pathfind from {playerPos} -> {dest}");
            Service
                .IPC
                .PathfindAndMoveTo(
                    dest,
                    Service.Condition[ConditionFlag.InFlight] || Service.Condition[ConditionFlag.Jumping] || forceFly
                );

            if (forceFly)
                WaitMount();
        }
    }

    private static unsafe bool WaitMount()
    {
        if (Service.Condition[ConditionFlag.Mounted])
            return false;

        if (Service.Condition[ConditionFlag.Casting] || Service.Condition[ConditionFlag.Unknown57])
            return true; // wait for cast to end

        if (ActionManager.Instance()->GetActionStatus(ActionType.GeneralAction, 9) != 0)
            return false; // can't mount here

        ActionManager.Instance()->UseAction(ActionType.GeneralAction, 9);

        return true;
    }

    internal void Goto(Vector3 destination, bool forceFly = false, uint fateId = 0)
    {
        nextDestination.Enqueue(new(destination, forceFly, fateId));
    }

    private void OnCommand(string command, string args)
    {
        var arguments = args.Split(" ", 2).ToList();
        if (arguments.Count == 1)
            arguments.Add("");

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
                overlayWindow.IsOpen = !overlayWindow.IsOpen;
                break;
            case "cancel":
                Service.IPC.PathfindCancel();
                break;
            default:
                MoveWaypoint(args);
                break;
        }
    }

    public void MoveTarget(string name)
    {
        if (!TryMoveTarget(name))
            Service.Toast.ShowError("No target found with that name.");
    }

    public bool TryMoveTarget(string name)
    {
        var nearest = Service
            .ObjectTable
            .Where(
                x => x.IsTargetable && x.Name.ToString().Equals(name, StringComparison.OrdinalIgnoreCase) && !x.IsDead
            )
            .MinBy(x => (x.Position - Service.Player!.Position).Length());
        if (nearest == null)
            return false;

        Goto(nearest.Position);
        return true;
    }

    public void MoveWaypoint(string label)
    {
        var wp = Service.Config.Waypoints.FirstOrDefault(x => x.Label == label);
        if (wp == null)
        {
            Service.Toast.ShowError("Unrecognized waypoint name.");
            return;
        }

        Goto(wp.Position);
    }

    public void MoveWaypoint(Waypoint wp) => Goto(wp.Position);

    public static void NewWaypoint(string label)
    {
        if (Service.Player is null)
            return;

        if (label == "")
        {
            Service.Toast.ShowError("Waypoint name required.");
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
                    Position = Service.Player.Position,
                    Label = label,
                    Icon = 0,
                    SortOrder = -1
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

        if (label == "")
            label = Service.TargetManager.Target.Name.ToString();

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
                    Label = label,
                    Icon = 0,
                    SortOrder = -1
                }
            );
    }

    private void DrawUI()
    {
        Camera.Instance?.Update();
        WindowSystem.Draw();
        if (highlightDestination != null)
        {
            Camera.Instance?.DrawWorldLine(Service.Player!.Position, highlightDestination.Value, 0xff00ff00);
        }
        Camera.Instance?.DrawWorldPrimitives();
    }

    internal void HighlightDestination(Vector3 dest)
    {
        if (highlightDestination == dest)
            highlightDestination = null;
        else
            highlightDestination = dest;
    }
}
