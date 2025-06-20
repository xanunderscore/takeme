using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

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

    private void OnTerritoryChange(ushort id)
    {
        _chests.Clear();
    }

    private unsafe void Tick(IFramework fw)
    {
        foreach (var item in Service.ObjectTable.Where(t => t.ObjectKind == Dalamud.Game.ClientState.Objects.Enums.ObjectKind.Treasure))
        {
            var obj = (GameObject*)item.Address;
            var flags = *((byte*)obj + 0x1EC);

            if ((flags & 1) == 0)
                _chests[item.GameObjectId] = new(item.GameObjectId, item.Position, item.DataId);
            else
                _chests.Remove(item.GameObjectId);
        }
    }
}
