using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Windowing;
using Dalamud.Utility;
using FFXIVClientStructs.FFXIV.Client.Game.Fate;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.Interop;
using ImGuiNET;
using Lumina.Excel.GeneratedSheets;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Numerics;

namespace TakeMe;

public class Overlay : Window
{
    private readonly Zodiac zod;
    private readonly ReadOnlyDictionary<ushort, byte> _territoryToAetherCurrentCompFlgSet;
    public bool PosDebug { get; set; }

    private const ushort BozjaZone = 920;
    private const ushort ZadnorZone = 0;

    public Overlay()
        : base("TakeMe Overlay")
    {
        zod = new();
        _territoryToAetherCurrentCompFlgSet = Service.Data.GetExcelSheet<TerritoryType>()!
            .Where(x => x.RowId > 0 && x.Unknown32 > 0)
            .ToDictionary(x => (ushort)x.RowId, x => x.Unknown32)
            .AsReadOnly();
        IsOpen = true;
    }

    private static readonly List<(string Name, Vector3 Position)> bozjaAetherytes = [("Utya's Aegis", new(-201.95f, 5.02f, 846.95f)), ("Olana's Stand", new(486.78f, 34.93f, 531.33f)), ("Lunya's Stand", new(-257.95f, 35.93f, 534.36f)), ("Camp Steva", new(169.79f, 2.95f, 192.28f))];

    private static readonly List<(string Name, Vector3 Position)> zadnorAetherytes = [];

    private static IEnumerable<Waypoint> WaypointsCurrentZone =>
        Service.Config.Waypoints.Where(x => x.Zone == Service.ClientState.TerritoryType);

    private unsafe ReadOnlySpan<Pointer<FateContext>> FatesCurrentZone => FateManager.Instance()->Fates.Span;

    private static readonly HashSet<uint> ImportantQuestIcons = [
        60490, // msq highlighted area
        60494, // feature quest highlighted area
        70963, // msq next objective, area transition
        71001, // msq new quest
        71003, // msq next objective
        71005, // msq quest complete
        71143, // feature quest next objective
        71145, // feature quest complete
    ];

    private static readonly HashSet<uint> UnimportantQuestIcons = [
        60091, // gemstone/courier unlock
        60987, // gemstone vendor
        60467, // adjoining area
        60458, 60493, 60501, 60502, 60503, 60504, 60512, 60934, // fates
        60801, 60802, // bozja fates
        71021, // sidequest
        71022, // repeatable sidequest
        71032, // daily feature quest, locked
        70965, // sidequest in other zone
        71142, // repeatable feature quest
        71151, // feature quest, locked
        71152, // repeatable feature quest, locked
        71121, // hall of novices
        71041, // leve
        71044, // leve eventobj
        71081, // guildhest
        63922, // moogle quest
        63933, // speech bubble
        71006, // icon placed on npc that gives you a required status effect for a quest
        63921, // sidequest icon shaped like a book? idk what this is
        60959, 63913, 60412, // bozja stuff
    ];

    // mapping of transparent "area of interest" icons to the quest type they correspond with
    private static readonly Dictionary<uint, uint> IconRemap = new() {
        // msq
        { 60490u, 71003u },
        // leve
        { 60492u, 61422u },
        // sidequest
        { 60494u, 71143u }
    };

    private static unsafe IEnumerable<MapMarkerData> QuestObjectivesCurrentZone
    {
        get
        {
            var items = new List<MapMarkerData>();
            var hd = AgentHUD.Instance();
            foreach (var d in hd->MapMarkers.Span)
            {
                if (ImportantQuestIcons.Contains(d.IconId))
                    items.Insert(0, d);
                else if (!UnimportantQuestIcons.Contains(d.IconId))
                    items.Add(d);
            }

            return items;
        }
    }

    private static unsafe FlagMapMarker? FlagCurrentZone
    {
        get
        {
            var m = AgentMap.Instance();
            if (m == null || m->IsFlagMarkerSet == 0)
                return null;

            if (m->FlagMapMarker.TerritoryId != Service.ClientState.TerritoryType)
                return null;

            return m->FlagMapMarker;
        }
    }

    public override bool DrawConditions()
    {
        return QuestObjectivesCurrentZone.Any()
            || WaypointsCurrentZone.Any()
            || FatesCurrentZone.Length > 0
            || zod.Active
            || Service.ClientState.TerritoryType is BozjaZone or ZadnorZone;
    }

    public override unsafe void Draw()
    {
        var tt = Service.ClientState.TerritoryType;
        if (PosDebug)
        {
            if (Service.Player is null)
                return;
            var playerPos = Service.Player.Position;
            ImGui.Text($"Pos: {playerPos}");
            ImGui.Text($"Floor (2y radius): {Service.IPC.PointOnFloor(playerPos, false, 2f)}"); ;
            ImGui.Text($"Floor (2y radius, unlandable): {Service.IPC.PointOnFloor(playerPos, false, 2f)}");
            playerPos.Y += 15f;
            ImGui.Text($"Floor (2y radius, 15y vertical): {Service.IPC.PointOnFloor(playerPos, false, 2f)}");
        }

        if (FlagCurrentZone is { } flag && ImGui.TreeNodeEx("Flag", ImGuiTreeNodeFlags.DefaultOpen))
        {
            var flagpos = MapUtil.WorldToMap(new Vector2(flag.XFloat, flag.YFloat));
            Utils.Icon(60561, new(32, 32));
            ImGui.Text($"{flagpos.X:0.0}, {flagpos.Y:0.0}");
            ImGui.SameLine();
            if (Service.IPC.PointOnFloor(new(flag.XFloat, 1024, flag.YFloat), true, 5) is { } walkable)
                DrawGoButtons("###flag", () => Destination.FromPoint(walkable));
            else
                ImGui.TextDisabled("(not walkable)");
            ImGui.TreePop();
        }

        if (QuestObjectivesCurrentZone.Any() && ImGui.TreeNodeEx("Quests", ImGuiTreeNodeFlags.DefaultOpen))
        {
            foreach (var objective in QuestObjectivesCurrentZone)
            {
                if (!IconRemap.TryGetValue(objective.IconId, out var iconId))
                    iconId = objective.IconId;
                Utils.Icon(iconId, new(32, 32));
                ImGui.Text($"({objective.IconId}) ({objective.ObjectiveId}) {objective.TooltipString->ToString()}");
                ImGui.SameLine();
                ImGui.TextDisabled($"{Vector3.Distance(objective.Pos(), Service.Player!.Position):F1}y");
                ImGui.SameLine();
                DrawGoButtons($"###quest{objective.LevelId}", () => GetPoint(objective.TooltipString->ToString(), new Vector3(objective.X, objective.Y, objective.Z)));
            }
            ImGui.TreePop();
        }

        if (tt is BozjaZone or ZadnorZone && ImGui.TreeNodeEx("Aetherytes", ImGuiTreeNodeFlags.DefaultOpen))
        {
            var aetherytes = tt == BozjaZone ? bozjaAetherytes : zadnorAetherytes;
            foreach (var ae in aetherytes)
            {
                Utils.Icon(60959, new(32, 32));
                ImGui.Text(ae.Name);
                ImGui.SameLine();
                DrawGoButtons($"###ae{ae.Name}", () => Destination.FromPoint(ae.Position));
            }
            ImGui.TreePop();
        }

        if (WaypointsCurrentZone.Any() && ImGui.TreeNodeEx("Waypoints", ImGuiTreeNodeFlags.DefaultOpen))
        {
            foreach (var wp in WaypointsCurrentZone)
            {
                ImGui.Text($"{wp.Label}");
                ImGui.SameLine();
                ImGui.TextDisabled($"{Vector3.Distance(wp.Position, Service.Player!.Position):F1}y");
                ImGui.SameLine();
                DrawGoButtons($"###wp{wp.Label}", () => Destination.FromPoint(wp.Position));
            }
            ImGui.TreePop();
        }

        if (FatesCurrentZone.Length > 0 && ImGui.TreeNodeEx("FATEs", ImGuiTreeNodeFlags.DefaultOpen))
        {
            var dt = DateTime.Now;
            foreach (var fate in FatesCurrentZone)
            {
                var fateDuration = new TimeSpan(0, 0, fate.Value->Duration);
                var fateStart = DateTimeOffset.FromUnixTimeSeconds(fate.Value->StartTimeEpoch);
                var fateTimeRemaining = fateDuration - (dt - fateStart);
                Utils.Icon(fate.Value->MapIconId, new(32, 32));
                ImGui.Text($"{fate.Value->Name} ({fate.Value->Progress}%%, {fateTimeRemaining:mm\\:ss})");
                ImGui.SameLine();
                ImGui.TextDisabled($"{Vector3.Distance(fate.Value->Location, Service.Player!.Position):F1}y");
                ImGui.SameLine();
                DrawGoButtons($"###fate{fate.Value->FateId}", () => GetPoint(fate.Value));
            }
            ImGui.TreePop();
        }

        if (zod.Active && ImGui.TreeNodeEx("Zodiac", ImGuiTreeNodeFlags.DefaultOpen))
        {
            zod.Draw();
            ImGui.TreePop();
        }
    }

    private unsafe bool CanFlyCurrentZone()
    {
        var tt = Service.ClientState.TerritoryType;
        var ps = PlayerState.Instance();
        return ps != null &&
            _territoryToAetherCurrentCompFlgSet.TryGetValue(tt, out byte accfs) &&
            ps->IsAetherCurrentZoneComplete(accfs);
    }

    private static float RawPosToMapCoordinate(int pos, float scale, short offset)
    {
        float num = scale / 100f;
        float num2 = (float)pos / 1000f;
        float num3 = (num2 + (float)offset) * num;
        return 41f / num * ((num3 + 1024f) / 2048f) + 1f;
    }

    private static unsafe Destination GetPoint(FateContext* fate) => GetPoint(fate->Name.ToString(), fate->Location, 15f, fate->Radius / 3);

    private static unsafe Destination GetPoint(string hint, Vector3 worldPos, float extraHeight = 30f, float extraRadius = 0f)
    {
        var originalPos = new Vector3(worldPos.X, worldPos.Y, worldPos.Z);
        var target = Service.IPC.PointOnFloor(worldPos, false, 2f);
        Service.Log.Info($"Point on floor @ {worldPos}, 2y: {target}");
        if (target == null && extraHeight > 0)
        {
            worldPos.Y += extraHeight;
            target = Service.IPC.PointOnFloor(worldPos, false, 2f);
            Service.Log.Info($"Point on floor @ {worldPos}, 2y: {target}");
        }

        if (target == null && extraRadius > 0)
        {
            target = Service.IPC.PointOnFloor(worldPos, false, extraRadius);
            Service.Log.Info($"Point on floor @ {worldPos}, {extraRadius}y: {target}");
        }

        if (target == null)
            Service.Log.Error($"No point on floor near {worldPos} with radius {Math.Max(extraRadius, 2f)}");

        return new()
        {
            Reported = originalPos,
            Actual = target
        };
    }

    public void DrawGoButtons(string label, Func<Destination> destination)
    {
        if (ImGuiComponents.IconButton(label, FontAwesomeIcon.Walking))
        {
            var d = destination();
            if (d.Actual != null)
                Service.Plugin.Goto(d.Actual.Value);
        }

        if (CanFlyCurrentZone())
        {
            ImGui.SameLine();

            if (ImGuiComponents.IconButton($"{label}fly", FontAwesomeIcon.Plane))
            {
                var d = destination();
                if (d.Actual != null)
                    Service.Plugin.Goto(d.Actual.Value, true);
            }
        }

        ImGui.SameLine();

        if (ImGuiComponents.IconButton($"{label}help", FontAwesomeIcon.InfoCircle))
        {
            var d = destination();
            Service.Plugin.HighlightDestination(d.Reported);
        }
    }

    public class Destination
    {
        public Vector3 Reported;
        public Vector3? Actual;

        public static Destination FromPoint(Vector3 point) => new()
        {
            Reported = point,
            Actual = point
        };
    }
}

internal static class MapMarkerExtensions
{
    public static Vector3 Pos(this MapMarkerData data) => new(data.X, data.Y, data.Z);
}
