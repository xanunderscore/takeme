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
using Lumina.Excel.Sheets;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Numerics;
using FieldMarker = FFXIVClientStructs.FFXIV.Client.Game.UI.FieldMarker;

namespace TakeMe;

public unsafe class Overlay : Window
{
    public class Destination
    {
        public Vector3 Reported;
        public Vector3? Actual;
        public uint FateId = 0;

        public static Destination FromPoint(Vector3 point) => new()
        {
            Reported = point,
            Actual = point
        };
    }

    private readonly Zodiac zod;
    private readonly ReadOnlyDictionary<ushort, uint> _territoryToAetherCurrentCompFlgSet;
    public bool PosDebug { get; set; }

    private static IPlayerCharacter Player => Service.Player!;
    private static Vector3 PlayerPos => Player.Position;
    private static Vector2 PlayerPosXZ => new(PlayerPos.X, PlayerPos.Z);

    private FlagMapMarker? Flag;
    private readonly List<Waypoint> SavedWaypoints = [];
    private readonly List<Waypoint> SavedAetherytes = [];
    private readonly List<MapMarkerData> MapMarkers = [];
    private readonly List<Pointer<FateContext>> Fates = [];
    private readonly FieldMarker[] Waymarks = new FieldMarker[8];
    private readonly List<MiniMapGatheringMarker> GatherMarkers = [];

    private bool ShouldDraw;

    public Overlay()
        : base("TakeMe Overlay")
    {
        zod = new();
        _territoryToAetherCurrentCompFlgSet = Service.Data.GetExcelSheet<TerritoryType>()!
            .Where(x => x.AetherCurrentCompFlgSet.RowId >= 0)
            .ToDictionary(x => (ushort)x.RowId, x => x.AetherCurrentCompFlgSet.RowId)
            .AsReadOnly();
        IsOpen = true;
    }

    public override bool DrawConditions() => ShouldDraw;

    private static readonly Dictionary<ushort, List<(string Name, Vector3 Position)>> aetheryteInstances = new()
    {
        [795] = [
            ("Northpoint", new(-250.87f, 680.76f, 150.31f)),
            ("The Dragon Star Observatory", new(127.90f, 753.61f, 792.42f)),
            ("The Firing Chamber", new(126.28f, 660.15f, -199.00f)),
            ("Carbonatite Quarry", new(-440.02f, 671.15f, -620.51f))
        ],
        [827] = [
            ("Central Point", new Vector3(-61.30f, 523.22f, -872.90f)),
            ("Unverified Research", new Vector3(-585.81f, 505.51f, -151.93f)),
            ("The Dormitory", new Vector3(778.22f, 512.20f, -418.36f))
        ],
        [920] = [
            ("Utya's Aegis", new(-201.95f, 5.02f, 846.95f)),
            ("Olana's Stand", new(486.78f, 34.93f, 531.33f)),
            ("Lunya's Stand", new(-257.95f, 35.93f, 534.36f)),
            ("Camp Steva", new(169.79f, 2.95f, 192.28f))
        ],
        [975] = [
            ("Camp Vrdelnis", new(679.68f, 297.29f, 660.03f)),
            ("Zuprtik Point", new(-356.47f, 286.03f, 758.45f)),
            ("Ljeban Point", new(-689.39f, 276.54f, -292.16f)),
            ("Hrmovir Point", new(106.37f, 300.95f, -130.82f))
        ]
    };

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
        71006, // icon placed on npc that gives you a required buff for a quest step
        63921, // sidequest icon shaped like a book? idk what this is
        60935, // firmament vendor icon

        60970, // eureka stuff
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

    public override void PreOpenCheck()
    {
        ShouldDraw = false;
        if (Service.Player == null)
            return;

        MapMarkers.Clear();
        foreach (var marker in AgentHUD.Instance()->MapMarkers)
            if (ImportantQuestIcons.Contains(marker.IconId))
                MapMarkers.Add(marker);
        ShouldDraw |= MapMarkers.Count > 0;

        SavedWaypoints.Clear();
        SavedWaypoints.AddRange(Service.Config.Waypoints.Where(x => x.Zone == Service.ClientState.TerritoryType));
        ShouldDraw |= SavedWaypoints.Count > 0;

        SavedAetherytes.Clear();
        SavedAetherytes.AddRange(Service.Config.Aetherytes.Where(x => x.Zone == Service.ClientState.TerritoryType));
        ShouldDraw |= SavedAetherytes.Count > 0;

        Flag = null;
        var map = AgentMap.Instance();
        if (map != null && map->IsFlagMarkerSet && map->FlagMapMarker.TerritoryId == Service.ClientState.TerritoryType)
        {
            Flag = map->FlagMapMarker;
            ShouldDraw = true;
        }

        Fates.Clear();
        Fates.AddRange(FateManager.Instance()->Fates.AsEnumerable());
        ShouldDraw |= Fates.Count > 0;

        // not setting ShouldDraw here as people usually set markers in raids and we don't want the overlay there
        MarkingController.Instance()->FieldMarkers.CopyTo(Waymarks.AsSpan());

        GatherMarkers.Clear();
        foreach (var g in AgentMap.Instance()->MiniMapGatheringMarkers)
            if (g.ShouldRender > 0)
                GatherMarkers.Add(g);
        ShouldDraw |= GatherMarkers.Count > 0;

        ShouldDraw |= aetheryteInstances.ContainsKey(Service.ClientState.TerritoryType);
    }

    public override void Draw()
    {
        if (Service.Player is null)
            return;

        var tt = Service.ClientState.TerritoryType;
        if (PosDebug)
        {
            ImGui.Text($"Pos: {PlayerPos}");
            ImGui.Text($"Floor (2y radius): {Service.IPC.PointOnFloor(PlayerPos, false, 2f)}"); ;
            ImGui.Text($"Floor (2y radius, unlandable): {Service.IPC.PointOnFloor(PlayerPos, true, 2f)}");
            var p = PlayerPos;
            p.Y += 15f;
            ImGui.Text($"Floor (2y radius, 15y vertical): {Service.IPC.PointOnFloor(p, false, 2f)}");
        }

        if (Flag != null)
            DrawFlag(Flag.Value);

        DrawSection("Gathering markers", GatherMarkers, mark =>
        {
            Utils.Icon(mark.MapMarker.IconId, new(32, 32));
            ImGui.Text($"{mark.TooltipText}");
        }, ImGuiTreeNodeFlags.DefaultOpen);

        DrawSection("Quests", MapMarkers.OrderBy(q => DistanceFromPlayer(q.Pos())), DrawQuest, ImGuiTreeNodeFlags.DefaultOpen);

        var aetherytesHere = aetheryteInstances.TryGetValue(tt, out var i) ? i : Enumerable.Empty<(string Name, Vector3 Position)>();

        DrawSection("Aetherytes", aetherytesHere, ae =>
        {
            Utils.Icon(60959, new(32, 32));
            ImGui.Text(ae.Name);
            ImGui.SameLine();
            DrawDistanceFromPlayer(ae.Position);
            DrawGoButtons($"###ae{ae.Name}", () => Destination.FromPoint(ae.Position));
        }, ImGuiTreeNodeFlags.DefaultOpen);

        DrawSection("Waypoints", SavedWaypoints, wp =>
        {
            if (wp.Icon > 0)
                Utils.Icon(wp.Icon, new(32, 32));

            ImGui.Text(wp.Label);
            ImGui.SameLine();
            DrawDistanceFromPlayer(wp.Position);
            DrawGoButtons($"###wp{wp.Label}", () => Destination.FromPoint(wp.Position));
        }, ImGuiTreeNodeFlags.DefaultOpen);

        DrawSection("FATEs", Fates.ToArray().OrderBy(x => DistanceFromPlayer(x.Value->Location)), DrawFATE, ImGuiTreeNodeFlags.DefaultOpen);

        DrawSection("Aethernet", SavedAetherytes.OrderBy(x => x.SortOrder), wp =>
        {
            if (wp.Icon > 0)
                Utils.Icon(wp.Icon, new(32, 32));

            ImGui.Text(wp.Label);
            ImGui.SameLine();
            DrawDistanceFromPlayer(wp.Position);
            DrawGoButtons($"###aetheryte{wp.Label}", () => Destination.FromPoint(wp.Position));
        });

        if (zod.Active && ImGui.TreeNodeEx("Zodiac", ImGuiTreeNodeFlags.DefaultOpen))
        {
            zod.Draw();
            ImGui.TreePop();
        }

        DrawSection("Markers", Waymarks.Zip(WaymarkIcons).Where(t => t.First.Active), f =>
        {
            Utils.Icon(f.Second, new(32, 32));
            ImGui.SameLine();
            var pos = new Vector3(f.First.X / 1000f, f.First.Y / 1000f, f.First.Z / 1000f);
            DrawDistanceFromPlayer(pos);
            DrawGoButtons($"###waymark{f.Second}", () => Destination.FromPoint(pos));
        }, ImGuiTreeNodeFlags.DefaultOpen);

        if (ImGui.Button("Copy location"))
        {
            var pos = Player.Position;
            ImGui.SetClipboardText($"new Vector3({pos.X:f2}f, {pos.Y:f2}f, {pos.Z:f2}f)");
        }
    }

    private static readonly uint[] WaymarkIcons = [61241, 61242, 61243, 61247, 61244, 61245, 61246, 61248];

    private bool CanFlyCurrentZone()
    {
        var tt = Service.ClientState.TerritoryType;
        var ps = PlayerState.Instance();
        return ps != null &&
            _territoryToAetherCurrentCompFlgSet.TryGetValue(tt, out var accfs) &&
            ps->IsAetherCurrentZoneComplete(accfs);
    }

    private static void DrawSection<T>(string label, IEnumerable<T> items, Action<T> draw, ImGuiTreeNodeFlags flags = default)
    {
        var open = false;
        foreach (var item in items)
        {
            if (!open)
                open = ImGui.TreeNodeEx(label, flags);
            if (!open)
                break;

            draw(item);
        }
        if (open)
            ImGui.TreePop();
    }

    private static float RawPosToMapCoordinate(int pos, float scale, short offset)
    {
        var num = scale / 100f;
        var num2 = (float)pos / 1000f;
        var num3 = (num2 + (float)offset) * num;
        return 41f / num * ((num3 + 1024f) / 2048f) + 1f;
    }

    private static Destination GetPoint(FateContext* fate)
    {
        var loc = fate->Location;

        // fuck ultima thule
        if (Service.ClientState.TerritoryType == 960)
            loc.Y = 1024f;

        return GetPoint(loc, 15f, fate->Radius / 3);
    }

    private static Destination GetPoint(Vector3 worldPos, float extraHeight = 30f, float extraRadius = 0f)
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
                Service.Plugin.Goto(d.Actual.Value, false, d.FateId);
        }

        if (CanFlyCurrentZone())
        {
            ImGui.SameLine();

            if (ImGuiComponents.IconButton($"{label}fly", FontAwesomeIcon.Plane))
            {
                var d = destination();
                if (d.Actual != null)
                    Service.Plugin.Goto(d.Actual.Value, true, d.FateId);
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

    private void DrawFlag(FlagMapMarker flag)
    {
        var flagpos = new Vector2(flag.XFloat, flag.YFloat);
        var map = Service.ExcelRow<Lumina.Excel.Sheets.Map>(flag.MapId)!;
        var flagMapPos = MapUtil.WorldToMap(flagpos, map.OffsetX, map.OffsetY, map.SizeFactor);
        var yceiling = Service.ClientState.TerritoryType == 1192 ? 135f : 1024f;
        Utils.Icon(60561, new(32, 32));
        ImGui.Text($"{flagMapPos.X:0.0}, {flagMapPos.Y:0.0}");
        ImGui.SameLine();
        DrawDistanceFromPlayerXZ(flagpos);
        if (Service.IPC.PointOnFloor(new(flag.XFloat, yceiling, flag.YFloat), true, 5) is { } walkable)
        {
            DrawGoButtons("###flag", () => Destination.FromPoint(walkable));
            ImGui.SameLine();
            if (ImGuiComponents.IconButton(FontAwesomeIcon.Crosshairs))
            {
                Service.Plugin.Goto(walkable);
                AgentMap.Instance()->IsFlagMarkerSet = false;
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Goto and clear flag");
        }
        else
            ImGui.TextDisabled("(not walkable)");
    }

    private void DrawQuest(MapMarkerData objective)
    {
        if (!IconRemap.TryGetValue(objective.IconId, out var iconId))
            iconId = objective.IconId;

        Utils.Icon(iconId, new(32, 32));
        ImGui.Text($"{objective.TooltipString->ToString()} ({iconId})");
        ImGui.SameLine();
        DrawDistanceFromPlayer(objective.Pos());
        DrawGoButtons($"###quest{objective.LevelId}", () => GetPoint(new Vector3(objective.X, objective.Y, objective.Z)));
    }

    private void DrawFATE(Pointer<FateContext> fate)
    {
        var dt = DateTime.Now;
        var fateDuration = new TimeSpan(0, 0, fate.Value->Duration);
        var fateTimeRemaining = fate.Value->StartTimeEpoch == 0 ? ""
            : $", {fateDuration - (dt - DateTimeOffset.FromUnixTimeSeconds(fate.Value->StartTimeEpoch)):mm\\:ss}";
        Utils.Icon(fate.Value->MapIconId, new(32, 32));
        ImGui.Text($"Lv. {fate.Value->Level} {fate.Value->Name} ({fate.Value->Progress}%%{fateTimeRemaining})");
        ImGui.SameLine();
        DrawDistanceFromPlayer(fate.Value->Location);
        DrawGoButtons($"###fate{fate.Value->FateId}", () =>
        {
            var dest = GetPoint(fate.Value);
            dest.FateId = fate.Value->FateId;
            return dest;
        });
    }
}

internal static class MapMarkerExtensions
{
    public static Vector3 Pos(this MapMarkerData data) => new(data.X, data.Y, data.Z);
}
