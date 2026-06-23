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
                    HandleRestoreDiskRequest(mod, reader);
                    break;
                case PacketType.UpgradeDiskRequest:
                    HandleUpgradeDiskRequest(mod, reader);
                    break;
                case PacketType.DefragRequest:
                    HandleDefragRequest(mod, reader);
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

            DriveBayEntity sbe = null;
            if (Terraria.DataStructures.TileEntity.ByID.TryGetValue(entityId, out var entity)
                && entity is DriveBayEntity blockEntity)
            {
                sbe = blockEntity;
                if (Main.netMode == NetmodeID.Server)
                {
                    // InsertDisk assigns the GUID and registers the disk in StorageWorldSystem
                    // so clients can retrieve disk data by the correct GUID via RequestDiskData.
                    sbe.InsertDisk(item, slot);
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

            if (Terraria.DataStructures.TileEntity.ByID.TryGetValue(entityId, out var entity)
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
            packet.Write(Main.myPlayer);
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
            packet.Write(Main.myPlayer);
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
            packet.Write(Main.myPlayer);
            packet.Write(diskIds.Count);
            foreach (var id in diskIds)
                packet.Write(id.ToByteArray());
            TagIO.Write(fullItemTag, packet);
            packet.Write(shift);
            packet.Send();
        }

        private static void HandleWithdrawItemByFullItemTag(Mod mod, BinaryReader reader, int whoAmI)
        {
            int playerIndex = reader.ReadInt32();
            int diskCount = reader.ReadInt32();
            var diskIds = ReadGuidList(reader, diskCount);
            var fullItemTag = TagIO.Read(reader);
            bool shift = reader.ReadBoolean();

            if (Main.netMode == NetmodeID.Server)
            {
                DBG($"HandleWithdrawItemByFullItemTag: from={whoAmI} player={playerIndex} disks=[{string.Join(", ", diskIds.Select(g => g.ToString()[..8]))}]");
                StorageWorldSystem.Instance.BeginModificationTracking();
                var extracted = StorageWorldSystem.Instance.ExtractItemWithFullItemTag(diskIds, fullItemTag);
                EndTrackingAndRespond(mod, whoAmI, !extracted.IsAir, diskIds);
                DBG($"  ExtractItemWithFullItemTag result: type={extracted.type} stack={extracted.stack} isAir={extracted.IsAir}");

                var resultPacket = mod.GetPacket();
                resultPacket.Write((byte)PacketType.WithdrawItemResult);
                ItemIO.Send(extracted, resultPacket, true);
                resultPacket.Write(shift);
                resultPacket.Send(playerIndex, -1);
            }
        }

        private static void HandleWithdrawItemByModData(Mod mod, BinaryReader reader, int whoAmI)
        {
            int playerIndex = reader.ReadInt32();
            int diskCount = reader.ReadInt32();
            var diskIds = ReadGuidList(reader, diskCount);
            var modData = TagIO.Read(reader);
            bool shift = reader.ReadBoolean();

            if (Main.netMode == NetmodeID.Server)
            {
                DBG($"HandleWithdrawItemByModData: from={whoAmI} player={playerIndex} disks=[{string.Join(", ", diskIds.Select(g => g.ToString()[..8]))}]");
                StorageWorldSystem.Instance.BeginModificationTracking();
                var extracted = StorageWorldSystem.Instance.ExtractItemWithModData(diskIds, modData);
                var modified = StorageWorldSystem.Instance.EndModificationTracking();
                DBG($"  ExtractItemWithModData result: type={extracted.type} stack={extracted.stack} isAir={extracted.IsAir}");

                var resultPacket = mod.GetPacket();
                resultPacket.Write((byte)PacketType.WithdrawItemResult);
                ItemIO.Send(extracted, resultPacket, true);
                resultPacket.Write(shift);
                resultPacket.Send(playerIndex, -1);

                EndTrackingAndRespond(mod, whoAmI, !extracted.IsAir, diskIds);
            }
        }

        private static void HandleDepositItem(Mod mod, BinaryReader reader, int whoAmI)
        {
            int count = reader.ReadInt32();
            var diskIds = ReadGuidList(reader, count);
            var item = ItemIO.Receive(reader, true);

            if (Main.netMode == NetmodeID.Server)
            {
                DBG($"HandleDepositItem: from={whoAmI} item={item.type}x{item.stack} disks=[{string.Join(", ", diskIds.Select(g => g.ToString()[..8]))}]");
                StorageWorldSystem.Instance.BeginModificationTracking();
                int leftover = StorageWorldSystem.Instance.InsertItem(diskIds, item);
                DBG($"  InsertItem result: leftover={leftover}");
                if (leftover > 0)
                {
                    item.stack = leftover;
                    var resultPacket = mod.GetPacket();
                    resultPacket.Write((byte)PacketType.WithdrawItemResult);
                    ItemIO.Send(item, resultPacket, true);
                    resultPacket.Write(true); // shift=true: route into inventory, fall back to cursor
                    resultPacket.Send(whoAmI);
                }
                EndTrackingAndRespond(mod, whoAmI, leftover < item.stack, diskIds);
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
                return;
            }

            var player = Main.player[whoAmI];
            float dx = player.Center.X - (terminalPos.X * 16f + 24f);
            float dy = player.Center.Y - (terminalPos.Y * 16f + 24f);
            if (dx * dx + dy * dy > 240f * 240f) // 15 tiles in pixels
            {
                SendReturnItemToClient(mod, whoAmI, item);
                return;
            }

            var diskIds = StorageNetwork.GetAllConnectedDiskIds(terminalPos);
            if (diskIds.Count == 0)
            {
                SendReturnItemToClient(mod, whoAmI, item);
                return;
            }

            StorageWorldSystem.Instance.BeginModificationTracking();
            int leftover = StorageWorldSystem.Instance.InsertItem(diskIds, item);
            if (leftover > 0)
            {
                item.stack = leftover;
                SendReturnItemToClient(mod, whoAmI, item);
            }
            EndTrackingAndRespond(mod, whoAmI, leftover < item.stack, diskIds);
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
            int playerIndex = reader.ReadInt32();
            int diskCount = reader.ReadInt32();
            var diskIds = ReadGuidList(reader, diskCount);
            int itemType = reader.ReadInt32();
            int count = reader.ReadInt32();
            int prefix = reader.ReadInt32();
            bool shift = reader.ReadBoolean();

            if (Main.netMode == NetmodeID.Server)
            {
                DBG($"HandleWithdrawItem: from={whoAmI} player={playerIndex} type={itemType} count={count} prefix={prefix} disks=[{string.Join(", ", diskIds.Select(g => g.ToString()[..8]))}]");
                StorageWorldSystem.Instance.BeginModificationTracking();
                var extracted = StorageWorldSystem.Instance.ExtractItem(diskIds, itemType, count, prefix);
                DBG($"  ExtractItem result: type={extracted.type} stack={extracted.stack} isAir={extracted.IsAir}");

                // Send the extracted item back to the requesting client to place on cursor or in inventory
                var resultPacket = mod.GetPacket();
                resultPacket.Write((byte)PacketType.WithdrawItemResult);
                ItemIO.Send(extracted, resultPacket, true);
                resultPacket.Write(shift);
                resultPacket.Send(playerIndex, -1);

                EndTrackingAndRespond(mod, whoAmI, !extracted.IsAir, diskIds);
            }
        }

        private static void HandleWithdrawItemResult(BinaryReader reader)
        {
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
            packet.Write(Main.myPlayer);
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
            int playerIndex = reader.ReadInt32();
            int diskCount = reader.ReadInt32();
            var diskIds = ReadGuidList(reader, diskCount);
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
                bool success = false;
                if (plan != null && plan.IsFeasible)
                {
                    // Pre-check: block the craft if neither storage nor player inventory
                    // has room. This prevents consuming ingredients with nowhere to put the result.
                    var resultPreview = new Item();
                    resultPreview.SetDefaults(plan.FinalItemType);
                    resultPreview.stack = plan.FinalItemCount;

                    var player = Main.player[whoAmI];
                    bool canCraft;
                    if (craftToInventory)
                        canCraft = PlayerHasRoomFor(player, resultPreview);
                    else
                    {
                        bool storageHasRoom = StorageWorldSystem.Instance.HasRoomFor(diskIds, resultPreview);
                        canCraft = storageHasRoom || PlayerHasRoomFor(player, resultPreview);
                    }

                    if (canCraft)
                    {
                        var result = RecipeResolver.ExecutePlan(plan, diskIds, cleanCraft);
                        if (!result.IsAir)
                        {
                            if (craftToInventory)
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
                            success = true;
                        }
                    }
                }
                EndTrackingAndRespond(mod, whoAmI, success, diskIds);
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
            var diskId = new Guid(reader.ReadBytes(16));
            int seqNum = reader.ReadInt32();
            ushort chunkIndex = reader.ReadUInt16();
            ushort totalChunks = reader.ReadUInt16();
            int dataLength = reader.ReadInt32();
            byte[] data = reader.ReadBytes(dataLength);

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
            try
            {
                int count = reader.ReadInt32();
                int seqNum = reader.ReadInt32();
                var sys = StorageWorldSystem.Instance;
                for (int i = 0; i < count; i++)
                {
                    var data = DiskData.ReadNet(reader);
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
        public static void SendArchiveDiskRequest(Mod mod, int playerIndex, int slot, Guid diskId)
        {
            if (Main.netMode != NetmodeID.MultiplayerClient)
                return;

            var packet = mod.GetPacket();
            packet.Write((byte)PacketType.ArchiveDiskRequest);
            packet.Write(playerIndex);
            packet.Write(slot);
            packet.Write(diskId.ToByteArray());
            packet.Send();
        }

        private static void HandleArchiveDiskRequest(Mod mod, BinaryReader reader, int whoAmI)
        {
            int playerIndex = reader.ReadInt32();
            int slot = reader.ReadInt32();
            var diskId = new Guid(reader.ReadBytes(16));

            if (Main.netMode != NetmodeID.Server)
                return;

            var player = Main.player[playerIndex];
            if (slot < 0 || slot >= player.inventory.Length)
                return;

            var invItem = player.inventory[slot];
            if (invItem == null || invItem.IsAir || invItem.ModItem is not StorageDiskBase disk)
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
            packet.Send(playerIndex);
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

        private static void HandleRestoreDiskRequest(Mod mod, BinaryReader reader)
        {
            var oldGuid      = new Guid(reader.ReadBytes(16));
            var repDiskOldId = new Guid(reader.ReadBytes(16));
            var newId        = new Guid(reader.ReadBytes(16));

            if (Main.netMode != NetmodeID.Server) return;

            var sys = StorageWorldSystem.Instance;
            if (sys == null) return;

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
            var diskIds   = ReadGuidList(reader, diskCount);
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

            sys.BeginModificationTracking();

            // Consume ingredients (craft shortfalls from storage if needed).
            foreach (var (itemType, need) in option)
            {
                int have = sys.CountItem(diskIds, itemType);
                if (have < need)
                {
                    var plan = RecipeResolver.Resolve(itemType, need - have, diskIds, stations, conditions);
                    if (plan != null && plan.IsFeasible)
                    {
                        var crafted = RecipeResolver.ExecutePlan(plan, diskIds);
                        if (!crafted.IsAir)
                            sys.InsertItem(diskIds, crafted);
                    }
                }
                sys.ExtractItem(diskIds, itemType, need);
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

        private static void HandleDefragRequest(Mod mod, BinaryReader reader)
        {
            int count = reader.ReadInt32();
            var diskIds = ReadGuidList(reader, count);

            if (Main.netMode != NetmodeID.Server) return;

            var sys = StorageWorldSystem.Instance;
            if (sys == null) return;

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
        private static void EndTrackingAndRespond(Mod mod, int toClient, bool success,
            List<Guid> requestedDiskIds = null)
        {
            var sys = StorageWorldSystem.Instance;
            var (_, deltas) = sys.EndModificationTrackingWithDeltas();

            if (success && deltas.Count > 0)
            {
                SendOperationResponse(mod, toClient, true);
                BroadcastDiskDeltas(mod, deltas);
            }
            else if (!success)
            {
                // Denied: send failure response + full disk corrections
                SendOperationResponse(mod, toClient, false, requestedDiskIds);
            }
            else
            {
                // Success but no changes (e.g. deposit into a full disk) — still confirm
                SendOperationResponse(mod, toClient, true);
            }
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
        public static void SendOperationResponse(Mod mod, int toClient, bool success,
            List<Guid> affectedDiskIds = null)
        {
            if (Main.netMode != NetmodeID.Server)
                return;

            var packet = mod.GetPacket();
            packet.Write((byte)PacketType.OperationResponse);
            packet.Write(success);
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
                        s.ModData == null && s.ItemType == entry.ItemType && s.PrefixId == entry.PrefixId);
                }
                else
                {
                    // Find existing stack and update, or add new one
                    StoredItemStack existing = null;
                    int currentTotal = 0;
                    foreach (var s in disk.Items)
                    {
                        if (s.ModData == null && s.ItemType == entry.ItemType && s.PrefixId == entry.PrefixId)
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

            // Replace all unique (mod-data) items with the authoritative after-state
            disk.Items.RemoveAll(s => s.ModData != null);
            disk.Items.AddRange(delta.UniqueItemsAfter);
        }

        private static void HandleOperationResponse(BinaryReader reader)
        {
            bool success = reader.ReadBoolean();
            if (Main.netMode != NetmodeID.MultiplayerClient) return;

            // On failure, correction packets (SyncDiskData) follow immediately and are
            // handled by HandleSyncDiskData which resets local state. Nothing extra needed here.
            DBG($"HandleOperationResponse: success={success}");
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
                return;

            // Validate player is within range
            var player = Main.player[whoAmI];
            float dx = player.Center.X - (terminalPos.X * 16f + 24f);
            float dy = player.Center.Y - (terminalPos.Y * 16f + 24f);
            if (dx * dx + dy * dy > 240f * 240f) // 15 tiles in pixels
                return;

            var diskIds = StorageNetwork.GetAllConnectedDiskIds(terminalPos);
            if (diskIds.Count == 0) return;

            var existingTypes = StorageWorldSystem.Instance.GetItemCounts(diskIds);

            StorageWorldSystem.Instance.BeginModificationTracking();

            var results = new List<(byte slot, int newStack)>();
            foreach (var (slotIdx, item) in slots)
            {
                if (!existingTypes.ContainsKey(item.type)) continue;

                int leftover = StorageWorldSystem.Instance.InsertItem(diskIds, item);
                results.Add((slotIdx, leftover));
            }

            EndTrackingAndRespond(mod, whoAmI, results.Count > 0, diskIds);

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

        private static List<Guid> ReadGuidList(BinaryReader reader, int count)
        {
            var list = new List<Guid>(count);
            for (int i = 0; i < count; i++)
                list.Add(new Guid(reader.ReadBytes(16)));
            return list;
        }
    }
}
