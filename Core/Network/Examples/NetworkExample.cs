using InsightBot.Core.Network;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace InsightBot.Core.Network.Examples;

/// <summary>
/// Demonstrates how to wire the network layer together.
/// 
/// Mode A: Direct Connection (clientless bot — no game client needed)
/// Mode B: Proxy Mode (game client connects through the bot)
/// </summary>
public static class NetworkExample
{
    // ── Mode A: Clientless (Direct) ──────────────────────────────────────────

    /// <summary>
    /// Connect directly to the iSRO gateway, authenticate, and listen.
    /// The bot acts as the client — no game window required.
    /// </summary>
    public static async Task RunClientlessModeAsync(
        string gatewayHost, int gatewayPort,
        string username, string password,
        CancellationToken ct)
    {
        // 1. Open direct connection to gateway
        await using var conn = await SroConnection.ConnectAsync(gatewayHost, gatewayPort, ct);

        // 2. Set up packet dispatcher
        var dispatcher = new PacketDispatcher();
        var logger = new PacketLogger().IgnoreCommon();

        conn.PacketReceived += packet =>
        {
            logger.LogServerPacket(packet);
            dispatcher.Dispatch(packet);
        };

        // 3. Register handlers for gateway packets
        dispatcher.Register(Opcodes.S_PATCH_RESPONSE, OnPatchResponse);
        dispatcher.Register(Opcodes.S_LOGIN_RESPONSE, OnLoginResponse);
        dispatcher.Register(Opcodes.S_SERVER_LIST, OnServerList);
        dispatcher.Register(Opcodes.S_AGENT_HANDOFF, OnAgentHandoff);

        // 4. Start receive loop (runs until disconnected)
        var receiveTask = conn.RunReceiveLoopAsync(ct);

        // 5. Send initial patch request (first packet after handshake)
        var patchRequest = new PacketWriter()
            .WriteByte(0x00)   // Locale: 0 = global
            .WriteByte(0x00)
            .WriteAsciiString("SR_Client")
            .WriteUInt16(1)    // Version major
            .WriteUInt16(188)  // Version minor (iSRO 1.188)
            .Build(Opcodes.C_PATCH_REQUEST);

        await conn.SendAsync(patchRequest, ct);
        await receiveTask;
    }

    // ── Mode B: Proxy ────────────────────────────────────────────────────────

    /// <summary>
    /// Start a local proxy so the game client connects through the bot.
    /// Set the game's gateway IP to 127.0.0.1 and port to <paramref name="localPort"/>.
    /// </summary>
    public static async Task RunProxyModeAsync(
        int localPort,
        string remoteGatewayHost, int remoteGatewayPort,
        CancellationToken ct)
    {
        var proxy = new SroProxy(
            localHost: "127.0.0.1",
            localPort: localPort,
            remoteHost: remoteGatewayHost,
            remotePort: remoteGatewayPort
        );

        var dispatcher = new PacketDispatcher();
        var logger = new PacketLogger().IgnoreCommon();

        // Filter = process packet AND decide whether to forward (true = forward)
        proxy.ServerPacketFilter += packet =>
        {
            logger.LogServerPacket(packet);
            dispatcher.Dispatch(packet);
            return true; // always forward
        };

        proxy.ClientPacketFilter += packet =>
        {
            logger.LogClientPacket(packet);
            // Example: block chat packets the bot wants to suppress
            // if (packet.Opcode == Opcodes.C_CHAT) return false;
            return true;
        };

        dispatcher.Register(Opcodes.S_HP_UPDATE, packet =>
        {
            var reader = new PacketReader(packet);
            uint uid = reader.ReadUniqueId();
            uint hp = reader.ReadUInt32();
            uint mp = reader.ReadUInt32();
            Console.WriteLine($"[HP] UID={uid} HP={hp} MP={mp}");
        });

        dispatcher.Register(Opcodes.S_ENTITY_SPAWN, packet =>
        {
            var reader = new PacketReader(packet);
            uint refId = reader.ReadUInt32();
            byte type = reader.ReadByte(); // 1=NPC 2=PC 3=Monster 4=Pet
            uint uid = reader.ReadUniqueId();
            var (x, y, z, region) = reader.ReadPosition();
            Console.WriteLine($"[SPAWN] RefId={refId} Type={type} UID={uid} @ ({x:F1},{y:F1}) Region={region}");
        });

        proxy.Start();
        Console.WriteLine($"[Proxy] Listening on 127.0.0.1:{localPort} → {remoteGatewayHost}:{remoteGatewayPort}");

        // Accept connections in a loop
        while (!ct.IsCancellationRequested)
        {
            try { await proxy.AcceptAndRunAsync(ct); }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { Console.WriteLine($"[Proxy] Session error: {ex.Message}"); }
        }

        await proxy.DisposeAsync();
    }

    // ── Packet Handlers (Gateway) ────────────────────────────────────────────

    private static void OnPatchResponse(Packet packet)
    {
        var r = new PacketReader(packet);
        byte result = r.ReadByte(); // 1 = OK, 2 = need update
        Console.WriteLine($"[Patch] Result={result}");
    }

    private static void OnLoginResponse(Packet packet)
    {
        var r = new PacketReader(packet);
        byte result = r.ReadByte();
        Console.WriteLine(result == 1
            ? "[Login] Success"
            : $"[Login] Failed, code={result}");
    }

    private static void OnServerList(Packet packet)
    {
        var r = new PacketReader(packet);
        byte count = r.ReadByte();
        Console.WriteLine($"[Servers] {count} server(s):");
        for (int i = 0; i < count; i++)
        {
            byte id = r.ReadByte();
            string name = r.ReadAsciiString();
            Console.WriteLine($"  [{id}] {name}");
        }
    }

    private static void OnAgentHandoff(Packet packet)
    {
        var r = new PacketReader(packet);
        byte result = r.ReadByte();
        uint token = r.ReadUInt32();
        string agentIp = r.ReadAsciiString();
        ushort agentPort = r.ReadUInt16();
        Console.WriteLine($"[Handoff] Agent={agentIp}:{agentPort} Token=0x{token:X8}");
    }
}