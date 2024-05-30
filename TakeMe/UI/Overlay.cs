using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Windowing;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Fate;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.Interop;
using ImGuiNET;
using Lumina.Excel.GeneratedSheets;

namespace TakeMe;

public class Overlay : Window
{
    public Overlay()
        : base("TakeMe Overlay", ImGuiWindowFlags.NoCollapse)
    {
        IsOpen = true;
    }

    private static IEnumerable<Waypoint> WaypointsCurrentZone =>
        Service.Config.Waypoints.Where(x => x.Zone == Service.ClientState.TerritoryType);

    private unsafe ReadOnlySpan<Pointer<FateContext>> FatesCurrentZone => FateManager.Instance()->Fates.Span;

    private static unsafe IEnumerable<(Quest, Level)> QuestObjectivesCurrentZone
    {
        get {
            var qm = QuestManager.Instance();
            var items = new List<(Quest, Level)>();
            foreach(var q in qm->NormalQuestsSpan)
            {
                if (q.QuestId == 0)
                    continue;
                var quest = Service.ExcelRow<Quest>(q.QuestId | 0x10000u);
                if (quest is null)
                {
                    Service.Log.Debug($"unrecognized quest {q.QuestId}");
                    continue;
                }
                var todos = quest.Unknown1221;
                var progress = QuestManager.GetQuestSequence(q.QuestId);
                var seqnum = Array.IndexOf(quest.ToDoCompleteSeq, progress);
                if (seqnum < 0)
                {
                    Service.Log.Debug($"unrecognized sequence number {progress} for quest {q.QuestId}");
                    continue;
                }
                var questNextObjective = Service.ExcelRow<Level>(todos[seqnum]);
                if (questNextObjective != null && questNextObjective.Territory.Row == Service.ClientState.TerritoryType)
                    items.Add((quest, questNextObjective));
            }

            return items;
        }
    }

    public override bool DrawConditions()
    {
        return QuestObjectivesCurrentZone.Any() || WaypointsCurrentZone.Any() || FatesCurrentZone.Length > 0;
    }

    public override unsafe void Draw()
    {
        if (QuestObjectivesCurrentZone.Any())
        {
            ImGui.Text("Quests");
            foreach((var quest, var objective) in QuestObjectivesCurrentZone)
            {
                ImGui.Text(quest.Name);
                ImGui.SameLine();
                DrawGoButtons($"###quest{quest.RowId}", () => GetPoint(new Vector3(objective.X, objective.Y, objective.Z)));
            }
        }

        if (WaypointsCurrentZone.Any())
        {
            ImGui.Text("Waypoints");
            foreach (var wp in WaypointsCurrentZone)
            {
                ImGui.Text($"{wp.Label}");
                ImGui.SameLine();
                DrawGoButtons($"###wp{wp.Label}", () => wp.Position);
            }
        }

        if (FatesCurrentZone.Length > 0)
        {
            ImGui.Text("FATEs");
            foreach (var fate in FatesCurrentZone)
            {
                ImGui.Text($"{fate.Value->Name} {fate.Value->Progress}%%");
                ImGui.SameLine();
                DrawGoButtons($"###fate{fate.Value->FateId}", () => GetPoint(fate.Value));
            }
        }
    }

    private static unsafe Vector3? GetPoint(FateContext* fate) => GetPoint(fate->Location, 15f, fate->Radius / 3);

    private static unsafe Vector3? GetPoint(Vector3 worldPos, float extraHeight = 15f, float extraRadius = 0f)
    {
        var target = Service.IPC.PointOnFloor(worldPos, false, 2f);
        if (target == null && extraHeight > 0)
        {
            worldPos.Y += extraHeight;
            target = Service.IPC.PointOnFloor(worldPos, false, 2f);
        }

        if (target == null && extraRadius > 0)
            target = Service.IPC.PointOnFloor(worldPos, false, extraRadius);

        return target;
    }

    private static void Goto(Vector3 destination, bool forceFly = false)
    {
        Service.Log.Debug($"pathfind from {Service.Player!.Position} -> {destination}");
        Service
            .IPC
            .PathfindAndMoveTo(
                destination,
                Service.Condition[ConditionFlag.InFlight] || Service.Condition[ConditionFlag.Jumping] || forceFly
            );
    }

    public static void DrawGoButtons(string label, Func<Vector3?> destination)
    {
        if (ImGuiComponents.IconButton(label, FontAwesomeIcon.Walking))
        {
            var d = destination();
            if (d is null)
                Service.Toast.ShowError("Unable to find walkable point near destination");
            else
                Goto(d.Value);
        }

        ImGui.SameLine();

        if (ImGuiComponents.IconButton($"{label}fly", FontAwesomeIcon.Plane))
        {
            var d = destination();
            if (d is null)
                Service.Toast.ShowError("Unable to find walkable point near destination");
            else
                Goto(d.Value, true);
        }
    }
}
