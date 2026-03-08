using InsightBot.Core.Network;
using System;
using System.Collections.Generic;

namespace InsightBot.Core.Network.Handlers;

/// <summary>
/// Marker interface for all packet handlers.
/// Each handler owns one or more opcodes and writes results into <see cref="Game.GameContext"/>.
/// </summary>
public interface IPacketHandler
{
    /// <summary>The opcodes this handler is responsible for.</summary>
    IReadOnlyList<ushort> HandledOpcodes { get; }

    /// <summary>Process one incoming packet.</summary>
    void Handle(Packet packet);
}

/// <summary>
/// Convenience base class — handles a single opcode and wraps errors.
/// </summary>
public abstract class PacketHandlerBase : IPacketHandler
{
    protected abstract ushort Opcode { get; }
    public IReadOnlyList<ushort> HandledOpcodes => [Opcode];

    public void Handle(Packet packet)
    {
        try
        {
            Process(new PacketReader(packet), packet);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Handler 0x{Opcode:X4}] Error: {ex.Message}");
        }
    }

    protected abstract void Process(PacketReader reader, Packet raw);
}

/// <summary>
/// Registry: collects all handlers and registers them with a <see cref="PacketDispatcher"/>.
/// </summary>
public sealed class HandlerRegistry
{
    private readonly List<IPacketHandler> _handlers = new();

    public HandlerRegistry Register(IPacketHandler handler)
    {
        _handlers.Add(handler);
        return this;
    }

    public HandlerRegistry RegisterAll(IEnumerable<IPacketHandler> handlers)
    {
        foreach (var h in handlers) _handlers.Add(h);
        return this;
    }

    /// <summary>Wire all registered handlers into the dispatcher.</summary>
    public void AttachTo(PacketDispatcher dispatcher)
    {
        foreach (var handler in _handlers)
            foreach (var opcode in handler.HandledOpcodes)
                dispatcher.Register(opcode, handler.Handle);
    }

    /// <summary>Build the standard iSRO handler set and attach it.</summary>
    public static HandlerRegistry BuildDefault(PacketDispatcher dispatcher)
    {
        var registry = new HandlerRegistry();
        registry
            .Register(new SpawnHandler())
            .Register(new DespawnHandler())
            .Register(new CharacterDataHandler())
            .Register(new HpMpUpdateHandler())
            .Register(new MovementHandler())
            .Register(new MovementStopHandler())
            .Register(new EntityDeathHandler())
            .Register(new ItemPickupHandler())
            .Register(new InventoryUpdateHandler())
            .Register(new ChatHandler())
            .Register(new BuffHandler())
            .Register(new LoginResponseHandler())
            .Register(new AgentAuthHandler())
            .Register(new PassKeyRequestHandler())
            .Register(new PassKeyResultHandler())
            .Register(new InventoryHandler());

        registry.AttachTo(dispatcher);
        return registry;
    }
}