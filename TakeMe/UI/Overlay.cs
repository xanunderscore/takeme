using Dalamud.Game.ClientState.Objects.SubKinds;
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

    private readonly Zodiac zod;
    private readonly ReadOnlyDictionary<ushort, byte> _territoryToAetherCurrentCompFlgSet;
    public bool PosDebug { get; set; }

    private const ushort BozjaZone = 920;
    private const ushort ZadnorZone = 975;

    private static IPlayerCharacter Player => Service.Player!;
    private static Vector3 PlayerPos => Player.Position;
    private static Vector2 PlayerPosXZ => new(PlayerPos.X, PlayerPos.Z);

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

    private static readonly List<(string Name, Vector3 Position)> bozjaAetherytes = [
        ("Utya's Aegis", new(-201.95f, 5.02f, 846.95f)),
        ("Olana's Stand", new(486.78f, 34.93f, 531.33f)),
        ("Lunya's Stand", new(-257.95f, 35.93f, 534.36f)),
        ("Camp Steva", new(169.79f, 2.95f, 192.28f))
    ];

    private static readonly List<(string Name, Vector3 Position)> zadnorAetherytes = [
        ("Camp Vrdelnis", new(679.68f, 297.29f, 660.03f)),
        ("Zuprtik Point", new(-356.47f, 286.03f, 758.45f)),
        ("Ljeban Point", new(-689.39f, 276.54f, -292.16f)),
        ("Hrmovir Point", new(106.37f, 300.95f, -130.82f))
    ];

    private static IEnumerable<Waypoint> WaypointsCurrentZone =>
        Service.Config.Waypoints.Where(x => x.Zone == Service.ClientState.TerritoryType);

    private static IEnumerable<Waypoint> AetherytesCurrentZone =>
        Service.Config.Aetherytes.Where(x => x.Zone == Service.ClientState.TerritoryType);

    private unsafe Span<Pointer<FateContext>> FatesCurrentZone => FateManager.Instance()->Fates.AsSpan();

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
        60458, 60493, 60501, 60502, 60503, 60504, 60505, 60512, 60934, // fates
        60801, 60802, // bozja fates
        // 71021, // sidequest
        70965, // sidequest in other zone
        71151, // feature quest, locked
        71022, // repeatable sidequest
        71031, // sidequest, locked
        71032, // daily feature quest, locked
        71142, // repeatable feature quest
        71152, // repeatable feature quest, locked
        70974, // repeatable feature quest, locked, different zone
        71121, // hall of novices
        71041, // leve
        71044, // leve eventobj
        71081, // guildhest
        63922, // moogle quest
        63933, // speech bubble
        71006, // icon placed on npc that gives you a required status effect for a quest
        63921, // sidequest icon shaped like a book? idk what this is
        60935, // firmament vendor icon
        60959, 63913, 60412, 63909, 60489, // bozja stuff
        60758, 60769, 60770, 60771, 60772, 60773, 60774, 60776, 60789 // housing
    ];

    // mapping of transparent "area of interest" icons to the quest type they correspond with
    private static readonly Dictionary<uint, uint> IconRemap = new() {
        // msq
        { 60490u, 71003u },
        // leve
        { 60492u, 61422u },
        // sidequest
        { 60494u, 71143u },
        { 60491u, 71023u }
    };

    private static unsafe IEnumerable<MapMarkerData> QuestObjectivesCurrentZone
    {
        get
        {
            var items = new List<MapMarkerData>();
            var hd = AgentHUD.Instance();

            // Null pointer exception on this line
            foreach (var d in hd->MapMarkers.AsSpan())
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

    private static IEnumerable<MiniMapGatheringMarker> GatheringMarkers
    {
        get
        {
            static unsafe MiniMapGatheringMarker[] g() { return AgentMap.Instance()->MiniMapGatheringMarkers.ToArray(); }

            return g().Where(m => m.ShouldRender > 0);
        }
    }

    public override bool DrawConditions()
    {
        if (Service.Player is null)
            return false;

        return QuestObjectivesCurrentZone.Any()
            || WaypointsCurrentZone.Any()
            || AetherytesCurrentZone.Any()
            || FatesCurrentZone.Length > 0
            || zod.Active
            || FlagCurrentZone != null
            || GatheringMarkers.Any()
            || Service.ClientState.TerritoryType is BozjaZone or ZadnorZone;
    }

    public override unsafe void Draw()
    {
        if (Service.Player is null)
            return;

        var tt = Service.ClientState.TerritoryType;
        if (PosDebug)
        {
            ImGui.Text($"Pos: {PlayerPos}");
            ImGui.Text($"Floor (2y radius): {Service.IPC.PointOnFloor(PlayerPos, false, 2f)}"); ;
            ImGui.Text($"Floor (2y radius, unlandable): {Service.IPC.PointOnFloor(PlayerPos, false, 2f)}");
            var p = PlayerPos;
            p.Y += 15f;
            ImGui.Text($"Floor (2y radius, 15y vertical): {Service.IPC.PointOnFloor(p, false, 2f)}");
        }

        if (FlagCurrentZone is { } flag)
        {
            var flagpos = new Vector2(flag.XFloat, flag.YFloat);
            var map = Service.ExcelRow<Lumina.Excel.GeneratedSheets.Map>(flag.MapId)!;
            var flagMapPos = MapUtil.WorldToMap(flagpos, map.OffsetX, map.OffsetY, map.SizeFactor);
            var yceiling = Service.ClientState.TerritoryType == 1192 ? 135f : 1024f;
            Utils.Icon(60561, new(32, 32));
            ImGui.Text($"{flagMapPos.X:0.0}, {flagMapPos.Y:0.0}");
            ImGui.SameLine();
            DrawDistanceFromPlayerXZ(flagpos);
            if (Service.IPC.PointOnFloor(new(flag.XFloat, yceiling, flag.YFloat), true, 5) is { } walkable)
                DrawGoButtons("###flag", () => Destination.FromPoint(walkable));
            else
                ImGui.TextDisabled("(not walkable)");
        }

        if (GatheringMarkers.Any() && ImGui.TreeNodeEx("Gathering markers", ImGuiTreeNodeFlags.DefaultOpen))
        {
            foreach (var mark in GatheringMarkers)
            {
                Utils.Icon(mark.MapMarker.IconId, new(32, 32));
                ImGui.Text($"{mark.TooltipText}");
            }

            ImGui.TreePop();
        }

        if (QuestObjectivesCurrentZone.Any() && ImGui.TreeNodeEx("Quests", ImGuiTreeNodeFlags.DefaultOpen))
        {
            foreach (var objective in QuestObjectivesCurrentZone.OrderBy(q => DistanceFromPlayer(q.Pos())))
            {
                if (!IconRemap.TryGetValue(objective.IconId, out var iconId))
                    iconId = objective.IconId;
                Utils.Icon(iconId, new(32, 32));
                ImGui.Text(objective.TooltipString->ToString());
                ImGui.SameLine();
                DrawDistanceFromPlayer(objective.Pos());
                DrawGoButtons($"###quest{objective.LevelId}", () => GetPoint(new Vector3(objective.X, objective.Y, objective.Z)));
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
                DrawDistanceFromPlayer(ae.Position);
                DrawGoButtons($"###ae{ae.Name}", () => Destination.FromPoint(ae.Position));
            }
            ImGui.TreePop();
        }

        if (WaypointsCurrentZone.Any() && ImGui.TreeNodeEx("Waypoints", ImGuiTreeNodeFlags.DefaultOpen))
        {
            foreach (var wp in WaypointsCurrentZone)
            {
                if (wp.Icon > 0)
                    Utils.Icon(wp.Icon, new(32, 32));

                ImGui.Text(wp.Label);
                ImGui.SameLine();
                DrawDistanceFromPlayer(wp.Position);
                DrawGoButtons($"###wp{wp.Label}", () => Destination.FromPoint(wp.Position));
            }
            ImGui.TreePop();
        }

        if (FatesCurrentZone.Length > 0 && ImGui.TreeNodeEx("FATEs", ImGuiTreeNodeFlags.DefaultOpen))
        {
            var dt = DateTime.Now;
            var fates = FatesCurrentZone.ToArray();
            foreach (var fate in fates.OrderBy(x => DistanceFromPlayer(x.Value->Location)))
            {
                var fateDuration = new TimeSpan(0, 0, fate.Value->Duration);
                var fateTimeRemaining = fate.Value->StartTimeEpoch == 0 ? ""
                    : $", {fateDuration - (dt - DateTimeOffset.FromUnixTimeSeconds(fate.Value->StartTimeEpoch)):mm\\:ss}";
                // FIXME MapIconId
                Utils.Icon(*(uint*)((IntPtr)fate.Value + 988), new(32, 32));
                ImGui.Text($"Lv. {fate.Value->Level} {fate.Value->Name} ({fate.Value->Progress}%%{fateTimeRemaining})");
                ImGui.SameLine();
                DrawDistanceFromPlayer(fate.Value->Location);
                DrawGoButtons($"###fate{fate.Value->FateId}", () => GetPoint(fate.Value));
            }
            ImGui.TreePop();
        }

        if (AetherytesCurrentZone.Any() && ImGui.TreeNodeEx("Aetherytes"))
        {
            foreach (var wp in AetherytesCurrentZone.OrderBy(x => x.SortOrder))
            {
                if (wp.Icon > 0)
                    Utils.Icon(wp.Icon, new(32, 32));

                ImGui.Text(wp.Label);
                ImGui.SameLine();
                DrawDistanceFromPlayer(wp.Position);
                DrawGoButtons($"###aetheryte{wp.Label}", () => Destination.FromPoint(wp.Position));
            }
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

    private static unsafe Destination GetPoint(FateContext* fate)
    {
        var loc = fate->Location;

        // fuck ultima thule
        if (Service.ClientState.TerritoryType == 960)
            loc.Y = 1024f;

        return GetPoint(loc, 15f, fate->Radius / 3);
    }

    private static unsafe Destination GetPoint(Vector3 worldPos, float extraHeight = 30f, float extraRadius = 0f)
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

    private static float DistanceFromPlayer(Vector3 point) => Vector3.Distance(point, PlayerPos);
    private static float DistanceFromPlayerXZ(Vector2 point) => Vector2.Distance(point, PlayerPosXZ);

    private static void DrawDistanceFromPlayer(Vector3 point)
    {
        ImGui.TextDisabled($"{DistanceFromPlayer(point):F1}y");
        ImGui.SameLine();
    }

    private static void DrawDistanceFromPlayerXZ(Vector2 point)
    {
        ImGui.TextDisabled($"{DistanceFromPlayerXZ(point):F1}y");
        ImGui.SameLine();
    }
}

internal static class MapMarkerExtensions
{
    public static Vector3 Pos(this MapMarkerData data) => new(data.X, data.Y, data.Z);
}
