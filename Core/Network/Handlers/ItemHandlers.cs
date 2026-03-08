using InsightBot.Core.Game;
using InsightBot.Core.Game.Entities;
using InsightBot.Core.Network;
using System;

namespace InsightBot.Core.Network.Handlers;

// ── 0xA034 — Item pickup result ───────────────────────────────────────────────

/// <summary>
/// Handles 0xA034 — result of a pick-up attempt.
///
/// Packet layout:
///   byte    Result (1=success, 0=fail)
///   uint32  ItemUniqueId  (only if success)
///   byte    InventorySlot (only if success)
/// </summary>
public sealed class ItemPickupHandler : PacketHandlerBase
{
    protected override ushort Opcode => Opcodes.S_ITEM_PICKUP;

    protected override void Process(PacketReader r, Packet raw)
    {
        byte result = r.ReadByte();
        if (result != 1) return;

        uint itemUid     = r.ReadUInt32();
        byte inventorySlot = r.ReadByte();

        GameContext.Instance.RemoveEntity(itemUid);
        GameContext.Instance.RaiseItemPickedUp(itemUid);
    }
}

// ── Ground item spawn is handled by SpawnHandler (EntityType.GroundItem) ──────
// ── But item drop event (server confirmation) comes via 0xB036 ───────────────

/// <summary>
/// Handles 0xB036 — inventory update after picking up, dropping, or
/// equipping/unequipping an item.
///
/// Packet layout:
///   byte    UpdateType  (1=add, 2=remove, 3=move, 4=update)
///   byte    SlotFrom
///   byte    SlotTo      (only for move/equip)
///   uint32  ItemRefId   (only for add)
///   … additional item attributes (optional, varies by type) …
///
/// We log this and update a simple slot map in GameContext.
/// Full inventory model is handled in the GameContext extension below.
/// </summary>
public sealed class InventoryUpdateHandler : PacketHandlerBase
{
    protected override ushort Opcode => Opcodes.S_INVENTORY_UPDATE;

    protected override void Process(PacketReader r, Packet raw)
    {
        byte updateType = r.ReadByte();

        switch (updateType)
        {
            case 1: // Item added to inventory
            {
                byte slotTo   = r.ReadByte();
                uint itemRefId = r.ReadUInt32();
                Console.WriteLine($"[Inventory] Item added RefId={itemRefId} -> slot {slotTo}");
                break;
            }
            case 2: // Item removed
            {
                byte slotFrom = r.ReadByte();
                Console.WriteLine($"[Inventory] Item removed from slot {slotFrom}");
                break;
            }
            case 3: // Item moved/equipped
            {
                byte slotFrom = r.ReadByte();
                byte slotTo   = r.ReadByte();
                Console.WriteLine($"[Inventory] Item moved slot {slotFrom} -> {slotTo}");
                break;
            }
            case 4: // Item quantity updated (stackable)
            {
                byte slot   = r.ReadByte();
                uint amount = r.ReadUInt32();
                Console.WriteLine($"[Inventory] Slot {slot} quantity={amount}");
                break;
            }
        }
    }
}
