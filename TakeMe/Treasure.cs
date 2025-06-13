using Dalamud.Hooking;
using Dalamud.Plugin.Services;
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

    private unsafe delegate void ProcessPacketOpenTreasureDelegate(uint actorID, byte* packet);
    private readonly Hook<ProcessPacketOpenTreasureDelegate> _processPacketOpenTreasureHook;

    public unsafe Treasure()
    {
        Service.Framework.Update += Tick;
        Service.ClientState.TerritoryChanged += OnTerritoryChange;
        _processPacketOpenTreasureHook = Service.Hook.HookFromSignature<ProcessPacketOpenTreasureDelegate>("40 53 48 83 EC 20 48 8B DA 48 8D 0D ?? ?? ?? ?? 8B 52 10 E8 ?? ?? ?? ?? 48 85 C0 74 1B", ProcessPacketOpenTreasureDetour);
        _processPacketOpenTreasureHook.Enable();
    }

    public void Dispose()
    {
        Service.Framework.Update -= Tick;
        Service.ClientState.TerritoryChanged -= OnTerritoryChange;
        _processPacketOpenTreasureHook.Dispose();
        GC.SuppressFinalize(this);
    }

    private void OnTerritoryChange(ushort id)
    {
        _chests.Clear();
    }

    private unsafe void ProcessPacketOpenTreasureDetour(uint playerID, byte* packet)
    {
        _processPacketOpenTreasureHook.Original(playerID, packet);
        var actorID = *(uint*)(packet + 16);
        _opened.TryAdd(actorID, DateTime.Now);
        _chests.Remove(actorID);
    }

    private void Tick(IFramework fw)
    {
        foreach (var item in Service.ObjectTable.Where(t => t.ObjectKind == Dalamud.Game.ClientState.Objects.Enums.ObjectKind.Treasure))
        {
            if (item.IsTargetable)
            {
                if (!_opened.TryGetValue(item.GameObjectId, out var openedAt) || openedAt.AddSeconds(30) < fw.LastUpdate)
                {
                    _chests[item.GameObjectId] = new(item.GameObjectId, item.Position, item.DataId);
                    _opened.Remove(item.GameObjectId);
                }
            }
            else
            {
                _opened.Remove(item.GameObjectId);
                _chests.Remove(item.GameObjectId);
            }
        }
    }
}
