using Dalamud.Plugin.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using FFXIV = FFXIVClientStructs.FFXIV.Client.Game.Object;

namespace TakeMe;

public class Treasure : IDisposable
{
    public record struct Chest(ulong Id, Vector3 Position, uint DataId);

    private readonly Dictionary<ulong, Chest> _chests = [];
    private readonly Dictionary<ulong, DateTime> _opened = [];

    public IEnumerable<Chest> Chests => _chests.Values;
    public bool HaveChests => _chests.Count > 0;

    public Treasure()
    {
        Service.Framework.Update += Tick;
        Service.ClientState.TerritoryChanged += OnTerritoryChange;
    }

    public void Dispose()
    {
        Service.Framework.Update -= Tick;
        Service.ClientState.TerritoryChanged -= OnTerritoryChange;
        GC.SuppressFinalize(this);
    }

    private void OnTerritoryChange(uint id)
    {
        _chests.Clear();
    }

    private unsafe void Tick(IFramework fw)
    {
        foreach (var item in Service.ObjectTable.Where(t => t.ObjectKind == Dalamud.Game.ClientState.Objects.Enums.ObjectKind.Treasure))
        {
            var obj = (FFXIV.Treasure*)item.Address;

            if (obj->Flags.HasFlag(FFXIV.Treasure.TreasureFlags.Opened))
                _chests.Remove(item.GameObjectId);
            else
                _chests[item.GameObjectId] = new(item.GameObjectId, item.Position, item.BaseId);
        }
    }
}
