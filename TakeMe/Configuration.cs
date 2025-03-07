using Dalamud.Configuration;
using Lumina.Excel.Sheets;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Numerics;

namespace TakeMe;

public enum OverlayBehavior : uint
{
    [Description("Manually")]
    Manual = 0,
    [Description("When near aetheryte")]
    NearAetheryte = 1,
    [Description("Always")]
    Always = 2,
}

[Serializable]
public class Waypoint
{
    public required uint Zone;
    public required Vector3 Position;
    public required string Label;
    public required uint Icon;
    public required int SortOrder;
}

public static class WaypointExtensions
{
    public static TerritoryType TerritoryType(this Waypoint wp) => Service.ExcelRow<TerritoryType>(wp.Zone);
}

[Serializable]
public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 0;

    public OverlayBehavior OverlayBehavior = OverlayBehavior.Manual;
    public List<Waypoint> Waypoints = [];
    public List<Waypoint> Aetherytes = [];

    public void Save()
    {
        Service.PluginInterface.SavePluginConfig(this);
    }

    public void Update(Action<Configuration> func)
    {
        func(this);
    }
}
