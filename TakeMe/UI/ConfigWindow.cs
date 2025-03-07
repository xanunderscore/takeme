using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Windowing;
using ImGuiNET;
using Lumina.Excel.Sheets;
using System;
using System.ComponentModel;
using System.Linq;
using System.Numerics;
using System.Reflection;

namespace TakeMe;

public class ConfigWindow : Window
{
    public ConfigWindow()
        : base("TakeMe Config", ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoScrollbar)
    {
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(300, 200)
        };
    }

    public unsafe override void Draw()
    {
        if (ImGui.BeginTabBar("config_tabs"))
        {
            if (ImGui.BeginTabItem("Waypoints"))
            {
                DrawWaypoints();
                ImGui.EndTabItem();
            }
            if (ImGui.BeginTabItem("Aetherytes"))
            {
                DrawAetherytes();
                ImGui.EndTabItem();
            }
            if (ImGui.BeginTabItem("Settings"))
            {
                ImGui.Text("Show overlay:");
                ImGui.SameLine();
                ImGui.SetNextItemWidth(200);
                var beh = Service.Config.OverlayBehavior;
                if (ImGui.BeginCombo("###overlaybehavior", EnumString(beh)))
                {
                    foreach (var item in typeof(OverlayBehavior).GetEnumValues())
                    {
                        if (ImGui.Selectable(EnumString((OverlayBehavior)item), item.Equals(beh)))
                        {
                            Service.Config.OverlayBehavior = (OverlayBehavior)item;
                        }
                    }
                    ImGui.EndCombo();
                }
                ImGui.EndTabItem();
            }
            ImGui.EndTabBar();
        }
    }

    private static void DrawWaypoints()
    {
        if (ImGuiComponents.IconButton(FontAwesomeIcon.UserPlus))
        {
            if (Service.Player != null)
            {
                var zone = Service.ClientState.TerritoryType;
                var label = $"Untitled {Service.Config.Waypoints.Count}";
                var pos = Service.Player.Position;
                Service.Config.Waypoints.Add(new Waypoint
                {
                    Zone = zone,
                    Position = pos,
                    Label = label,
                    Icon = 0,
                    SortOrder = -1
                });
            }
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Add waypoint: current position");

        ImGui.BeginTable("waypoints", 4, ImGuiTableFlags.BordersInnerV | ImGuiTableFlags.Sortable);
        ImGui.TableSetupColumn("Label", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("Zone", ImGuiTableColumnFlags.WidthFixed, 150);
        ImGui.TableSetupColumn("Location", ImGuiTableColumnFlags.WidthFixed, 350);
        ImGui.TableSetupColumn("###controls", ImGuiTableColumnFlags.WidthFixed, 70);
        ImGui.TableHeadersRow();

        var i = 0;
        foreach (var wp in Service.Config.Waypoints)
        {
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            var label = wp.Label;
            ImGui.SetNextItemWidth(-1);
            if (ImGui.InputText($"###label{i}", ref label, 255))
            {
                Service.Config.Waypoints[i].Label = label;
            }

            ImGui.TableNextColumn();
            ImGui.TextUnformatted(wp.TerritoryType().PlaceName.Value.Name.ExtractText());

            ImGui.TableNextColumn();
            var pos = wp.Position;
            ImGui.SetNextItemWidth(-1);
            if (ImGui.InputFloat3($"###pos{i}", ref pos))
                Service.Log.Debug($"{pos}");

            var ctrl = ImGui.GetIO().KeyCtrl;
            ImGui.TableNextColumn();

            if (ImGuiComponents.IconButton($"###goto{i}", FontAwesomeIcon.Play))
                Service.Plugin.MoveWaypoint(wp);

            ImGui.SameLine();
            if (!ctrl) ImGui.BeginDisabled();
            if (ImGuiComponents.IconButton($"###delete{i}", FontAwesomeIcon.Trash))
            {
                Service.Config.Waypoints.RemoveAt(i);
                break; // breaks iteration otherwise
            }
            if (!ctrl) ImGui.EndDisabled();

            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            {
                ImGui.SetTooltip("Delete waypoint (hold CTRL)");
            }

            i++;
        }

        ImGui.EndTable();
    }

    private static void DrawAetherytes()
    {
        foreach (var aetheryteObj in Service.ObjectTable.Where(x => x.ObjectKind == Dalamud.Game.ClientState.Objects.Enums.ObjectKind.Aetheryte))
        {
            var aetheryte = Service.Data.GetExcelSheet<Aetheryte>().GetRow(aetheryteObj.DataId);

            if (Service.Config.Aetherytes.Any(x => x.Position == aetheryteObj.Position && x.Zone == Service.ClientState.TerritoryType))
                continue;

            var isMaster = aetheryte.PlaceName.RowId > 0;

            ImGui.Text($"{aetheryte.AethernetName.Value!.Name}");
            ImGui.SameLine();
            if (ImGuiComponents.IconButton($"###save{aetheryteObj.GameObjectId}", FontAwesomeIcon.Save))
            {
                Service.Config.Aetherytes.Add(new Waypoint
                {
                    Zone = Service.ClientState.TerritoryType,
                    Position = aetheryteObj.Position,
                    Label = aetheryte.AethernetName.Value.Name.ExtractText(),
                    Icon = isMaster ? 60453u : 60430u,
                    SortOrder = (int)aetheryteObj.DataId
                });
            }
        }
    }

    private static string EnumString<T>(T v) where T : Enum =>
        v.GetType().GetField(v.ToString())?.GetCustomAttribute<DescriptionAttribute>()?.Description ?? v.ToString();
}
