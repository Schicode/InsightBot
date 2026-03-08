using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace InsightBot.Core.Network;

/// <summary>
/// Routes incoming packets to registered handler delegates by opcode.
/// Supports wildcard handlers (catch-all) and per-opcode handlers.
/// 
/// Usage:
///   dispatcher.Register(Opcodes.S_ENTITY_SPAWN, HandleEntitySpawn);
///   dispatcher.Register(Opcodes.S_HP_UPDATE,    HandleHpUpdate);
///   connection.PacketReceived += dispatcher.Dispatch;
/// </summary>
public sealed class PacketDispatcher
{
    private readonly Dictionary<ushort, List<Func<Packet, Task>>> _handlers = new();
    private readonly List<Func<Packet, Task>> _wildcardHandlers = new();

    // ── Registration ─────────────────────────────────────────────────────────

    public void Register(ushort opcode, Action<Packet> handler)
    {
        Register(opcode, p => { handler(p); return Task.CompletedTask; });
    }

    public void Register(ushort opcode, Func<Packet, Task> handler)
    {
        if (!_handlers.TryGetValue(opcode, out var list))
        {
            list = new List<Func<Packet, Task>>();
            _handlers[opcode] = list;
        }
        list.Add(handler);
    }

    /// <summary>Register a handler that receives ALL packets regardless of opcode.</summary>
    public void RegisterWildcard(Action<Packet> handler) =>
        _wildcardHandlers.Add(p => { handler(p); return Task.CompletedTask; });

    public void RegisterWildcard(Func<Packet, Task> handler) =>
        _wildcardHandlers.Add(handler);

    public void Unregister(ushort opcode) => _handlers.Remove(opcode);

    // ── Dispatch ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Synchronous dispatch — use when PacketReceived is an Action event.
    /// Async handlers are fire-and-forget in this overload.
    /// </summary>
    public void Dispatch(Packet packet)
    {
        // Wildcard first
        foreach (var h in _wildcardHandlers)
            _ = h(packet);

        if (_handlers.TryGetValue(packet.Opcode, out var list))
            foreach (var h in list)
                _ = h(packet);
    }

    /// <summary>
    /// Asynchronous dispatch — awaits all handlers sequentially.
    /// </summary>
    public async Task DispatchAsync(Packet packet)
    {
        foreach (var h in _wildcardHandlers)
            await h(packet);

        if (_handlers.TryGetValue(packet.Opcode, out var list))
            foreach (var h in list)
                await h(packet);
    }
}
