using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;
using TerraStorage.Common;
using TerraStorage.Content.Items;
using TerraStorage.Content.Tiles;
using TerraStorage.Helpers;

namespace TerraStorage.Systems
{
    //Identifies the type of a Requisition multiplayer packet.
    public enum PacketType : byte
    {
        SyncDiskInsert,
        SyncDiskRemove,
        DepositItem,
        WithdrawItem,
        CraftRequest,
        SyncDriveBay,
        SyncStationInsert,
        SyncStationRemove,
        SyncDiskData,
        RequestDiskData,
        ArchiveDiskRequest,
        ArchiveDiskResult,
        WithdrawItemResult,
        WithdrawItemByModData,
        WithdrawItemByFullItemTag,
        SyncRemoveDiskData,
        RestoreDiskRequest,
        UpgradeDiskRequest,
        DefragRequest,

        // ─── Delta Sync (Predictive Mode) ────────────────────
        //Server → all clients: item-level delta for a single disk.
        DeltaDiskData,
        //Server → requesting client: success/failure for a storage operation.
        OperationResponse,
        //Client → server: request full resync for a specific disk (seq gap detected).
        RequestFullDiskSync,

        //Server → client: give an item directly to the client's inventory (used when storage is full).
        GiveItemToClient,

        //Client → server: quick-stack inventory items into a nearby terminal's disk network.
        QuickStackToStorage,
        //Server → client: slot updates after a quick-stack operation.
        QuickStackResult,

        //Server → client: chunked disk data for disks that exceed the 65 KB packet limit.
        SyncDiskDataChunked,

        //Client → server: deposit one item into the network of the Terminal at a given tile
        //position. Server resolves the network and range-checks the player (the not-open trust
        //model, mirroring QuickStackToStorage). Appended last to keep existing byte values stable.
        DepositItemAtPosition,
    }

    // Sends and receives all Requisition network packets.
    // On the server, most handlers also relay the packet to all other clients
    // (the standard tModLoader server-relay pattern).
    public static class NetworkHandler
    {
        public static void HandlePacket(Mod mod, BinaryReader reader, int whoAmI)
        {
            var type = (PacketType)reader.ReadByte();

            switch (type)
            {
                case PacketType.SyncDiskInsert:
                    HandleSyncDiskInsert(mod, reader, whoAmI);
                    break;
                case PacketType.SyncDiskRemove:
                    HandleSyncDiskRemove(mod, reader, whoAmI);
                    break;
                case PacketType.DepositItem:
                    HandleDepositItem(mod, reader, whoAmI);
                    break;
                case PacketType.WithdrawItem:
                    HandleWithdrawItem(mod, reader, whoAmI);
                    break;
                case PacketType.CraftRequest:
                    HandleCraftRequest(mod, reader, whoAmI);
                    break;
                case PacketType.SyncDriveBay:
                    HandleSyncDriveBay(mod, reader, whoAmI);
                    break;
                case PacketType.SyncStationInsert:
                    HandleSyncStationInsert(mod, reader, whoAmI);
                    break;
                case PacketType.SyncStationRemove:
                    HandleSyncStationRemove(mod, reader, whoAmI);
                    break;
                case PacketType.SyncDiskData:
                    HandleSyncDiskData(reader);
                    break;
                case PacketType.RequestDiskData:
                    HandleRequestDiskData(mod, reader, whoAmI);
                    break;
                case PacketType.ArchiveDiskRequest:
                    HandleArchiveDiskRequest(mod, reader, whoAmI);
                    break;
                case PacketType.ArchiveDiskResult:
                    HandleArchiveDiskResult(reader);
                    break;
                case PacketType.WithdrawItemResult:
                    HandleWithdrawItemResult(reader);
                    break;
                case PacketType.WithdrawItemByModData:
                    HandleWithdrawItemByModData(mod, reader, whoAmI);
                    break;
                case PacketType.WithdrawItemByFullItemTag:
                    HandleWithdrawItemByFullItemTag(mod, reader, whoAmI);
                    break;
                case PacketType.SyncRemoveDiskData:
                    HandleSyncRemoveDiskData(reader);
                    break;
                case PacketType.RestoreDiskRequest:
                    HandleRestoreDiskRequest(mod, reader, whoAmI);
                    break;
                case PacketType.UpgradeDiskRequest:
                    HandleUpgradeDiskRequest(mod, reader);
                    break;
                case PacketType.DefragRequest:
                    HandleDefragRequest(mod, reader, whoAmI);
                    break;
                case PacketType.DeltaDiskData:
                    HandleDeltaDiskData(reader);
                    break;
                case PacketType.OperationResponse:
                    HandleOperationResponse(reader);
                    break;
                case PacketType.RequestFullDiskSync:
                    HandleRequestFullDiskSync(mod, reader, whoAmI);
                    break;
                case PacketType.GiveItemToClient:
                    HandleGiveItemToClient(reader);
                    break;
                case PacketType.QuickStackToStorage:
                    HandleQuickStackToStorage(mod, reader, whoAmI);
                    break;
                case PacketType.QuickStackResult:
                    HandleQuickStackResult(reader);
                    break;
                case PacketType.SyncDiskDataChunked:
                    HandleSyncDiskDataChunked(reader);
                    break;
                case PacketType.DepositItemAtPosition:
                    HandleDepositItemAtPosition(mod, reader, whoAmI);
                    break;
            }
        }

        // ─── Disk Slot Sync (Drive Bays) ────────────────────────────

        public static void SendSyncDiskInsert(Mod mod, int entityId, int slot, Item diskItem)
        {
            if (Main.netMode == NetmodeID.SinglePlayer)
                return;

            var packet = mod.GetPacket();
            packet.Write((byte)PacketType.SyncDiskInsert);
            packet.Write(entityId);
            packet.Write(slot);
            ItemIO.Send(diskItem, packet, true);
            packet.Send();
        }

        public static void SendSyncDiskRemove(Mod mod, int entityId, int slot)
        {
            if (Main.netMode == NetmodeID.SinglePlayer)
                return;

            var packet = mod.GetPacket();
            packet.Write((byte)PacketType.SyncDiskRemove);
            packet.Write(entityId);
            packet.Write(slot);
            packet.Send();
        }

        private static void HandleSyncDiskInsert(Mod mod, BinaryReader reader, int whoAmI)
        {
            int entityId = reader.ReadInt32();
            int slot = reader.ReadInt32();
            var item = ItemIO.Receive(reader, true);

            // Fixed-size array, index off the wire — see HandleSyncDiskRemove.
            if (slot < 0 || slot >= DriveBayEntity.DiskSlotCount)
                return;

            DriveBayEntity sbe = null;
            if (Terraria.DataStructures.TileEntity.ByID.TryGetValue(entityId, out var entity)
                && entity is DriveBayEntity blockEntity)
            {
                sbe = blockEntity;
                if (Main.netMode == NetmodeID.Server)
                {
                    // The GUID on this item came off the wire, and every client is told every
                    // disk's GUID, so naming one proves nothing about whose disk it is.
                    var insertedDisk = item.ModItem as StorageDiskBase;
                    if (insertedDisk != null && !SenderMayClaimDisk(whoAmI, insertedDisk.DiskId))
                    {
                        RefuseInsert(mod, whoAmI, sbe, item);
                        return;
                    }

                    // InsertDisk assigns the GUID and registers the disk in StorageWorldSystem
                    // so clients can retrieve disk data by the correct GUID via RequestDiskData.
                    if (!sbe.InsertDisk(item, slot))
                    {
                        // The slot filled before this packet arrived.
                        RefuseInsert(mod, whoAmI, sbe, item);
                        return;
                    }
                }
                else
                {
                    // Clients receive the GUID-bearing item directly from the server.
                    sbe.DiskSlots[slot] = item;
                    sbe.RefreshVisualState(sbe.IsConnected);
                }
            }

            if (Main.netMode == NetmodeID.Server)
            {
                // Relay to ALL clients including the original sender so every client receives
                // the server-registered GUID for this disk.
                Item slotItem = sbe?.DiskSlots[slot] ?? item;
                var packet = mod.GetPacket();
                packet.Write((byte)PacketType.SyncDiskInsert);
                packet.Write(entityId);
                packet.Write(slot);
                ItemIO.Send(slotItem, packet, true);
                packet.Send(-1, -1);
            }
        }

        private static void HandleSyncDiskRemove(Mod mod, BinaryReader reader, int whoAmI)
        {
            int entityId = reader.ReadInt32();
            int slot = reader.ReadInt32();

            // The slot index comes off the wire. DiskSlots is a fixed array, so an out-of-range
            // value throws rather than doing anything useful.
            if (slot >= 0 && slot < DriveBayEntity.DiskSlotCount
                && Terraria.DataStructures.TileEntity.ByID.TryGetValue(entityId, out var entity)
                && entity is DriveBayEntity sbe)
            {
                sbe.DiskSlots[slot] = new Item();
                sbe.DiskSlots[slot].TurnToAir();
                sbe.RefreshVisualState(sbe.IsConnected);
            }

            if (Main.netMode == NetmodeID.Server)
            {
                var packet = mod.GetPacket();
                packet.Write((byte)PacketType.SyncDiskRemove);
                packet.Write(entityId);
                packet.Write(slot);
                packet.Send(-1, whoAmI);
            }
        }

        // ─── Station Slot Sync (CraftingCore) ───────────────────────────

        public static void SendSyncStationInsert(Mod mod, int entityId, int slot, Item stationItem)
        {
            if (Main.netMode == NetmodeID.SinglePlayer)
                return;

            var packet = mod.GetPacket();
            packet.Write((byte)PacketType.SyncStationInsert);
            packet.Write(entityId);
            packet.Write(slot);
            ItemIO.Send(stationItem, packet, true);
            packet.Send();
        }

        public static void SendSyncStationRemove(Mod mod, int entityId, int slot)
        {
            if (Main.netMode == NetmodeID.SinglePlayer)
                return;

            var packet = mod.GetPacket();
            packet.Write((byte)PacketType.SyncStationRemove);
            packet.Write(entityId);
            packet.Write(slot);
            packet.Send();
        }

        private static void HandleSyncStationInsert(Mod mod, BinaryReader reader, int whoAmI)
        {
            int entityId = reader.ReadInt32();
            int slot = reader.ReadInt32();
            var item = ItemIO.Receive(reader, true);

            // Fixed-size array, index off the wire — the same guard the disk slots carry.
            if (slot < 0 || slot >= CraftingCoreEntity.StationSlotCount)
                return;

            if (Terraria.DataStructures.TileEntity.ByID.TryGetValue(entityId, out var entity)
                && entity is CraftingCoreEntity cce)
            {
                cce.EnsureSlotsInitialized();
                cce.StationSlots[slot] = item;
            }

            if (Main.netMode == NetmodeID.Server)
            {
                var packet = mod.GetPacket();
                packet.Write((byte)PacketType.SyncStationInsert);
                packet.Write(entityId);
                packet.Write(slot);
                ItemIO.Send(item, packet, true);
                packet.Send(-1, whoAmI);
            }
        }

        private static void HandleSyncStationRemove(Mod mod, BinaryReader reader, int whoAmI)
        {
            int entityId = reader.ReadInt32();
            int slot = reader.ReadInt32();

            // Fixed-size array, index off the wire — see HandleSyncStationInsert.
            if (slot < 0 || slot >= CraftingCoreEntity.StationSlotCount)
                return;

            if (Terraria.DataStructures.TileEntity.ByID.TryGetValue(entityId, out var entity)
                && entity is CraftingCoreEntity cce)
            {
                cce.EnsureSlotsInitialized();
                cce.StationSlots[slot] = new Item();
                cce.StationSlots[slot].TurnToAir();
            }

            if (Main.netMode == NetmodeID.Server)
            {
                var packet = mod.GetPacket();
                packet.Write((byte)PacketType.SyncStationRemove);
                packet.Write(entityId);
                packet.Write(slot);
                packet.Send(-1, whoAmI);
            }
        }

        // ─── Storage Item Operations ────────────────────────────────────

        public static void SendDepositItem(Mod mod, List<Guid> diskIds, Item item)
        {
            if (Main.netMode != NetmodeID.MultiplayerClient)
                return;

            var packet = mod.GetPacket();
            packet.Write((byte)PacketType.DepositItem);
            packet.Write(diskIds.Count);
            foreach (var id in diskIds)
                packet.Write(id.ToByteArray());
            ItemIO.Send(item, packet, true);
            packet.Send();
        }

        public static void SendWithdrawItem(Mod mod, List<Guid> diskIds, int itemType, int count, int prefix, bool shift = false)
        {
            if (Main.netMode != NetmodeID.MultiplayerClient)
                return;

            var packet = mod.GetPacket();
            packet.Write((byte)PacketType.WithdrawItem);
            packet.Write(diskIds.Count);
            foreach (var id in diskIds)
                packet.Write(id.ToByteArray());
            packet.Write(itemType);
            packet.Write(count);
            packet.Write(prefix);
            packet.Write(shift);
            packet.Send();
        }

        public static void SendWithdrawItemByModData(Mod mod, List<Guid> diskIds, TagCompound modData, bool shift = false)
        {
            if (Main.netMode != NetmodeID.MultiplayerClient)
                return;

            var packet = mod.GetPacket();
            packet.Write((byte)PacketType.WithdrawItemByModData);
            packet.Write(diskIds.Count);
            foreach (var id in diskIds)
                packet.Write(id.ToByteArray());
            TagIO.Write(modData, packet);
            packet.Write(shift);
            packet.Send();
        }

        public static void SendWithdrawItemByFullItemTag(Mod mod, List<Guid> diskIds, TagCompound fullItemTag, bool shift = false)
        {
            if (Main.netMode != NetmodeID.MultiplayerClient)
                return;

            var packet = mod.GetPacket();
            packet.Write((byte)PacketType.WithdrawItemByFullItemTag);
            packet.Write(diskIds.Count);
            foreach (var id in diskIds)
                packet.Write(id.ToByteArray());
            TagIO.Write(fullItemTag, packet);
            packet.Write(shift);
            packet.Send();
        }

        private static void HandleWithdrawItemByFullItemTag(Mod mod, BinaryReader reader, int whoAmI)
        {
            int diskCount = reader.ReadInt32();
            var diskIds = ReadGuidList(reader, diskCount);
            if (diskIds == null)
                return;

            var fullItemTag = TagIO.Read(reader);
            bool shift = reader.ReadBoolean();

            if (Main.netMode == NetmodeID.Server)
            {
                DBG($"HandleWithdrawItemByFullItemTag: from={whoAmI} disks=[{string.Join(", ", diskIds.Select(g => g.ToString()[..8]))}]");
                StorageWorldSystem.Instance.BeginModificationTracking();
                var extracted = StorageWorldSystem.Instance.ExtractItemWithFullItemTag(diskIds, fullItemTag);

                var withdrawFailure = extracted.IsAir
                    ? StorageOperationFailure.NothingWithdrawn
                    : StorageOperationFailure.None;
                EndTrackingAndRespond(mod, whoAmI, withdrawFailure, diskIds);
                DBG($"  ExtractItemWithFullItemTag result: type={extracted.type} stack={extracted.stack} isAir={extracted.IsAir}");

                var resultPacket = mod.GetPacket();
                resultPacket.Write((byte)PacketType.WithdrawItemResult);
                ItemIO.Send(extracted, resultPacket, true);
                resultPacket.Write(shift);
                resultPacket.Send(whoAmI);
            }
        }

        private static void HandleWithdrawItemByModData(Mod mod, BinaryReader reader, int whoAmI)
        {
            int diskCount = reader.ReadInt32();
            var diskIds = ReadGuidList(reader, diskCount);
            if (diskIds == null)
                return;

            var modData = TagIO.Read(reader);
            bool shift = reader.ReadBoolean();

            if (Main.netMode == NetmodeID.Server)
            {
                DBG($"HandleWithdrawItemByModData: from={whoAmI} disks=[{string.Join(", ", diskIds.Select(g => g.ToString()[..8]))}]");
                StorageWorldSystem.Instance.BeginModificationTracking();
                var extracted = StorageWorldSystem.Instance.ExtractItemWithModData(diskIds, modData);
                DBG($"  ExtractItemWithModData result: type={extracted.type} stack={extracted.stack} isAir={extracted.IsAir}");

                var resultPacket = mod.GetPacket();
                resultPacket.Write((byte)PacketType.WithdrawItemResult);
                ItemIO.Send(extracted, resultPacket, true);
                resultPacket.Write(shift);
                resultPacket.Send(whoAmI);

                var withdrawFailure = extracted.IsAir
                    ? StorageOperationFailure.NothingWithdrawn
                    : StorageOperationFailure.None;
                EndTrackingAndRespond(mod, whoAmI, withdrawFailure, diskIds);
            }
        }

        private static void HandleDepositItem(Mod mod, BinaryReader reader, int whoAmI)
        {
            int count = reader.ReadInt32();
            var diskIds = ReadGuidList(reader, count);
            if (diskIds == null)
                return;

            var item = ItemIO.Receive(reader, true);

            if (Main.netMode == NetmodeID.Server)
            {
                DBG($"HandleDepositItem: from={whoAmI} item={item.type}x{item.stack} disks=[{string.Join(", ", diskIds.Select(g => g.ToString()[..8]))}]");
                StorageWorldSystem.Instance.BeginModificationTracking();
                int leftover = StorageWorldSystem.Instance.InsertItem(diskIds, item);
                DBG($"  InsertItem result: leftover={leftover}");

                // Built before item.stack is overwritten with the leftover below.
                var outcome = new DepositOutcome(item.stack, leftover);

                if (outcome.NeedsReturn)
                {
                    item.stack = outcome.Leftover;
                    var resultPacket = mod.GetPacket();
                    resultPacket.Write((byte)PacketType.WithdrawItemResult);
                    ItemIO.Send(item, resultPacket, true);
                    resultPacket.Write(true); // shift=true: route into inventory, fall back to cursor
                    resultPacket.Send(whoAmI);
                }

                var depositFailure = outcome.AnyDeposited
                    ? StorageOperationFailure.None
                    : StorageOperationFailure.NothingDeposited;
                EndTrackingAndRespond(mod, whoAmI, depositFailure, diskIds);
            }
        }

        // Client → server: deposit one item into the network of the Terminal at terminalPos.
        // Used for the "nearby, no Terminal open" case — the server (not the client) resolves the
        // network and range-checks the player, so client-sent disk GUIDs are never trusted here.
        public static void SendDepositItemAtPosition(Mod mod, Point16 terminalPos, Item item)
        {
            if (Main.netMode != NetmodeID.MultiplayerClient)
                return;

            var packet = mod.GetPacket();
            packet.Write((byte)PacketType.DepositItemAtPosition);
            packet.Write(terminalPos.X);
            packet.Write(terminalPos.Y);
            ItemIO.Send(item, packet, true);
            packet.Send();
        }

        private static void HandleDepositItemAtPosition(Mod mod, BinaryReader reader, int whoAmI)
        {
            short tx = reader.ReadInt16();
            short ty = reader.ReadInt16();
            var item = ItemIO.Receive(reader, true);

            if (Main.netMode != NetmodeID.Server)
                return;

            var terminalPos = new Point16(tx, ty);

            // Every failure path returns the item to the client so it can never vanish.
            if (!TileEntity.ByPosition.TryGetValue(terminalPos, out var entity) || entity is not TerminalEntity)
            {
                SendReturnItemToClient(mod, whoAmI, item);
                RefuseOperation(mod, whoAmI, StorageOperationFailure.NoTerminalFound);
                return;
            }

            var player = Main.player[whoAmI];
            float dx = player.Center.X - (terminalPos.X * 16f + 24f);
            float dy = player.Center.Y - (terminalPos.Y * 16f + 24f);
            if (dx * dx + dy * dy > 240f * 240f) // 15 tiles in pixels
            {
                SendReturnItemToClient(mod, whoAmI, item);
                RefuseOperation(mod, whoAmI, StorageOperationFailure.NoStorageInRange);
                return;
            }

            var diskIds = StorageNetwork.GetAllConnectedDiskIds(terminalPos);
            if (diskIds.Count == 0)
            {
                SendReturnItemToClient(mod, whoAmI, item);
                RefuseOperation(mod, whoAmI, StorageOperationFailure.NoStorageConnected);
                return;
            }

            StorageWorldSystem.Instance.BeginModificationTracking();
            int leftover = StorageWorldSystem.Instance.InsertItem(diskIds, item);

            // Built before item.stack becomes the leftover — see HandleDepositItem.
            var outcome = new DepositOutcome(item.stack, leftover);

            if (outcome.NeedsReturn)
            {
                item.stack = outcome.Leftover;
                SendReturnItemToClient(mod, whoAmI, item);
            }

            var depositFailure = outcome.AnyDeposited
                ? StorageOperationFailure.None
                : StorageOperationFailure.NothingDeposited;
            EndTrackingAndRespond(mod, whoAmI, depositFailure, diskIds);
        }

        // Server → client: return an item to the player's inventory with full fidelity (mod data
        // preserved). Used when a deposit is rejected or only partially accepted. Reuses the
        // WithdrawItemResult route (shift=true) so modded items keep their data, unlike
        // SendGiveItemToClient which only carries type/stack/prefix.
        private static void SendReturnItemToClient(Mod mod, int toClient, Item item)
        {
            var packet = mod.GetPacket();
            packet.Write((byte)PacketType.WithdrawItemResult);
            ItemIO.Send(item, packet, true);
            packet.Write(true); // shift=true: route into inventory, fall back to cursor
            packet.Send(toClient);
        }

        private static void HandleWithdrawItem(Mod mod, BinaryReader reader, int whoAmI)
        {
            int diskCount = reader.ReadInt32();
            var diskIds = ReadGuidList(reader, diskCount);
            if (diskIds == null)
                return;

            int itemType = reader.ReadInt32();
            int count = reader.ReadInt32();
            int prefix = reader.ReadInt32();
            bool shift = reader.ReadBoolean();

            if (Main.netMode == NetmodeID.Server)
            {
                DBG($"HandleWithdrawItem: from={whoAmI} type={itemType} count={count} prefix={prefix} disks=[{string.Join(", ", diskIds.Select(g => g.ToString()[..8]))}]");
                StorageWorldSystem.Instance.BeginModificationTracking();
                var extracted = StorageWorldSystem.Instance.ExtractItem(diskIds, itemType, count, prefix);
                DBG($"  ExtractItem result: type={extracted.type} stack={extracted.stack} isAir={extracted.IsAir}");

                // Send the extracted item back to the requesting client to place on cursor or in inventory
                var resultPacket = mod.GetPacket();
                resultPacket.Write((byte)PacketType.WithdrawItemResult);
                ItemIO.Send(extracted, resultPacket, true);
                resultPacket.Write(shift);
                resultPacket.Send(whoAmI);

                var withdrawFailure = extracted.IsAir
                    ? StorageOperationFailure.NothingWithdrawn
                    : StorageOperationFailure.None;
                EndTrackingAndRespond(mod, whoAmI, withdrawFailure, diskIds);
            }
        }

        private static void HandleWithdrawItemResult(BinaryReader reader)
        {
            // Server → client only. Main.LocalPlayer on a dedicated server is the dummy player.
            if (Main.netMode != NetmodeID.MultiplayerClient) return;

            var item = ItemIO.Receive(reader, true);
            bool shift = reader.ReadBoolean();

            if (item.IsAir) return;

            var player = Main.LocalPlayer;

            if (shift)
            {
                item = player.GetItem(player.whoAmI, item, GetItemSettings.InventoryEntityToPlayerInventorySettings);
                if (!item.IsAir)
                    Main.mouseItem = item; // inventory full fallback: put on cursor
            }
            else if (Main.mouseItem.IsAir)
            {
                Main.mouseItem = item;
            }
            else if (Main.mouseItem.type == item.type && Main.mouseItem.prefix == item.prefix
                && Main.mouseItem.stack < Main.mouseItem.maxStack)
            {
                int canMerge = Math.Min(item.stack, Main.mouseItem.maxStack - Main.mouseItem.stack);
                Main.mouseItem.stack += canMerge;
                item.stack -= canMerge;
                if (item.stack > 0)
                    player.GetItem(player.whoAmI, item, GetItemSettings.InventoryEntityToPlayerInventorySettings);
            }
            else
            {
                // Cursor has a different item; try inventory
                player.GetItem(player.whoAmI, item, GetItemSettings.InventoryEntityToPlayerInventorySettings);
            }
        }

        // ─── Crafting ───────────────────────────────────────────────────

        public static void SendCraftRequest(Mod mod, List<Guid> diskIds, int recipeItemType,
            int craftAmount, HashSet<int> stations, HashSet<CraftingCondition> conditions, bool cleanCraft, bool craftToInventory,
            int recipeIndex)
        {
            if (Main.netMode != NetmodeID.MultiplayerClient)
                return;

            var packet = mod.GetPacket();
            packet.Write((byte)PacketType.CraftRequest);
            packet.Write(diskIds.Count);
            foreach (var id in diskIds)
                packet.Write(id.ToByteArray());
            packet.Write(recipeItemType);
            packet.Write(recipeIndex);
            packet.Write(craftAmount);
            packet.Write(stations.Count);
            foreach (int s in stations)
                packet.Write(s);
            packet.Write(conditions.Count);
            foreach (var c in conditions)
                packet.Write((byte)c);
            packet.Write(cleanCraft);
            packet.Write(craftToInventory);
            packet.Send();
        }

        private static void HandleCraftRequest(Mod mod, BinaryReader reader, int whoAmI)
        {
            int diskCount = reader.ReadInt32();
            var diskIds = ReadGuidList(reader, diskCount);
            if (diskIds == null)
                return;

            int recipeItemType = reader.ReadInt32();
            int recipeIndex = reader.ReadInt32();
            int craftAmount = reader.ReadInt32();
            int stationCount = reader.ReadInt32();
            var stations = new HashSet<int>();
            for (int i = 0; i < stationCount; i++)
                stations.Add(reader.ReadInt32());
            int condCount = reader.ReadInt32();
            var conditions = new HashSet<CraftingCondition>();
            for (int i = 0; i < condCount; i++)
                conditions.Add((CraftingCondition)reader.ReadByte());
            bool cleanCraft = reader.ReadBoolean();
            bool craftToInventory = reader.ReadBoolean();

            if (Main.netMode == NetmodeID.Server)
            {
                // Server re-resolves so existing stock of the target item is ignored — the client
                // explicitly requested new crafts. When the client locked a specific recipe variant
                // (recipeIndex >= 0), force exactly that recipe; otherwise auto-select the best one.
                var plan = recipeIndex >= 0 && recipeIndex < Recipe.numRecipes
                    ? RecipeResolver.ResolveRecipe(Main.recipe[recipeIndex], craftAmount, diskIds, stations, conditions)
                    : RecipeResolver.ResolveForceCraft(recipeItemType, craftAmount, diskIds, stations, conditions);
                StorageWorldSystem.Instance.BeginModificationTracking();

                // Pre-check: block the craft if neither storage nor player inventory has room.
                // This prevents consuming ingredients with nowhere to put the result. The verdict
                // comes from GetCraftFailure so the panel's copy of these guards cannot drift.
                bool planIsFeasible = plan != null && plan.IsFeasible;

                var resultPreview = new Item();
                if (planIsFeasible)
                {
                    resultPreview.SetDefaults(plan.FinalItemType);
                    resultPreview.stack = plan.FinalItemCount;
                }

                var player = Main.player[whoAmI];
                bool playerHasRoomForResult = planIsFeasible && PlayerHasRoomFor(player, resultPreview);
                bool storageHasRoomForResult = planIsFeasible && !craftToInventory
                    && StorageWorldSystem.Instance.HasRoomFor(diskIds, resultPreview);

                var craftFailure = StorageOperationFailures.GetCraftFailure(planIsFeasible,
                    craftToInventory, playerHasRoomForResult, storageHasRoomForResult);

                if (StorageOperationFailures.IsSuccess(craftFailure))
                {
                    var result = RecipeResolver.ExecutePlan(plan, diskIds, cleanCraft);
                    if (result.IsAir)
                    {
                        craftFailure = StorageOperationFailure.CraftCostingNoLongerHolds;
                    }
                    else if (craftToInventory)
                    {
                        // Send entire result to client's inventory.
                        SendGiveItemToClient(mod, whoAmI, result);
                    }
                    else
                    {
                        int leftover = StorageWorldSystem.Instance.InsertItem(diskIds, result);
                        if (leftover > 0)
                        {
                            // Storage is full — send the remainder to the client so it
                            // can add it to its own inventory directly. Calling GetItem
                            // server-side does not reliably sync to the client.
                            result.stack = leftover;
                            SendGiveItemToClient(mod, whoAmI, result);
                        }
                    }
                }

                EndTrackingAndRespond(mod, whoAmI, craftFailure, diskIds);
            }
        }

        // ─── DiskData Sync ──────────────────────────────────────────────

        // Client requests DiskData for a set of disk IDs (sent when opening Terminal).
        private static void DBG(string msg)
        {
            var path = Requisition.DebugLogPath;
            if (path == null) return;
            try
            {
                using var fs = new System.IO.FileStream(path, System.IO.FileMode.Append, System.IO.FileAccess.Write, System.IO.FileShare.ReadWrite);
                using var sw = new System.IO.StreamWriter(fs);
                sw.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}][net={Main.netMode}] {msg}");
            }
            catch { /* never let logging crash packet handling */ }
        }

        public static void SendRequestDiskData(Mod mod, List<Guid> diskIds)
        {
            if (Main.netMode != NetmodeID.MultiplayerClient)
                return;

            DBG($"SendRequestDiskData: sending {diskIds.Count} ids: {string.Join(", ", diskIds.Select(g => g.ToString()[..8]))}");

            var packet = mod.GetPacket();
            packet.Write((byte)PacketType.RequestDiskData);
            packet.Write(diskIds.Count);
            foreach (var id in diskIds)
                packet.Write(id.ToByteArray());
            packet.Send();
        }

        private static void HandleRequestDiskData(Mod mod, BinaryReader reader, int whoAmI)
        {
            int count = reader.ReadInt32();
            var diskIds = ReadGuidList(reader, count);
            if (diskIds == null)
                return;

            DBG($"HandleRequestDiskData: received {count} ids from whoAmI={whoAmI}: {string.Join(", ", diskIds.Select(g => g.ToString()[..8]))}");

            if (Main.netMode == NetmodeID.Server)
            {
                var sys = StorageWorldSystem.Instance;
                DBG($"  allDiskData before ensure: [{string.Join(", ", sys.GetAllDiskData().Select(d => d.DiskId.ToString()[..8]))}]");
                EnsureDisksRegistered(diskIds);
                DBG($"  allDiskData after ensure:  [{string.Join(", ", sys.GetAllDiskData().Select(d => d.DiskId.ToString()[..8]))}]");
                SendDiskDataToClient(mod, diskIds, whoAmI);
            }
        }

        // Scans all Drive Bay entities and registers any disks not yet in StorageWorldSystem.
        // Only runs on the server and only when there are missing IDs, so overhead is minimal.
        private static void EnsureDisksRegistered(List<Guid> diskIds)
        {
            var sys = StorageWorldSystem.Instance;
            if (diskIds.TrueForAll(id => sys.HasDiskData(id)))
            {
                DBG($"  EnsureDisksRegistered: all {diskIds.Count} already registered, skip scan");
                return;
            }

            int bayCount = 0;
            foreach (var kvp in Terraria.DataStructures.TileEntity.ByID)
            {
                if (kvp.Value is DriveBayEntity sbe)
                {
                    var ids = sbe.GetInsertedDiskIds(); // registers disks as a side effect
                    DBG($"  EnsureDisksRegistered: bay {kvp.Key} has {ids.Count} disks: [{string.Join(", ", ids.Select(g => g.ToString()[..8]))}]");
                    bayCount++;
                }
            }
            DBG($"  EnsureDisksRegistered: scanned {bayCount} bays");
        }

        // ─── Chunked Disk Packet Helper ────────────────────────────────

        // Sends a single disk's SyncDiskData, automatically chunking if the
        // serialized payload exceeds tModLoader's 65,535-byte packet limit.
        private static void SendDiskPacket(Mod mod, DiskData data, int seqNum,
            int toClient = -1, int ignoreClient = -1)
        {
            byte[] payload;
            using (var ms = new MemoryStream())
            using (var bw = new BinaryWriter(ms))
            {
                data.WriteNet(bw);
                payload = ms.ToArray();
            }

            // 40 bytes of overhead: PacketType + count + seqNum + tModLoader framing.
            if (payload.Length + 40 <= 65000)
            {
                var packet = mod.GetPacket();
                packet.Write((byte)PacketType.SyncDiskData);
                packet.Write(1); // count
                packet.Write(seqNum);
                packet.BaseStream.Write(payload, 0, payload.Length);
                packet.Send(toClient, ignoreClient);
            }
            else
            {
                // Chunk the payload into pieces that fit in a single packet.
                // Header per chunk: PacketType(1) + diskId(16) + seqNum(4) + chunkIdx(2)
                //                   + totalChunks(2) + dataLength(4) = 29 bytes + tML framing.
                const int chunkDataSize = 50000;
                int totalChunks = (payload.Length + chunkDataSize - 1) / chunkDataSize;
                var diskId = data.DiskId;

                DBG($"SendDiskPacket: disk {diskId.ToString()[..8]} payload={payload.Length} bytes, splitting into {totalChunks} chunks");

                for (int i = 0; i < totalChunks; i++)
                {
                    int offset = i * chunkDataSize;
                    int length = Math.Min(chunkDataSize, payload.Length - offset);

                    var packet = mod.GetPacket();
                    packet.Write((byte)PacketType.SyncDiskDataChunked);
                    packet.Write(diskId.ToByteArray());
                    packet.Write(seqNum);
                    packet.Write((ushort)i);
                    packet.Write((ushort)totalChunks);
                    packet.Write(length);
                    packet.Write(payload, offset, length);
                    packet.Send(toClient, ignoreClient);
                }
            }
        }

        private class ChunkBuffer
        {
            public int SeqNum;
            public ushort TotalChunks;
            public byte[][] Chunks;
            public int Received;
        }

        private static readonly Dictionary<Guid, ChunkBuffer> _chunkBuffers = new();

        private static void HandleSyncDiskDataChunked(BinaryReader reader)
        {
            if (Main.netMode != NetmodeID.MultiplayerClient) return;

            var diskId = new Guid(reader.ReadBytes(16));
            int seqNum = reader.ReadInt32();
            ushort chunkIndex = reader.ReadUInt16();
            ushort totalChunks = reader.ReadUInt16();
            int dataLength = reader.ReadInt32();

            // ReadBytes allocates the whole length before reading it, so this is the same
            // allocate-from-a-lie shape as a list capacity. The sender writes 50,000-byte chunks.
            if (!WireCount.FitsInOnePacket(dataLength, 1))
                return;

            byte[] data = reader.ReadBytes(dataLength);

            // Checked before the buffer below is sized from totalChunks, not after: the same
            // check-before-you-allocate rule the count bounds exist for.
            if (totalChunks == 0 || chunkIndex >= totalChunks)
                return;

            if (!_chunkBuffers.TryGetValue(diskId, out var buf) || buf.SeqNum != seqNum)
            {
                buf = new ChunkBuffer
                {
                    SeqNum = seqNum,
                    TotalChunks = totalChunks,
                    Chunks = new byte[totalChunks][],
                    Received = 0
                };
                _chunkBuffers[diskId] = buf;
            }

            // The buffer may have been sized by an earlier packet claiming a different total.
            if (chunkIndex >= buf.TotalChunks)
                return;

            // Counting a chunk that already arrived would let a repeat stand in for one still
            // missing, and the reassembly below would then read a null chunk.
            if (buf.Chunks[chunkIndex] != null)
                return;

            buf.Chunks[chunkIndex] = data;
            buf.Received++;

            if (buf.Received == buf.TotalChunks)
            {
                using var ms = new MemoryStream();
                for (int i = 0; i < buf.TotalChunks; i++)
                    ms.Write(buf.Chunks[i], 0, buf.Chunks[i].Length);
                ms.Position = 0;

                using var br = new BinaryReader(ms);
                var diskData = DiskData.ReadNet(br);
                if (diskData == null)
                {
                    _chunkBuffers.Remove(diskId);
                    return;
                }

                var sys = StorageWorldSystem.Instance;
                sys.ApplyDiskDataFromNetwork(diskData);
                sys.SetDiskSeqNum(diskId, seqNum);

                _chunkBuffers.Remove(diskId);
                RefreshAllDriveBays();
                DBG($"HandleSyncDiskDataChunked: reassembled {totalChunks} chunks for disk {diskId.ToString()[..8]} seq={seqNum}");
            }
            else
            {
                DBG($"HandleSyncDiskDataChunked: buffered chunk {chunkIndex + 1}/{totalChunks} for disk {diskId.ToString()[..8]}");
            }
        }

        // Server sends DiskData for specific disks to a specific client.
        private static void SendDiskDataToClient(Mod mod, List<Guid> diskIds, int toClient)
        {
            var sys = StorageWorldSystem.Instance;
            var dataToSend = new List<DiskData>();
            foreach (var id in diskIds)
            {
                var data = sys.GetDiskData(id);
                DBG($"  SendDiskDataToClient: GetDiskData({id.ToString()[..8]}) = {(data == null ? "NULL" : $"tier={data.Tier} used={data.UsedStacks}")}");
                if (data != null)
                    dataToSend.Add(data);
            }

            DBG($"  SendDiskDataToClient: sending {dataToSend.Count} disks to client {toClient}");

            foreach (var data in dataToSend)
                SendDiskPacket(mod, data, sys.GetDiskSeqNum(data.DiskId), toClient);
        }

        // Broadcasts DiskData for the given disk IDs to all clients.
        public static void BroadcastDiskData(Mod mod, List<Guid> diskIds, int ignoreClient)
        {
            if (Main.netMode != NetmodeID.Server)
                return;

            var sys = StorageWorldSystem.Instance;
            var dataToSend = new List<DiskData>();
            foreach (var id in diskIds)
            {
                var data = sys.GetDiskData(id);
                if (data != null)
                    dataToSend.Add(data);
            }

            if (dataToSend.Count == 0)
                return;

            DBG($"BroadcastDiskData: {dataToSend.Count} disks ignoreClient={ignoreClient}");

            foreach (var data in dataToSend)
                SendDiskPacket(mod, data, sys.GetDiskSeqNum(data.DiskId), -1, ignoreClient);
        }

        private static void HandleSyncDiskData(BinaryReader reader)
        {
            // Server-authoritative state arriving from a client is not a correction, it is a forgery.
            // ApplyDiskDataFromNetwork guards itself, but SetDiskSeqNum and RefreshAllDriveBays below
            // would still run for whatever GUIDs the packet named.
            if (Main.netMode != NetmodeID.MultiplayerClient) return;

            try
            {
                int count = reader.ReadInt32();
                int seqNum = reader.ReadInt32();
                var sys = StorageWorldSystem.Instance;
                for (int i = 0; i < count; i++)
                {
                    var data = DiskData.ReadNet(reader);
                    // Break rather than return: the disks already applied still need the refresh.
                    if (data == null)
                        break;

                    sys.ApplyDiskDataFromNetwork(data);
                    sys.SetDiskSeqNum(data.DiskId, seqNum);
                }
                RefreshAllDriveBays();
                DBG($"HandleSyncDiskData: applied {count} disk(s) seq={seqNum}");
            }
            catch (Exception ex)
            {
                DBG($"HandleSyncDiskData: EXCEPTION {ex.GetType().Name}: {ex.Message}");
            }
        }

        private static void RefreshAllDriveBays()
        {
            foreach (var kvp in TileEntity.ByID)
            {
                if (kvp.Value is DriveBayEntity bay)
                    bay.RefreshVisualState(StorageNetwork.HasTerminalNearby(bay.Position));
            }
        }

        // ─── Full DriveBay Sync ─────────────────────────────────────

        private static void HandleSyncDriveBay(Mod mod, BinaryReader reader, int whoAmI)
        {
            // Server → client only. Without this a client could rewrite all 40 slots of any bay.
            if (Main.netMode != NetmodeID.MultiplayerClient) return;

            int entityId = reader.ReadInt32();
            if (Terraria.DataStructures.TileEntity.ByID.TryGetValue(entityId, out var entity)
                && entity is DriveBayEntity sbe)
            {
                for (int i = 0; i < DriveBayEntity.DiskSlotCount; i++)
                {
                    sbe.DiskSlots[i] = ItemIO.Receive(reader, true);
                }
            }
        }

        // ─── Disk Archive ───────────────────────────────────────────────

        // Client requests the server to archive the disk at the given inventory slot.
        // The GUID is included so the server can look it up in StorageWorldSystem without
        // relying on a fully-synced copy of the player's inventory mod data.
        public static void SendArchiveDiskRequest(Mod mod, int slot, Guid diskId)
        {
            if (Main.netMode != NetmodeID.MultiplayerClient)
                return;

            var packet = mod.GetPacket();
            packet.Write((byte)PacketType.ArchiveDiskRequest);
            packet.Write(slot);
            packet.Write(diskId.ToByteArray());
            packet.Send();
        }

        private static void HandleArchiveDiskRequest(Mod mod, BinaryReader reader, int whoAmI)
        {
            int slot = reader.ReadInt32();
            var diskId = new Guid(reader.ReadBytes(16));

            if (Main.netMode != NetmodeID.Server)
                return;

            // whoAmI, never an index off the wire: the sender is the only player whose inventory
            // this may read, and an out-of-range value would index Netplay.Clients and throw.
            var player = Main.player[whoAmI];
            if (slot < 0 || slot >= player.inventory.Length)
                return;

            var invItem = player.inventory[slot];
            if (invItem == null || invItem.IsAir || invItem.ModItem is not StorageDiskBase disk)
                return;

            // The GUID must be the one on the disk actually held in that slot. Archiving whatever
            // GUID the packet named would hand the sender any disk's contents and erase it for
            // everyone else.
            if (disk.DiskId != diskId)
                return;

            // Extract items from world storage and embed them in the disk item.
            var items = StorageWorldSystem.Instance.ArchiveDisk(diskId);
            disk.DiskId = Guid.Empty;
            disk.IsArchived = true;
            disk.ArchivedItems = items;

            // Broadcast the GUID removal to all clients so their _allDiskData stays in sync.
            SendSyncRemoveDiskData(mod, diskId);

            // Send the updated disk item back to the requesting client.
            var packet = mod.GetPacket();
            packet.Write((byte)PacketType.ArchiveDiskResult);
            packet.Write(slot);
            ItemIO.Send(invItem, packet, true);
            packet.Send(whoAmI);
        }

        public static void SendSyncRemoveDiskData(Mod mod, Guid diskId)
        {
            if (Main.netMode != NetmodeID.Server) return;
            var packet = mod.GetPacket();
            packet.Write((byte)PacketType.SyncRemoveDiskData);
            packet.Write(diskId.ToByteArray());
            packet.Send(); // broadcast to all clients
        }

        private static void HandleSyncRemoveDiskData(BinaryReader reader)
        {
            var diskId = new Guid(reader.ReadBytes(16));
            if (Main.netMode != NetmodeID.MultiplayerClient) return;
            StorageWorldSystem.Instance?.RemoveDiskData(diskId);
            StorageWorldSystem.Instance?.RemoveDiskSeqNum(diskId);
        }

        private static void HandleArchiveDiskResult(BinaryReader reader)
        {
            int slot = reader.ReadInt32();
            var item = ItemIO.Receive(reader, true);

            if (Main.netMode == NetmodeID.MultiplayerClient)
                Main.LocalPlayer.inventory[slot] = item;
        }

        // ─── Disk Recovery ──────────────────────────────────────────────

        // Client asks the server to remap oldGuid→newId in StorageWorldSystem
        // (dupe-safe recovery: old GUID is deleted so the original disk becomes empty).
        // repDiskOldId is the replacement disk's previous GUID (Guid.Empty if blank).
        public static void SendRestoreDiskRequest(Mod mod, Guid oldGuid, Guid repDiskOldId, Guid newId)
        {
            if (Main.netMode != NetmodeID.MultiplayerClient) return;
            var packet = mod.GetPacket();
            packet.Write((byte)PacketType.RestoreDiskRequest);
            packet.Write(oldGuid.ToByteArray());
            packet.Write(repDiskOldId.ToByteArray());
            packet.Write(newId.ToByteArray());
            packet.Send();
        }

        private static void HandleRestoreDiskRequest(Mod mod, BinaryReader reader, int whoAmI)
        {
            var oldGuid      = new Guid(reader.ReadBytes(16));
            var repDiskOldId = new Guid(reader.ReadBytes(16));
            var newId        = new Guid(reader.ReadBytes(16));

            if (Main.netMode != NetmodeID.Server) return;

            var sys = StorageWorldSystem.Instance;
            if (sys == null) return;

            // RemapDiskData moves a disk's entire contents to a caller-chosen GUID, so this has to
            // establish two things the packet cannot: that the sender really holds the replacement
            // disk, and that the disk being recovered is genuinely lost. Without the second check a
            // player holding any blank disk could name someone else's live disk and take it.
            if (!PlayerHoldsDisk(whoAmI, repDiskOldId))
                return;

            // The recovery list shows every non-empty disk, not only lost ones, so a player may
            // legitimately pick one whose physical disk still exists — as long as it is their own.
            // Remapping a GUID that lives in someone else's inventory or in a Drive Bay is the
            // theft case, and that is what this refuses.
            if (IsDiskGuidInUse(oldGuid) && !PlayerHoldsDisk(whoAmI, oldGuid))
                return;

            if (newId == Guid.Empty || sys.GetDiskData(newId) != null)
                return;

            // Clean up the replacement disk's old entry if empty.
            if (repDiskOldId != Guid.Empty)
            {
                var existing = sys.GetDiskData(repDiskOldId);
                if (existing == null || existing.UsedStacks == 0)
                {
                    sys.RemoveDiskData(repDiskOldId);
                    SendSyncRemoveDiskData(mod, repDiskOldId);
                }
            }

            sys.RemapDiskData(oldGuid, newId);
            sys.RemoveDiskSeqNum(oldGuid);
            sys.IncrementDiskSeqNum(newId);
            SendSyncRemoveDiskData(mod, oldGuid);
            BroadcastDiskData(mod, new System.Collections.Generic.List<Guid> { newId }, -1);
        }

        // ─── Disk Upgrade ───────────────────────────────────────────────

        // Client asks the server to perform a disk tier upgrade in the given Drive Bay slot.
        public static void SendUpgradeDiskRequest(Mod mod, int entityId, int slotIdx, Guid diskId,
            System.Collections.Generic.List<Guid> diskIds, int optionIdx,
            System.Collections.Generic.HashSet<int> stations,
            System.Collections.Generic.HashSet<CraftingCondition> conditions)
        {
            if (Main.netMode != NetmodeID.MultiplayerClient) return;
            var packet = mod.GetPacket();
            packet.Write((byte)PacketType.UpgradeDiskRequest);
            packet.Write(entityId);
            packet.Write(slotIdx);
            packet.Write(diskId.ToByteArray());
            packet.Write(diskIds.Count);
            foreach (var id in diskIds) packet.Write(id.ToByteArray());
            packet.Write(optionIdx);
            packet.Write(stations.Count);
            foreach (int s in stations) packet.Write(s);
            packet.Write(conditions.Count);
            foreach (var c in conditions) packet.Write((byte)c);
            packet.Send();
        }

        private static void HandleUpgradeDiskRequest(Mod mod, BinaryReader reader)
        {
            int entityId  = reader.ReadInt32();
            int slotIdx   = reader.ReadInt32();
            var diskId    = new Guid(reader.ReadBytes(16));
            int diskCount = reader.ReadInt32();
            // Read to advance the stream; the network is re-derived below. Still has to be a count
            // that could describe a real list, or the rest of this packet is read from nowhere.
            var packetDiskIds = ReadGuidList(reader, diskCount);
            if (packetDiskIds == null)
                return;

            int optionIdx = reader.ReadInt32();
            int staCnt    = reader.ReadInt32();
            var stations  = new System.Collections.Generic.HashSet<int>();
            for (int i = 0; i < staCnt; i++) stations.Add(reader.ReadInt32());
            int conCnt     = reader.ReadInt32();
            var conditions = new System.Collections.Generic.HashSet<CraftingCondition>();
            for (int i = 0; i < conCnt; i++) conditions.Add((CraftingCondition)reader.ReadByte());

            if (Main.netMode != NetmodeID.Server) return;

            if (!Terraria.DataStructures.TileEntity.ByID.TryGetValue(entityId, out var entity)
                || entity is not DriveBayEntity bay) return;

            bay.EnsureSlotsInitialized();
            if (slotIdx < 0 || slotIdx >= DriveBayEntity.DiskSlotCount) return;
            if (bay.DiskSlots[slotIdx]?.ModItem is not StorageDiskBase disk) return;
            if (disk.DiskId != diskId) return;

            var opts = StorageDiskBase.GetUpgradeOptions(disk.Tier);
            if (opts == null || optionIdx < 0 || optionIdx >= opts.Length) return;
            var option   = opts[optionIdx];
            var nextTier = (DiskTier)((int)disk.Tier + 1);
            var sys      = StorageWorldSystem.Instance;

            // The packet's disk list is client-supplied: re-derive the network from the bay itself so
            // a crafted client cannot name disks it is nowhere near, or repeat one to inflate counts.
            var networkDiskIds = StorageNetwork.GetAllConnectedDiskIds(bay.Position);

            sys.BeginModificationTracking();

            // Affordability is re-checked here, not trusted from the client: the panel's gate lives
            // on the client only, and storage can change between its check and this packet arriving.
            // TryConsumeMaterials is all-or-nothing, so a shortfall leaves storage untouched.
            if (!RecipeResolver.TryConsumeMaterials(networkDiskIds, option, stations, conditions))
            {
                EndTrackingAndBroadcast(mod);
                return;
            }

            // Build upgraded disk item, carry GUID, upgrade tier in world storage.
            var newItem = new Item();
            newItem.SetDefaults(StorageDiskBase.GetItemTypeForTier(nextTier));
            if (newItem.ModItem is StorageDiskBase newDisk)
            {
                newDisk.AssignDiskId(diskId);
                sys.UpgradeDisk(diskId, nextTier);
            }
            bay.DiskSlots[slotIdx] = newItem.Clone();

            // Sync the bay slots and disk data to all clients.
            SendSyncDriveBay(mod, bay);
            EndTrackingAndBroadcast(mod);
        }

        public static void SendSyncDriveBay(Mod mod, DriveBayEntity bay, int toClient = -1)
        {
            if (Main.netMode != NetmodeID.Server) return;
            var packet = mod.GetPacket();
            packet.Write((byte)PacketType.SyncDriveBay);
            packet.Write(bay.ID);
            for (int i = 0; i < DriveBayEntity.DiskSlotCount; i++)
                ItemIO.Send(bay.DiskSlots[i] ?? new Item(), packet, true);
            packet.Send(toClient);
        }

        // ─── Defragment ─────────────────────────────────────────────────

        public static void SendDefragRequest(Mod mod, System.Collections.Generic.List<Guid> diskIds)
        {
            if (Main.netMode != NetmodeID.MultiplayerClient) return;
            var packet = mod.GetPacket();
            packet.Write((byte)PacketType.DefragRequest);
            packet.Write(diskIds.Count);
            foreach (var id in diskIds) packet.Write(id.ToByteArray());
            packet.Send();
        }

        private static void HandleDefragRequest(Mod mod, BinaryReader reader, int whoAmI)
        {
            int count = reader.ReadInt32();

            if (Main.netMode != NetmodeID.Server) return;

            var sys = StorageWorldSystem.Instance;
            if (sys == null) return;

            // Defragment moves stacks from later disks into earlier ones, so an arbitrary disk
            // list — in an attacker-chosen order — drains one player's disk into another's.
            // Only disks the sender is actually standing at may take part.
            var reachable = GetReachableDiskIds(whoAmI);

            // The count is read straight off the wire, and ReadGuidList pre-sizes a list from it:
            // left unbounded that is a remote out-of-memory. A defrag can never involve more disks
            // than the sender can reach, so that is the ceiling.
            if (count < 0 || count > reachable.Count) return;

            var diskIds = ReadGuidList(reader, count);
            if (diskIds == null)
                return;

            foreach (var diskId in diskIds)
            {
                if (!reachable.Contains(diskId))
                    return;
            }

            // Every id passing the reachability check does not make the list a defrag request: one
            // reachable id repeated fills it legally, and Defragment's sweep is quadratic in the
            // number of disks. A real request names each disk once.
            var distinctDiskIds = new HashSet<Guid>(diskIds);
            if (distinctDiskIds.Count != diskIds.Count) return;

            var modified = sys.Defragment(diskIds);
            if (modified.Count > 0)
            {
                // Defrag is a rare bulk operation — bump seq nums and broadcast full disk state
                foreach (var id in modified)
                    sys.IncrementDiskSeqNum(id);
                BroadcastDiskData(mod, modified, -1);
            }
        }

        // ─── Sync Dispatch ──────────────────────────────────────────────

        // Server → specific client: "put this item in your inventory."
        private static void SendGiveItemToClient(Mod mod, int toClient, Item item)
        {
            var packet = mod.GetPacket();
            packet.Write((byte)PacketType.GiveItemToClient);
            packet.Write(item.type);
            packet.Write(item.stack);
            packet.Write((byte)item.prefix);
            packet.Send(toClient);
        }

        // Client-side: server told us to take an item into our inventory.
        private static void HandleGiveItemToClient(BinaryReader reader)
        {
            if (Main.netMode != NetmodeID.MultiplayerClient)
                return;

            int type  = reader.ReadInt32();
            int stack = reader.ReadInt32();
            int prefix = reader.ReadByte();

            var item = new Item();
            item.SetDefaults(type);
            item.stack = stack;
            if (prefix > 0)
                item.Prefix(prefix);

            Main.LocalPlayer.GetItem(Main.myPlayer, item, GetItemSettings.GetItemInDropItemCheck);
        }

        // Returns true if the player's main inventory has at least one slot that can accept the item.
        private static bool PlayerHasRoomFor(Player player, Item item)
        {
            for (int i = 0; i < 50; i++)
            {
                var slot = player.inventory[i];
                if (slot.IsAir) return true;
                if (slot.type == item.type && slot.prefix == item.prefix && slot.stack < item.maxStack)
                    return true;
            }
            return false;
        }

        // Ends modification tracking and broadcasts item-level deltas to all clients.
        private static void EndTrackingAndBroadcast(Mod mod)
        {
            var sys = StorageWorldSystem.Instance;
            var (_, deltas) = sys.EndModificationTrackingWithDeltas();
            if (deltas.Count > 0)
                BroadcastDiskDeltas(mod, deltas);
        }

        // Ends modification tracking, sends OperationResponse to the requester,
        // then broadcasts item-level deltas to all clients.
        // On failure, sends denial + full disk correction packets.
        //
        // Takes the cause rather than a success flag: success is derived from it here and nowhere
        // else, so a caller cannot report a denial that names no reason, or a success that does.
        private static void EndTrackingAndRespond(Mod mod, int toClient,
            StorageOperationFailure failure, List<Guid> requestedDiskIds = null)
        {
            var sys = StorageWorldSystem.Instance;
            var (_, deltas) = sys.EndModificationTrackingWithDeltas();
            bool success = StorageOperationFailures.IsSuccess(failure);

            if (success && deltas.Count > 0)
            {
                SendOperationResponse(mod, toClient, StorageOperationFailure.None);
                BroadcastDiskDeltas(mod, deltas);
            }
            else if (!success)
            {
                // Denied: send failure response + full disk corrections
                SendOperationResponse(mod, toClient, failure, requestedDiskIds);
            }
            else
            {
                // Success but no changes (e.g. deposit into a full disk) — still confirm
                SendOperationResponse(mod, toClient, StorageOperationFailure.None);
            }
        }

        // A handler that refuses before it begins modification tracking still owes the client an
        // answer. SendOperationResponse touches no tracking, so these paths respond directly
        // rather than tearing down a tracker that was never started.
        private static void RefuseOperation(Mod mod, int toClient, StorageOperationFailure failure)
        {
            SendOperationResponse(mod, toClient, failure);
        }

        // Every disk the sender can legitimately reach: the union of the networks of the Terminals
        // within range of them. Disk GUIDs travel to every client (StorageDiskBase.NetSend sends
        // the full 16 bytes), so a client naming a GUID proves nothing about whether it may touch
        // it — bulk operations have to be scoped to what the player is actually standing at.
        private static HashSet<Guid> GetReachableDiskIds(int whoAmI)
        {
            var reachable = new HashSet<Guid>();

            if (whoAmI < 0 || whoAmI >= Main.player.Length)
                return reachable;

            var player = Main.player[whoAmI];
            if (player == null || !player.active)
                return reachable;

            foreach (var kvp in TileEntity.ByID)
            {
                if (kvp.Value is not TerminalEntity terminal)
                    continue;

                float dx = player.Center.X - (terminal.Position.X * 16f + 24f);
                float dy = player.Center.Y - (terminal.Position.Y * 16f + 24f);
                if (dx * dx + dy * dy > TerminalRangeSq)
                    continue;

                foreach (var diskId in StorageNetwork.GetAllConnectedDiskIds(terminal.Position))
                    reachable.Add(diskId);
            }

            return reachable;
        }

        // 15 tiles, matching the range quick-stack and position-deposit already enforce.
        private const float TerminalRangeSq = 240f * 240f;

        // Whether the client that sent a packet may name this disk GUID. Disk GUIDs reach every
        // client (StorageDiskBase.NetSend sends all 16 bytes), so the GUID itself establishes
        // nothing — either no physical disk carries it, or the sender is the one carrying it.
        private static bool SenderMayClaimDisk(int whoAmI, Guid diskId)
        {
            bool diskGuidInUse = IsDiskGuidInUse(diskId);
            bool senderHoldsDisk = PlayerHoldsDisk(whoAmI, diskId);

            return DiskClaim.SenderMayClaim(diskId, diskGuidInUse, senderHoldsDisk);
        }

        // Turn away a disk insert without costing the sender the disk or leaving it believing the
        // insert happened. Both matter: the client puts the disk into its own copy of the bay and
        // empties its cursor before the packet is sent, so a refusal that said nothing would leave
        // that client showing a disk the server does not have, while the disk itself existed nowhere.
        private static void RefuseInsert(Mod mod, int toClient, DriveBayEntity bay, Item item)
        {
            if (bay != null)
                SendSyncDriveBay(mod, bay, toClient);

            // Only ever a Storage Disk. The sender gave one up to send this packet; anything else in
            // that slot was never theirs to be handed back.
            if (item == null || item.IsAir || item.ModItem is not StorageDiskBase)
                return;

            SendReturnItemToClient(mod, toClient, item);
        }

        // True if this player physically holds a Storage Disk carrying that GUID.
        private static bool PlayerHoldsDisk(int whoAmI, Guid diskId)
        {
            if (whoAmI < 0 || whoAmI >= Main.player.Length)
                return false;

            var player = Main.player[whoAmI];
            if (player == null || !player.active)
                return false;

            foreach (var item in player.inventory)
            {
                if (item != null && !item.IsAir
                    && item.ModItem is StorageDiskBase held && held.DiskId == diskId)
                    return true;
            }

            return false;
        }

        // True if some physical disk in the world still carries that GUID — in a Drive Bay slot or
        // in any player's inventory. Recovery only applies to data whose disk is actually lost.
        private static bool IsDiskGuidInUse(Guid diskId)
        {
            if (diskId == Guid.Empty)
                return true;

            foreach (var kvp in TileEntity.ByID)
            {
                if (kvp.Value is not DriveBayEntity bay)
                    continue;

                foreach (var slotItem in bay.DiskSlots)
                {
                    if (slotItem != null && !slotItem.IsAir
                        && slotItem.ModItem is StorageDiskBase bayDisk && bayDisk.DiskId == diskId)
                        return true;
                }
            }

            for (int i = 0; i < Main.player.Length; i++)
            {
                if (Main.player[i] != null && Main.player[i].active && PlayerHoldsDisk(i, diskId))
                    return true;
            }

            return false;
        }

        // ─── Delta Sync (Predictive Mode) ──────────────────────────────

        // Broadcasts item-level deltas for modified disks to all clients.
        // Called instead of BroadcastDiskData when predictive sync is active. 
        public static void BroadcastDiskDeltas(Mod mod, Dictionary<Guid, DiskDelta> deltas)
        {
            if (Main.netMode != NetmodeID.Server)
                return;

            foreach (var kvp in deltas)
            {
                var packet = mod.GetPacket();
                packet.Write((byte)PacketType.DeltaDiskData);
                packet.Write(kvp.Key.ToByteArray()); // diskGuid
                kvp.Value.WriteNet(packet);
                packet.Send(); // broadcast to all clients
            }
        }

        // Sends an operation response (success/failure) to the requesting client.
        // On failure, also sends full SyncDiskData correction packets for all affected disks.
        public static void SendOperationResponse(Mod mod, int toClient,
            StorageOperationFailure failure, List<Guid> affectedDiskIds = null)
        {
            if (Main.netMode != NetmodeID.Server)
                return;

            bool success = StorageOperationFailures.IsSuccess(failure);

            var packet = mod.GetPacket();
            packet.Write((byte)PacketType.OperationResponse);
            packet.Write(success);

            // Appended last and only on a denial, so a success response stays the two bytes it has
            // always been and the cause can never contradict the flag it travels behind.
            if (!success)
                packet.Write((byte)failure);

            packet.Send(toClient);

            // On failure, send full disk state corrections for all affected disks
            if (!success && affectedDiskIds != null)
            {
                var sys = StorageWorldSystem.Instance;
                foreach (var diskId in affectedDiskIds)
                {
                    var data = sys.GetDiskData(diskId);
                    if (data == null) continue;
                    SendDiskPacket(mod, data, sys.GetDiskSeqNum(diskId), toClient);
                }
            }
        }

        private static void HandleDeltaDiskData(BinaryReader reader)
        {
            if (Main.netMode != NetmodeID.MultiplayerClient) return;

            var diskId = new Guid(reader.ReadBytes(16));
            var delta = DiskDelta.ReadNet(reader);
            var sys = StorageWorldSystem.Instance;

            // Sequence gap check: if the delta's seq is not exactly lastSeen + 1, request full resync
            int lastSeen = sys.GetDiskSeqNum(diskId);
            if (delta.SeqNum != lastSeen + 1)
            {
                DBG($"HandleDeltaDiskData: seq gap for disk {diskId.ToString()[..8]}: expected {lastSeen + 1}, got {delta.SeqNum}. Requesting full sync.");
                SendRequestFullDiskSync(ModContent.GetInstance<Requisition>(), diskId);
                return;
            }

            // Apply the delta to local disk data
            var diskData = sys.GetDiskData(diskId);
            if (diskData == null)
            {
                DBG($"HandleDeltaDiskData: disk {diskId.ToString()[..8]} not found locally, requesting full sync.");
                SendRequestFullDiskSync(ModContent.GetInstance<Requisition>(), diskId);
                return;
            }

            ApplyDeltaToDisk(diskData, delta);
            sys.SetDiskSeqNum(diskId, delta.SeqNum);
            sys.BumpStorageVersion();
            RefreshAllDriveBays();

            DBG($"HandleDeltaDiskData: applied delta seq={delta.SeqNum} to disk {diskId.ToString()[..8]}, {delta.ChangedItems.Count} item changes");
        }

        // Applies a DiskDelta to a local DiskData, modifying item stacks in-place.
        private static void ApplyDeltaToDisk(DiskData disk, DiskDelta delta)
        {
            foreach (var entry in delta.ChangedItems)
            {
                if (entry.NewStack == 0)
                {
                    // Item fully removed — remove all matching stacks
                    disk.Items.RemoveAll(s =>
                        !s.IsUnique && s.ItemType == entry.ItemType && s.PrefixId == entry.PrefixId);
                }
                else
                {
                    // Find existing stack and update, or add new one
                    StoredItemStack existing = null;
                    int currentTotal = 0;
                    foreach (var s in disk.Items)
                    {
                        if (!s.IsUnique && s.ItemType == entry.ItemType && s.PrefixId == entry.PrefixId)
                        {
                            existing ??= s;
                            currentTotal += s.Stack;
                        }
                    }

                    if (existing != null)
                    {
                        // Adjust the first matching stack by the difference, keeping it simple.
                        // The server's full state is authoritative; this is close enough for UI display.
                        int diff = entry.NewStack - currentTotal;
                        existing.Stack += diff;
                        if (existing.Stack <= 0)
                        {
                            disk.Items.Remove(existing);
                        }
                    }
                    else
                    {
                        // New item on this disk
                        disk.Items.Add(new StoredItemStack
                        {
                            ItemType = entry.ItemType,
                            PrefixId = entry.PrefixId,
                            Stack = entry.NewStack,
                            InsertionOrder = 0
                        });
                    }
                }
            }

            // Replace all items that stand for themselves with the authoritative after-state
            disk.Items.RemoveAll(s => s.IsUnique);
            disk.Items.AddRange(delta.UniqueItemsAfter);
        }

        private static void HandleOperationResponse(BinaryReader reader)
        {
            // Consumed before the netMode guard, the way every handler in this file consumes its
            // payload before branching. The reader is one stream over the shared connection
            // buffer, so a read this side skips is a byte the next packet inherits.
            bool success = reader.ReadBoolean();

            byte reasonByte = 0;
            var failure = StorageOperationFailure.None;
            if (!success)
            {
                reasonByte = reader.ReadByte();
                failure = StorageOperationFailures.GetFailureFromWireValue(reasonByte);
            }

            if (Main.netMode != NetmodeID.MultiplayerClient) return;

            // On failure, correction packets (SyncDiskData) follow immediately and are
            // handled by HandleSyncDiskData which resets local state.
            DBG($"HandleOperationResponse: success={success} reasonByte={reasonByte} mapped={failure}");

            if (!success)
                StorageOperationReporter.ReportServerDenial(failure);
        }

        public static void SendRequestFullDiskSync(Mod mod, Guid diskId)
        {
            if (Main.netMode != NetmodeID.MultiplayerClient) return;

            var packet = mod.GetPacket();
            packet.Write((byte)PacketType.RequestFullDiskSync);
            packet.Write(diskId.ToByteArray());
            packet.Send();
        }

        private static void HandleRequestFullDiskSync(Mod mod, BinaryReader reader, int whoAmI)
        {
            var diskId = new Guid(reader.ReadBytes(16));

            if (Main.netMode != NetmodeID.Server) return;

            var sys = StorageWorldSystem.Instance;
            var data = sys.GetDiskData(diskId);
            if (data == null) return;

            // Send full disk state with current sequence number
            int seq = sys.GetDiskSeqNum(diskId);
            SendDiskPacket(mod, data, seq, whoAmI);

            DBG($"HandleRequestFullDiskSync: sent full state for disk {diskId.ToString()[..8]} seq={seq} to client {whoAmI}");
        }

        // ─── Quick Stack ────────────────────────────────────────────────

        public static void SendQuickStackToStorage(Mod mod, Point16 terminalPos, Player player)
        {
            if (Main.netMode != NetmodeID.MultiplayerClient) return;

            var candidates = new List<(byte slot, Item item)>();
            for (int i = 10; i < 50; i++)
            {
                var item = player.inventory[i];
                if (item.IsAir || item.favorited || item.IsACoin) continue;
                candidates.Add(((byte)i, item));
            }
            if (candidates.Count == 0) return;

            var packet = mod.GetPacket();
            packet.Write((byte)PacketType.QuickStackToStorage);
            packet.Write(terminalPos.X);
            packet.Write(terminalPos.Y);
            packet.Write((byte)candidates.Count);
            foreach (var (slot, item) in candidates)
            {
                packet.Write(slot);
                ItemIO.Send(item, packet, true);
            }
            packet.Send();
        }

        private static void HandleQuickStackToStorage(Mod mod, BinaryReader reader, int whoAmI)
        {
            short tx = reader.ReadInt16();
            short ty = reader.ReadInt16();
            int slotCount = reader.ReadByte();

            var slots = new List<(byte idx, Item item)>(slotCount);
            for (int i = 0; i < slotCount; i++)
            {
                byte idx = reader.ReadByte();
                var item = ItemIO.Receive(reader, true);
                slots.Add((idx, item));
            }

            if (Main.netMode != NetmodeID.Server) return;

            var terminalPos = new Point16(tx, ty);
            if (!TileEntity.ByPosition.TryGetValue(terminalPos, out var entity)
                || entity is not TerminalEntity)
            {
                RefuseOperation(mod, whoAmI, StorageOperationFailure.NoTerminalFound);
                return;
            }

            // Validate player is within range
            var player = Main.player[whoAmI];
            float dx = player.Center.X - (terminalPos.X * 16f + 24f);
            float dy = player.Center.Y - (terminalPos.Y * 16f + 24f);
            if (dx * dx + dy * dy > 240f * 240f) // 15 tiles in pixels
            {
                RefuseOperation(mod, whoAmI, StorageOperationFailure.NoStorageInRange);
                return;
            }

            var diskIds = StorageNetwork.GetAllConnectedDiskIds(terminalPos);
            if (diskIds.Count == 0)
            {
                RefuseOperation(mod, whoAmI, StorageOperationFailure.NoStorageConnected);
                return;
            }

            var existingTypes = StorageWorldSystem.Instance.GetItemCounts(diskIds);

            StorageWorldSystem.Instance.BeginModificationTracking();

            var results = new List<(byte slot, int newStack)>();
            bool matchedAnySlot = false;
            bool anyDeposited = false;

            foreach (var (slotIdx, item) in slots)
            {
                if (slotIdx >= player.inventory.Length) continue;

                // Deposit the server's copy of that slot, not the item the packet carried. The
                // packet only says WHICH slot to stack; taking its payload on trust would insert
                // any quantity of any type the network already holds.
                var held = player.inventory[slotIdx];
                if (held == null || held.IsAir) continue;
                if (held.type != item.type) continue;
                if (!existingTypes.ContainsKey(held.type)) continue;

                matchedAnySlot = true;

                // Read the offered count BEFORE the insert, the way HandleDepositItem does: a slot
                // that matched but bounced off a full network still lands in results, so the list's
                // length says only that something was tried, never that anything moved.
                int offered = held.stack;
                int leftover = StorageWorldSystem.Instance.InsertItem(diskIds, held);

                var outcome = new DepositOutcome(offered, leftover);
                if (outcome.AnyDeposited)
                    anyDeposited = true;

                results.Add((slotIdx, leftover));
            }

            var quickStackFailure = StorageOperationFailures.GetQuickStackFailure(matchedAnySlot, anyDeposited);

            // A slot that matched but bounced off a full network moved nothing, so there is no
            // client state to correct — only a reason to report. This case used to report success
            // and send nothing; sweeping every disk's full contents for it now would turn a
            // spammable button into a resync storm. The nothing-matched case keeps its corrections,
            // which it always sent.
            var disksNeedingCorrection = quickStackFailure == StorageOperationFailure.NothingDeposited
                ? null
                : diskIds;
            EndTrackingAndRespond(mod, whoAmI, quickStackFailure, disksNeedingCorrection);

            if (results.Count > 0)
            {
                var resultPacket = mod.GetPacket();
                resultPacket.Write((byte)PacketType.QuickStackResult);
                resultPacket.Write((byte)results.Count);
                foreach (var (slot, newStack) in results)
                {
                    resultPacket.Write(slot);
                    resultPacket.Write(newStack);
                }
                resultPacket.Send(whoAmI);
            }
        }

        private static void HandleQuickStackResult(BinaryReader reader)
        {
            if (Main.netMode != NetmodeID.MultiplayerClient) return;

            int count = reader.ReadByte();
            var player = Main.LocalPlayer;

            for (int i = 0; i < count; i++)
            {
                byte slotIdx = reader.ReadByte();
                int newStack = reader.ReadInt32();

                if (slotIdx >= 50) continue;

                if (newStack <= 0)
                    player.inventory[slotIdx].TurnToAir();
                else
                    player.inventory[slotIdx].stack = newStack;
            }
        }

        // ─── Helpers ────────────────────────────────────────────────────

        // Null when the count could not honestly describe a list this packet carried. Callers must
        // return on null rather than carry on: the count sizes the allocation before a single GUID
        // is read, and continuing past a count already known to be a lie reads the rest of the
        // packet from an offset that no longer means anything.
        private static List<Guid> ReadGuidList(BinaryReader reader, int count)
        {
            if (!WireCount.FitsInOnePacket(count, WireCount.GuidBytes))
                return null;

            var list = new List<Guid>(count);
            for (int i = 0; i < count; i++)
                list.Add(new Guid(reader.ReadBytes(16)));
            return list;
        }
    }
}
