using System;
using System.Collections.Generic;
using System.IO;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;
using Terraria.ObjectData;
using Requisition.Content.Items;
using Requisition.Content.UI;
using Requisition.Helpers;
using Requisition.Systems;

namespace Requisition.Content.Tiles
{
    // Tile entity attached to each placed Drive Bay. Manages the array of inserted Storage Disks,
    // handles multiplayer synchronization, and persists disk items across save/load cycles.
    public class DriveBayEntity : ModTileEntity
    {
        //Maximum number of Storage Disk slots in a single Drive Bay.
        public const int DiskSlotCount = 40;

        public Item[] DiskSlots { get; private set; } = new Item[DiskSlotCount];

        // Client-side visual state — not persisted or synced, recomputed at trigger points.
        // Slot states: 0=empty, 1=offline, 2=online(<80%), 3=near80(>=80%), 4=full(100%)
        public bool IsConnected { get; private set; }
        public byte[] SlotDisplayState { get; private set; } = new byte[DiskSlotCount];
        // Aggregate fill across all inserted disks, for the bay-level status light.
        public int TotalUsedStacks { get; private set; }
        public int TotalMaxStacks { get; private set; }

        // Tracks StorageWorldSystem.StorageVersion to auto-refresh on storage changes
        // and on world load (starts at -1 so first tick always triggers).
        private long _lastSeenVersion = -1;

        public override void OnNetPlace()
        {
            InitializeSlots();
        }

        public override void Update()
        {
            if (Main.netMode == NetmodeID.Server) return;
            var sys = StorageWorldSystem.Instance;
            if (sys == null) return;

            bool connected = StorageNetwork.HasTerminalNearby(Position);
            if (connected != IsConnected || sys.StorageVersion != _lastSeenVersion)
            {
                _lastSeenVersion = sys.StorageVersion;
                RefreshVisualState(connected);
            }
        }

        public override bool IsTileValidForEntity(int x, int y)
        {
            // x, y is the entity's stored position (top-left of the multi-tile).
            // DriveBayLegacy is the old 2x2 tile kept for world-save compatibility.
            var tile = Main.tile[x, y];
            return tile.HasTile && (tile.TileType == ModContent.TileType<DriveBayLarge>() ||
                                    tile.TileType == ModContent.TileType<DriveBayLegacy>());
        }

        public override int Hook_AfterPlacement(int i, int j, int type, int style, int direction, int alternate)
        {
            // With processedCoordinates: true, i/j is the top-left corner of the multi-tile.
            if (Main.netMode == Terraria.ID.NetmodeID.MultiplayerClient)
            {
                // On a client we can't place tile entities directly; send the tile data and a
                // TileEntityPlacement message so the server creates and replicates the entity.
                NetMessage.SendTileSquare(Main.myPlayer, i, j, 3, 3);
                NetMessage.SendData(Terraria.ID.MessageID.TileEntityPlacement, number: i, number2: j, number3: Type);
                return -1;
            }

            int placedEntity = Place(i, j);
            if (TileEntity.ByID.TryGetValue(placedEntity, out var entity) && entity is DriveBayEntity sbe)
                sbe.InitializeSlots();

            return placedEntity;
        }

        public void EnsureSlotsInitialized() => InitializeSlots();

        // Recompute the client-side visual state for all 40 slots.
        // Only runs on client/singleplayer — server has no display state.
        public void RefreshVisualState(bool connected)
        {
            if (Main.netMode == NetmodeID.Server) return;
            IsConnected = connected;
            InitializeSlots();
            var sys = StorageWorldSystem.Instance;
            int totalUsed = 0, totalMax = 0;
            for (int i = 0; i < DiskSlotCount; i++)
            {
                if (DiskSlots[i] == null || DiskSlots[i].IsAir)
                {
                    SlotDisplayState[i] = 0;
                    continue;
                }
                if (!connected || DiskSlots[i].ModItem is not StorageDiskBase disk)
                {
                    SlotDisplayState[i] = 1; // offline
                    continue;
                }
                var data = sys?.GetDiskData(disk.DiskId);
                if (data == null || data.MaxStacks == 0) { SlotDisplayState[i] = 2; continue; }
                totalUsed += data.UsedStacks;
                totalMax  += data.MaxStacks;
                float pct = (float)data.UsedStacks / data.MaxStacks;
                SlotDisplayState[i] = pct >= 1f ? (byte)4 : pct >= 0.8f ? (byte)3 : (byte)2;
            }
            TotalUsedStacks = totalUsed;
            TotalMaxStacks  = totalMax;
        }

        //Returns true if at least one disk slot is occupied.
        public bool HasDisks()
        {
            for (int i = 0; i < DiskSlotCount; i++)
                if (DiskSlots[i] != null && !DiskSlots[i].IsAir)
                    return true;
            return false;
        }

        // Ensures every slot contains a non-null, air Item rather than a null reference.
        // Called defensively before any slot access to guard against partial deserialization.
        private void InitializeSlots()
        {
            for (int i = 0; i < DiskSlotCount; i++)
            {
                if (DiskSlots[i] == null)
                {
                    DiskSlots[i] = new Item();
                    DiskSlots[i].TurnToAir();
                }
            }
        }

        // Get all disk IDs from inserted disks.
        public List<Guid> GetInsertedDiskIds()
        {
            var seen = new HashSet<Guid>();
            var ids = new List<Guid>();
            for (int i = 0; i < DiskSlotCount; i++)
            {
                if (DiskSlots[i] != null && !DiskSlots[i].IsAir &&
                    DiskSlots[i].ModItem is StorageDiskBase disk)
                {
                    // Defensive fallback: InsertDisk should have assigned a GUID, but guard
                    // against edge cases (e.g. disks loaded from a pre-fix save file).
                    if (disk.DiskId == Guid.Empty)
                        disk.DiskId = Guid.NewGuid();

                    // Re-register in case the DiskData entry was purged on world load
                    // (empty entries are cleaned up at load time to keep the world tidy).
                    // Only register on the server/singleplayer — clients receive DiskData via network sync.
                    if (Main.netMode != NetmodeID.MultiplayerClient)
                    {
                        StorageWorldSystem.Instance?.RegisterDisk(disk.DiskId, disk.Tier);
                        // Always sync the tier from the disk item — the item type is the authoritative
                        // source of truth. DiskData.Tier can become stale when the world is reloaded
                        // without saving after a disk upgrade (player file keeps new tier, world save
                        // has old DiskData tier, and GetOrCreateDiskData never overwrites existing entries).
                        StorageWorldSystem.Instance?.UpgradeDisk(disk.DiskId, disk.Tier);
                    }

                    if (seen.Add(disk.DiskId))
                        ids.Add(disk.DiskId);
                }
            }
            return ids;
        }

        // Try to insert a disk into the first available slot.
        // Returns true if successful.
        public bool InsertDisk(Item diskItem, int slot = -1)
        {
            if (diskItem == null || diskItem.IsAir || diskItem.ModItem is not StorageDiskBase disk)
                return false;

            // Archived disks cannot be inserted — they must be unarchived first (middle-click).
            if (disk.IsArchived)
                return false;

            InitializeSlots();

            // Assign a GUID the first time a disk enters a Drive Bay. This is the canonical
            // point at which a disk becomes a storage identity in StorageWorldSystem.
            if (disk.DiskId == Guid.Empty)
            {
                disk.DiskId = Guid.NewGuid();

                if (Main.netMode != NetmodeID.MultiplayerClient)
                {
                    if (disk.ArchivedItems.Count > 0)
                    {
                        // Restore items from an unarchived disk into this world's storage.
                        StorageWorldSystem.Instance?.RegisterDiskWithItems(disk.DiskId, disk.Tier, disk.ArchivedItems);
                        disk.ArchivedItems.Clear();
                    }
                    else
                    {
                        StorageWorldSystem.Instance?.RegisterDisk(disk.DiskId, disk.Tier);
                    }
                }
                // MP client: GUID assigned but ArchivedItems left intact so they are
                // serialized in the SyncDiskInsert packet. The server handles restoration.
            }
            else if (Main.netMode != NetmodeID.MultiplayerClient)
            {
                // Disk already has a GUID — either a normal re-insert, a world-load, or
                // a client-assigned GUID arriving via SyncDiskInsert with ArchivedItems.
                if (disk.ArchivedItems.Count > 0)
                {
                    StorageWorldSystem.Instance?.RegisterDiskWithItems(disk.DiskId, disk.Tier, disk.ArchivedItems);
                    disk.ArchivedItems.Clear();
                }
                else
                {
                    StorageWorldSystem.Instance?.RegisterDisk(disk.DiskId, disk.Tier);
                    StorageWorldSystem.Instance?.UpgradeDisk(disk.DiskId, disk.Tier);
                }
            }

            if (slot >= 0 && slot < DiskSlotCount)
            {
                if (DiskSlots[slot].IsAir)
                {
                    DiskSlots[slot] = diskItem.Clone();
                    RefreshVisualState(IsConnected);
                    return true;
                }
                return false;
            }

            for (int i = 0; i < DiskSlotCount; i++)
            {
                if (DiskSlots[i].IsAir)
                {
                    DiskSlots[i] = diskItem.Clone();
                    RefreshVisualState(IsConnected);
                    return true;
                }
            }

            return false;
        }

        // Remove a disk from a specific slot. Returns the removed disk item.
        public Item RemoveDisk(int slot)
        {
            InitializeSlots();

            if (slot < 0 || slot >= DiskSlotCount || DiskSlots[slot].IsAir)
                return new Item();

            var removed = DiskSlots[slot].Clone();
            DiskSlots[slot].TurnToAir();
            RefreshVisualState(IsConnected);
            return removed;
        }

        // Open the disk insertion UI for this storage block. 
        public void OpenDiskUI(Player player)
        {
            var uiSystem = ModContent.GetInstance<DriveBayUISystem>();
            uiSystem?.OpenDriveBay(this);
        }

        public override void SaveData(TagCompound tag)
        {
            InitializeSlots();
            var diskTags = new List<TagCompound>();
            for (int i = 0; i < DiskSlotCount; i++)
            {
                diskTags.Add(ItemIO.Save(DiskSlots[i]));
            }
            tag["disks"] = diskTags;
        }

        public override void LoadData(TagCompound tag)
        {
            InitializeSlots();
            if (tag.ContainsKey("disks"))
            {
                var diskTags = tag.GetList<TagCompound>("disks");
                for (int i = 0; i < DiskSlotCount && i < diskTags.Count; i++)
                {
                    DiskSlots[i] = ItemIO.Load(diskTags[i]);
                }
            }
        }

        //Serializes all disk slots over the network (server → clients).
        public override void NetSend(BinaryWriter writer)
        {
            InitializeSlots();

            // Assign GUIDs to uninitialized disks before broadcasting so clients receive
            // GUID-bearing items and can request disk data with the correct IDs.
            if (Main.netMode != NetmodeID.MultiplayerClient)
            {
                for (int i = 0; i < DiskSlotCount; i++)
                {
                    if (DiskSlots[i] != null && !DiskSlots[i].IsAir &&
                        DiskSlots[i].ModItem is StorageDiskBase disk && disk.DiskId == Guid.Empty)
                    {
                        disk.DiskId = Guid.NewGuid();
                        StorageWorldSystem.Instance?.RegisterDisk(disk.DiskId, disk.Tier);
                    }
                }
            }

            for (int i = 0; i < DiskSlotCount; i++)
            {
                // StorageDiskBase.NetSend writes the GUID so it is preserved across the wire.
                ItemIO.Send(DiskSlots[i], writer, true);
            }
        }

        //Deserializes all disk slots received from the network.
        public override void NetReceive(BinaryReader reader)
        {
            InitializeSlots();
            for (int i = 0; i < DiskSlotCount; i++)
            {
                DiskSlots[i] = ItemIO.Receive(reader, true);
            }

            // Show disks immediately (lights will be green/unknown until fill data arrives).
            RefreshVisualState(StorageNetwork.HasTerminalNearby(Position));

            // Request disk fill data from the server so drive lights update to correct colors.
            if (Main.netMode == NetmodeID.MultiplayerClient)
            {
                var ids = GetInsertedDiskIds();
                if (ids.Count > 0)
                    NetworkHandler.SendRequestDiskData(ModLoader.GetMod("Requisition"), ids);
            }
        }

        // Find the DriveBayEntity at a given tile position (accounts for multi-tile).
        public static DriveBayEntity FindEntity(int i, int j)
        {
            var tile = Main.tile[i, j];
            if (!tile.HasTile)
                return null;

            // Entity is stored at the top-left corner of the multi-tile.
            Point16 topLeft = TileObjectData.TopLeft(i, j);
            if (topLeft == Point16.NegativeOne)
                return null;

            if (TileEntity.ByPosition.TryGetValue(topLeft, out var entity) && entity is DriveBayEntity sbe)
                return sbe;

            return null;
        }
    }
}
