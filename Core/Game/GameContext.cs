using InsightBot.Core.Game.Entities;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace InsightBot.Core.Game;

/// <summary>
/// Central, thread-safe repository of the current game world state.
/// All packet handlers write here; the UI and bot logic read from here.
/// </summary>
public sealed class GameContext
{
    // ── Singleton-style access ────────────────────────────────────────────────
    public static readonly GameContext Instance = new();
    private GameContext() { }

    // ── Local character ───────────────────────────────────────────────────────
    public Character? LocalCharacter { get; private set; }
    public bool IsInGame => LocalCharacter != null;

    public void SetLocalCharacter(Character c) => LocalCharacter = c;
    public void ClearLocalCharacter() => LocalCharacter = null;

    // ── World entities (keyed by UniqueId) ───────────────────────────────────
    private readonly ConcurrentDictionary<uint, WorldEntity> _entities = new();

    public IEnumerable<WorldEntity> AllEntities => _entities.Values;
    public IEnumerable<Monster> Monsters => _entities.Values.OfType<Monster>();
    public IEnumerable<OtherPlayer> Players => _entities.Values.OfType<OtherPlayer>();
    public IEnumerable<GroundItem> GroundItems => _entities.Values.OfType<GroundItem>();
    public IEnumerable<Npc> Npcs => _entities.Values.OfType<Npc>();

    public void AddOrUpdateEntity(WorldEntity entity) =>
        _entities[entity.UniqueId] = entity;

    public bool TryGetEntity(uint uid, out WorldEntity? entity) =>
        _entities.TryGetValue(uid, out entity);

    public bool TryGetMonster(uint uid, out Monster? monster)
    {
        if (_entities.TryGetValue(uid, out var e) && e is Monster m)
        {
            monster = m;
            return true;
        }
        monster = null;
        return false;
    }

    public bool RemoveEntity(uint uid) => _entities.TryRemove(uid, out _);

    public void ClearEntities() => _entities.Clear();

    // ── Nearest helpers (used by bot logic) ───────────────────────────────────
    public Monster? NearestLiveMonster(float maxRange = float.MaxValue)
    {
        if (LocalCharacter == null) return null;
        var pos = LocalCharacter.Position;
        return Monsters
            .Where(m => !m.IsDead && pos.DistanceTo(m.Position) <= maxRange)
            .OrderBy(m => pos.DistanceTo(m.Position))
            .FirstOrDefault();
    }

    public GroundItem? NearestPickableItem(float maxRange = 200f)
    {
        if (LocalCharacter == null) return null;
        var pos = LocalCharacter.Position;
        uint uid = LocalCharacter.UniqueId;
        return GroundItems
            .Where(i => i.CanPickUp(uid) && pos.DistanceTo(i.Position) <= maxRange)
            .OrderBy(i => pos.DistanceTo(i.Position))
            .FirstOrDefault();
    }

    // ── Current target ────────────────────────────────────────────────────────
    public uint CurrentTargetUid { get; private set; }
    public WorldEntity? CurrentTarget =>
        CurrentTargetUid != 0 && _entities.TryGetValue(CurrentTargetUid, out var t) ? t : null;

    public void SetTarget(uint uid) => CurrentTargetUid = uid;
    public void ClearTarget() => CurrentTargetUid = 0;

    // ── Secondary PIN (PassKey) ───────────────────────────────────────────────
    private string? _secondaryPin;

    public void SetSecondaryPin(string? pin) => _secondaryPin = pin;
    public string? GetSecondaryPin() => _secondaryPin;

    // ── Packet send delegate (injected by BotService) ─────────────────────────
    /// <summary>
    /// Delegate set by BotService/BotEngine so that packet handlers can send
    /// packets to the server without a direct SroConnection reference.
    /// </summary>
    public Action<Network.Packet>? SendPacket { get; set; }

    // ── Events ────────────────────────────────────────────────────────────────
    public event Action<Character>? CharacterUpdated;
    public event Action<WorldEntity>? EntitySpawned;
    public event Action<uint>? EntityDespawned;
    public event Action<uint, uint, uint>? HpMpUpdated;     // uid, hp, mp
    public event Action<uint>? EntityDied;
    public event Action<GroundItem>? ItemDropped;
    public event Action<uint>? ItemPickedUp;
    public event Action<ChatMessage>? ChatReceived;
    public event Action<string>? LoginFailed;
    public event Action? PassKeyAccepted;
    public event Action<string, ushort, uint>? AgentHandoff;    // ip, port, token

    internal void RaiseCharacterUpdated() => CharacterUpdated?.Invoke(LocalCharacter!);
    internal void RaiseEntitySpawned(WorldEntity e) => EntitySpawned?.Invoke(e);
    internal void RaiseEntityDespawned(uint uid) => EntityDespawned?.Invoke(uid);
    internal void RaiseHpMpUpdated(uint uid, uint hp, uint mp) => HpMpUpdated?.Invoke(uid, hp, mp);
    internal void RaiseEntityDied(uint uid) => EntityDied?.Invoke(uid);
    internal void RaiseItemDropped(GroundItem item) => ItemDropped?.Invoke(item);
    internal void RaiseItemPickedUp(uint uid) => ItemPickedUp?.Invoke(uid);
    internal void RaiseChatReceived(ChatMessage msg) => ChatReceived?.Invoke(msg);
    internal void RaiseLoginFailed(string reason) => LoginFailed?.Invoke(reason);
    internal void RaisePassKeyAccepted() => PassKeyAccepted?.Invoke();
    internal void RaiseAgentHandoff(string ip, ushort port, uint token)
        => AgentHandoff?.Invoke(ip, port, token);
}

// ── Chat message model ────────────────────────────────────────────────────────

public sealed class ChatMessage
{
    public ChatChannel Channel { get; init; }
    public string Sender { get; init; } = string.Empty;
    public string Content { get; init; } = string.Empty;
    public DateTime ReceivedAt { get; } = DateTime.Now;
}