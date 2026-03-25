using System;
using System.Collections.Generic;
using System.Linq;
using Terraria;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;
using TerraStorage.Common;

namespace TerraStorage.Systems
{
    /// <summary>
    /// World-level system that owns the authoritative storage of all disk data.
    /// Provides insert, extract, and query operations across one or more disks,
    /// manages the world-save lifecycle, and exposes a <see cref="StorageVersion"/>
    /// counter so UI components can poll for changes without per-frame full refreshes.
    /// </summary>
    public class StorageWorldSystem : ModSystem
    {
        // Keyed by DiskId GUID for O(1) disk lookups
        private Dictionary<Guid, DiskData> _allDiskData = new();
        // Monotonically increasing counter stamped on every insert; used for "recently added" sort
        private long _insertionCounter;
        // When non-null, InsertItem/ExtractItem record which disk GUIDs they touch.
        // Use BeginModificationTracking / EndModificationTracking around server operations
        // so BroadcastDiskData only sends the actually-modified disks.
        private HashSet<Guid> _modifiedTracker;

        // Per-disk sequence numbers for delta sync. Incremented each time a disk is modified.
        // Clients track their own copy; a mismatch triggers a full resync for that disk.
        private readonly Dictionary<Guid, int> _diskSeqNums = new();

        // Snapshot of disk item states captured at BeginModificationTracking.
        // Used to compute item-level deltas (what actually changed) in EndModificationTrackingWithDeltas.
        private Dictionary<Guid, List<StoredItemStack>> _preModificationSnapshot;

        /// <summary>
        /// Incremented on every insert or extract. UI can poll this to detect changes.
        /// </summary>
        public long StorageVersion { get; private set; }

        public static StorageWorldSystem Instance => ModContent.GetInstance<StorageWorldSystem>();

        public long InsertionCounter => _insertionCounter;

        /// <summary>Increment StorageVersion to trigger UI refresh (used by delta sync on client).</summary>
        public void BumpStorageVersion() => StorageVersion++;

        /// <summary>Get the current sequence number for a disk (0 if untracked).</summary>
        public int GetDiskSeqNum(Guid diskId) =>
            _diskSeqNums.TryGetValue(diskId, out int seq) ? seq : 0;

        /// <summary>Increment and return the new sequence number for a disk.</summary>
        public int IncrementDiskSeqNum(Guid diskId)
        {
            _diskSeqNums.TryGetValue(diskId, out int seq);
            seq++;
            _diskSeqNums[diskId] = seq;
            return seq;
        }

        /// <summary>Set the client-side sequence number baseline for a disk (used after full sync).</summary>
        public void SetDiskSeqNum(Guid diskId, int seq) => _diskSeqNums[diskId] = seq;

        /// <summary>Remove sequence tracking for a disk (used when disk is removed).</summary>
        public void RemoveDiskSeqNum(Guid diskId) => _diskSeqNums.Remove(diskId);

        public void BeginModificationTracking()
        {
            _modifiedTracker = new HashSet<Guid>();

            // In predictive mode, snapshot all disk states so we can compute item-level deltas later
            if (TerraStorageConfig.IsPredictiveSync)
            {
                _preModificationSnapshot = new Dictionary<Guid, List<StoredItemStack>>();
                foreach (var kvp in _allDiskData)
                {
                    _preModificationSnapshot[kvp.Key] = SnapshotItems(kvp.Value.Items);
                }
            }
        }

        public List<Guid> EndModificationTracking()
        {
            var result = _modifiedTracker?.ToList() ?? new List<Guid>();
            _modifiedTracker = null;
            _preModificationSnapshot = null;
            return result;
        }

        /// <summary>
        /// Ends modification tracking and computes item-level deltas for each modified disk.
        /// Returns (modifiedDiskIds, deltas) where deltas maps diskGuid → list of changed items.
        /// Each delta entry is the NEW state of that item stack on the disk (or stack=0 if removed).
        /// </summary>
        public (List<Guid> modified, Dictionary<Guid, DiskDelta> deltas) EndModificationTrackingWithDeltas()
        {
            var modifiedIds = _modifiedTracker?.ToList() ?? new List<Guid>();
            var deltas = new Dictionary<Guid, DiskDelta>();

            if (_preModificationSnapshot != null)
            {
                foreach (var diskId in modifiedIds)
                {
                    var before = _preModificationSnapshot.TryGetValue(diskId, out var snap)
                        ? snap : new List<StoredItemStack>();
                    var after = _allDiskData.TryGetValue(diskId, out var disk)
                        ? disk.Items : new List<StoredItemStack>();

                    var delta = ComputeDelta(before, after);
                    delta.SeqNum = IncrementDiskSeqNum(diskId);
                    deltas[diskId] = delta;
                }
            }

            _modifiedTracker = null;
            _preModificationSnapshot = null;
            return (modifiedIds, deltas);
        }

        /// <summary>Shallow-clone item list for snapshotting (clones each StoredItemStack's mutable fields).</summary>
        private static List<StoredItemStack> SnapshotItems(List<StoredItemStack> items)
        {
            var snapshot = new List<StoredItemStack>(items.Count);
            foreach (var s in items)
            {
                snapshot.Add(new StoredItemStack
                {
                    ItemType = s.ItemType,
                    Stack = s.Stack,
                    PrefixId = s.PrefixId,
                    InsertionOrder = s.InsertionOrder,
                    ModData = s.ModData
                });
            }
            return snapshot;
        }

        /// <summary>
        /// Computes the item-level differences between a before and after snapshot of a disk.
        /// </summary>
        private static DiskDelta ComputeDelta(List<StoredItemStack> before, List<StoredItemStack> after)
        {
            var delta = new DiskDelta();

            // Build lookup: (itemType, prefixId) → total stack for items WITHOUT mod data.
            // Items WITH mod data are unique and tracked individually.
            var beforeCounts = new Dictionary<(int type, int prefix), int>();
            var afterCounts = new Dictionary<(int type, int prefix), int>();
            var beforeUnique = new List<StoredItemStack>();
            var afterUnique = new List<StoredItemStack>();

            foreach (var s in before)
            {
                if (s.ModData != null) { beforeUnique.Add(s); continue; }
                var key = (s.ItemType, s.PrefixId);
                beforeCounts.TryGetValue(key, out int existing);
                beforeCounts[key] = existing + s.Stack;
            }
            foreach (var s in after)
            {
                if (s.ModData != null) { afterUnique.Add(s); continue; }
                var key = (s.ItemType, s.PrefixId);
                afterCounts.TryGetValue(key, out int existing);
                afterCounts[key] = existing + s.Stack;
            }

            // Detect changes in stackable items
            var allKeys = new HashSet<(int type, int prefix)>(beforeCounts.Keys);
            allKeys.UnionWith(afterCounts.Keys);
            foreach (var key in allKeys)
            {
                beforeCounts.TryGetValue(key, out int bCount);
                afterCounts.TryGetValue(key, out int aCount);
                if (bCount != aCount)
                {
                    delta.ChangedItems.Add(new DeltaItemEntry
                    {
                        ItemType = key.type,
                        PrefixId = key.prefix,
                        NewStack = aCount // 0 means item fully removed from disk
                    });
                }
            }

            // For unique (mod data) items: send the full after-state as part of the delta.
            // This is simpler than diffing individual mod data blobs and covers all cases.
            delta.UniqueItemsAfter = afterUnique;

            return delta;
        }

        /// <summary>
        /// Get or create disk data for a given ID and tier.
        /// </summary>
        public DiskData GetOrCreateDiskData(Guid diskId, DiskTier tier)
        {
            if (!_allDiskData.TryGetValue(diskId, out var data))
            {
                data = new DiskData
                {
                    DiskId = diskId,
                    Tier = tier,
                    Items = new List<StoredItemStack>()
                };
                _allDiskData[diskId] = data;
            }
            return data;
        }

        /// <summary>
        /// Get disk data by ID. Returns null if not found.
        /// </summary>
        public DiskData GetDiskData(Guid diskId)
        {
            return _allDiskData.TryGetValue(diskId, out var data) ? data : null;
        }

        /// <summary>
        /// Check if a disk ID exists in world data.
        /// </summary>
        public bool HasDiskData(Guid diskId) => _allDiskData.ContainsKey(diskId);

        /// <summary>
        /// Fast item count lookup: returns itemType → total count across all given disks.
        /// No object allocation beyond the dictionary. Use this instead of GetConsolidatedItems
        /// when you only need counts (e.g. canCraft checks).
        /// </summary>
        public Dictionary<int, int> GetItemCounts(IEnumerable<Guid> diskIds)
        {
            var counts = new Dictionary<int, int>();
            foreach (var diskId in diskIds)
            {
                if (!_allDiskData.TryGetValue(diskId, out var disk))
                    continue;
                foreach (var stored in disk.Items)
                {
                    counts.TryGetValue(stored.ItemType, out int existing);
                    counts[stored.ItemType] = existing + stored.Stack;
                }
            }
            return counts;
        }

        /// <summary>
        /// Get all items across multiple disks, consolidated by type+prefix.
        /// </summary>
        public List<ConsolidatedItem> GetConsolidatedItems(IEnumerable<Guid> diskIds)
        {
            var consolidated = new Dictionary<(int type, int prefix), ConsolidatedItem>();
            // Items with per-instance mod data (UnloadedItems, disks, etc.) are unique and
            // must never be merged — each gets its own grid slot.
            var uniqueEntries = new List<ConsolidatedItem>();

            foreach (var diskId in diskIds)
            {
                if (!_allDiskData.TryGetValue(diskId, out var disk))
                    continue;

                foreach (var stored in disk.Items)
                {
                    if (stored.ModData != null || stored.FullItemTag != null)
                    {
                        uniqueEntries.Add(new ConsolidatedItem
                        {
                            ItemType = stored.ItemType,
                            PrefixId = stored.PrefixId,
                            TotalCount = stored.Stack,
                            LatestInsertionOrder = stored.InsertionOrder,
                            SourceDisks = new HashSet<Guid> { diskId },
                            ModData = stored.ModData,
                            FullItemTag = stored.FullItemTag
                        });
                        continue;
                    }

                    var key = (stored.ItemType, stored.PrefixId);
                    if (!consolidated.TryGetValue(key, out var entry))
                    {
                        entry = new ConsolidatedItem
                        {
                            ItemType = stored.ItemType,
                            PrefixId = stored.PrefixId,
                            TotalCount = 0,
                            SourceDisks = new HashSet<Guid>()
                        };
                        consolidated[key] = entry;
                    }

                    entry.TotalCount += stored.Stack;
                    if (stored.InsertionOrder > entry.LatestInsertionOrder)
                        entry.LatestInsertionOrder = stored.InsertionOrder;
                    entry.SourceDisks.Add(diskId);
                }
            }

            return consolidated.Values.Concat(uniqueEntries).ToList();
        }

        /// <summary>
        /// Insert an item across the given disks (tries each until inserted).
        /// Returns leftover count.
        /// </summary>
        public int InsertItem(IEnumerable<Guid> diskIds, Item item)
        {
            if (item == null || item.IsAir)
                return 0;

            // Bump counters before insertion so the new InsertionOrder is strictly greater than any prior one
            _insertionCounter++;
            StorageVersion++;
            BackupSystem.MarkDirty();
            int remaining = item.stack;

            // Serialize the original item BEFORE any Clone() so GlobalItem data from
            // other mods (enchantments etc.) is captured intact. Clone() may not deep-copy
            // per-instance GlobalItem state, so serializing the clone can lose that data.
            var originalTag = ItemIO.Save(item);

            foreach (var diskId in diskIds)
            {
                if (!_allDiskData.TryGetValue(diskId, out var disk))
                    continue;

                // Clone with only the current remaining count so DiskData.InsertItem sees the right stack
                var tempItem = item.Clone();
                tempItem.stack = remaining;
                int before = remaining;
                remaining = disk.InsertItem(tempItem, _insertionCounter, originalTag);
                if (remaining < before)
                    _modifiedTracker?.Add(diskId);

                if (remaining <= 0)
                    return 0;
            }

            return remaining;
        }

        /// <summary>
        /// Returns true if the given item can be fully inserted across the given disks
        /// without actually modifying them. Used to pre-check capacity before crafting.
        /// </summary>
        public bool HasRoomFor(IEnumerable<Guid> diskIds, Item item)
        {
            if (item == null || item.IsAir) return true;
            int remaining = item.stack;

            // Items with per-instance mod data don't stack — only check free slots.
            bool hasModData = false;
            if (item.ModItem != null)
            {
                var tag = new TagCompound();
                item.ModItem.SaveData(tag);
                hasModData = tag.Count > 0;
            }

            foreach (var diskId in diskIds)
            {
                if (!_allDiskData.TryGetValue(diskId, out var disk)) continue;

                if (!hasModData)
                {
                    foreach (var stored in disk.Items)
                    {
                        if (stored.ItemType == item.type && stored.PrefixId == item.prefix && stored.Stack < item.maxStack)
                        {
                            remaining -= item.maxStack - stored.Stack;
                            if (remaining <= 0) return true;
                        }
                    }
                }

                int freeSlots = disk.MaxStacks - disk.UsedStacks;
                if (freeSlots > 0)
                {
                    remaining -= freeSlots * item.maxStack;
                    if (remaining <= 0) return true;
                }
            }
            return remaining <= 0;
        }

        /// <summary>
        /// Extract an item from across multiple disks.
        /// </summary>
        public Item ExtractItem(IEnumerable<Guid> diskIds, int itemType, int count, int prefixId = -1)
        {
            // Use the item returned by DiskData.ExtractItem directly so that mod data
            // (e.g. UnloadedItem's original tag, Storage Disk GUIDs) is preserved.
            // Reconstructing via SetDefaults would produce a blank item with no state.
            Item result = null;
            int totalExtracted = 0;

            foreach (var diskId in diskIds)
            {
                if (!_allDiskData.TryGetValue(diskId, out var disk))
                    continue;

                int needed = count - totalExtracted;
                var extracted = disk.ExtractItem(itemType, needed, prefixId);
                if (extracted.IsAir)
                    continue;

                totalExtracted += extracted.stack;
                result ??= extracted;
                _modifiedTracker?.Add(diskId);

                if (totalExtracted >= count)
                    break;
            }

            if (result == null)
                return new Item();

            StorageVersion++;
            BackupSystem.MarkDirty();
            result.stack = totalExtracted;
            return result;
        }

        /// <summary>
        /// Extract a specific per-instance item (e.g. UnloadedItem) identified by its exact
        /// ModData. Searches disks in order and returns the first matching stack.
        /// </summary>
        public Item ExtractItemWithModData(IEnumerable<Guid> diskIds, TagCompound modData)
        {
            foreach (var diskId in diskIds)
            {
                if (!_allDiskData.TryGetValue(diskId, out var disk))
                    continue;

                var extracted = disk.ExtractItemWithModData(modData);
                if (!extracted.IsAir)
                {
                    StorageVersion++;
                    BackupSystem.MarkDirty();
                    _modifiedTracker?.Add(diskId);
                    return extracted;
                }
            }
            return new Item();
        }

        /// <summary>
        /// Extract a specific per-instance item identified by its exact FullItemTag.
        /// Used for GlobalItem-backed items (e.g. Entropy enchantments) that have no ModData.
        /// </summary>
        public Item ExtractItemWithFullItemTag(IEnumerable<Guid> diskIds, TagCompound fullItemTag)
        {
            foreach (var diskId in diskIds)
            {
                if (!_allDiskData.TryGetValue(diskId, out var disk))
                    continue;

                var extracted = disk.ExtractItemWithFullItemTag(fullItemTag);
                if (!extracted.IsAir)
                {
                    StorageVersion++;
                    BackupSystem.MarkDirty();
                    _modifiedTracker?.Add(diskId);
                    return extracted;
                }
            }
            return new Item();
        }

        /// <summary>
        /// Count total of a given item across multiple disks.
        /// </summary>
        public int CountItem(IEnumerable<Guid> diskIds, int itemType, int prefixId = -1)
        {
            int total = 0;
            foreach (var diskId in diskIds)
            {
                if (_allDiskData.TryGetValue(diskId, out var disk))
                    total += disk.CountItem(itemType, prefixId);
            }
            return total;
        }

        /// <summary>
        /// Register a disk ID with a given tier (ensures data exists).
        /// </summary>
        public void RegisterDisk(Guid diskId, DiskTier tier)
        {
            GetOrCreateDiskData(diskId, tier);
        }

        /// <summary>
        /// Get all disk data in the world.
        /// </summary>
        public IReadOnlyCollection<DiskData> GetAllDiskData() => _allDiskData.Values;

        /// <summary>
        /// Remove a disk's data entry (used when reassigning a blank disk's GUID during recovery).
        /// </summary>
        public void RemoveDiskData(Guid diskId)
        {
            if (_allDiskData.Remove(diskId))
                StorageVersion++;
        }

        /// <summary>
        /// Move a disk's data from <paramref name="oldId"/> to <paramref name="newId"/>,
        /// then delete the old entry.  Used by Disk Recovery so the original physical disk
        /// (if it still exists) is left pointing at nothing and becomes empty.
        /// </summary>
        public void RemapDiskData(Guid oldId, Guid newId)
        {
            if (!_allDiskData.TryGetValue(oldId, out var data)) return;
            data.DiskId = newId;
            _allDiskData[newId] = data;
            _allDiskData.Remove(oldId);
            StorageVersion++;
            BackupSystem.MarkDirty();
        }

        /// <summary>
        /// Archive a disk: removes its data from the world system and returns the stored items
        /// so they can be embedded in the disk item's own NBT for cross-world transport.
        /// After this call, the disk's GUID no longer exists in world storage.
        /// </summary>
        public List<StoredItemStack> ArchiveDisk(Guid diskId)
        {
            DBG($"ArchiveDisk: diskId={diskId.ToString()[..8]} found={_allDiskData.ContainsKey(diskId)} allDiskData=[{string.Join(", ", _allDiskData.Keys.Select(g => g.ToString()[..8]))}]");
            if (!_allDiskData.TryGetValue(diskId, out var data))
                return new List<StoredItemStack>();

            var items = new List<StoredItemStack>(data.Items);
            _allDiskData.Remove(diskId);
            DBG($"ArchiveDisk: removed {diskId.ToString()[..8]}, returning {items.Count} item stacks");
            StorageVersion++;
            BackupSystem.MarkDirty();
            return items;
        }

        /// <summary>
        /// Defragments the given disks (in the order provided) by moving item stacks from
        /// later disks into free space on earlier disks.  Respects item identity:
        ///   • Stacks without ModData are merged up to maxStack with matching type+prefix stacks.
        ///   • Stacks with ModData (unique items) are moved whole to an empty slot only.
        /// Returns the GUIDs of every disk whose Items list was modified.
        /// </summary>
        public List<Guid> Defragment(List<Guid> orderedDiskIds)
        {
            var disks = orderedDiskIds
                .Select(id => _allDiskData.TryGetValue(id, out var d) ? d : null)
                .Where(d => d != null)
                .ToList();

            var modified = new HashSet<Guid>();

            for (int ti = 0; ti < disks.Count - 1; ti++)
            {
                var target = disks[ti];
                if (target.IsFull) continue;

                for (int di = ti + 1; di < disks.Count && !target.IsFull; di++)
                {
                    var donor = disks[di];
                    if (donor.Items.Count == 0) continue;

                    for (int si = donor.Items.Count - 1; si >= 0 && !target.IsFull; si--)
                    {
                        var stack = donor.Items[si];

                        if (stack.ModData != null)
                        {
                            // Unique item — move whole stack to a free slot only.
                            target.Items.Add(stack);
                            donor.Items.RemoveAt(si);
                            modified.Add(target.DiskId);
                            modified.Add(donor.DiskId);
                        }
                        else
                        {
                            // Plain item — merge into existing partial stacks first, then add new slot.
                            var tempItem = new Terraria.Item();
                            tempItem.SetDefaults(stack.ItemType);
                            int maxStack = tempItem.maxStack;
                            int toMove = stack.Stack;

                            foreach (var existing in target.Items)
                            {
                                if (existing.ItemType == stack.ItemType
                                    && existing.PrefixId == stack.PrefixId
                                    && existing.ModData == null
                                    && existing.Stack < maxStack)
                                {
                                    int canAdd = Math.Min(toMove, maxStack - existing.Stack);
                                    existing.Stack += canAdd;
                                    toMove -= canAdd;
                                    if (toMove == 0) break;
                                }
                            }

                            while (toMove > 0 && !target.IsFull)
                            {
                                int addAmount = Math.Min(toMove, maxStack);
                                target.Items.Add(new StoredItemStack
                                {
                                    ItemType     = stack.ItemType,
                                    Stack        = addAmount,
                                    PrefixId     = stack.PrefixId,
                                    InsertionOrder = stack.InsertionOrder,
                                    ModData      = null
                                });
                                toMove -= addAmount;
                            }

                            if (toMove < stack.Stack)
                            {
                                modified.Add(target.DiskId);
                                modified.Add(donor.DiskId);
                            }

                            if (toMove == 0)
                                donor.Items.RemoveAt(si);
                            else
                                stack.Stack = toMove;
                        }
                    }
                }
            }

            if (modified.Count > 0)
            {
                StorageVersion++;
                BackupSystem.MarkDirty();
            }

            return modified.ToList();
        }

        private static void DBG(string msg)
        {
            var path = TerraStorage.DebugLogPath;
            if (path == null) return;
            try
            {
                using var fs = new System.IO.FileStream(path, System.IO.FileMode.Append, System.IO.FileAccess.Write, System.IO.FileShare.ReadWrite);
                using var sw = new System.IO.StreamWriter(fs);
                sw.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}][net={Terraria.Main.netMode}] {msg}");
            }
            catch { }
        }

        /// <summary>
        /// Register a disk with a pre-existing item list (used when an unarchived disk is
        /// first inserted into a Drive Bay to restore its items into this world).
        /// </summary>
        public void RegisterDiskWithItems(Guid diskId, DiskTier tier, List<StoredItemStack> items)
        {
            var data = new DiskData
            {
                DiskId = diskId,
                Tier = tier,
                Items = new List<StoredItemStack>(items)
            };
            _allDiskData[diskId] = data;
            StorageVersion++;
            BackupSystem.MarkDirty();
        }

        /// <summary>
        /// Applies a DiskData received from the server, replacing any local copy.
        /// Used by clients in multiplayer to stay in sync with the authoritative server state.
        /// </summary>
        public void ApplyDiskDataFromNetwork(DiskData data)
        {
            if (data == null)
                return;

            _allDiskData[data.DiskId] = data;
            StorageVersion++;
        }

        /// <summary>
        /// Upgrade an existing disk's tier in-place, preserving all stored items.
        /// </summary>
        public void UpgradeDisk(Guid diskId, DiskTier newTier)
        {
            if (_allDiskData.TryGetValue(diskId, out var data))
                data.Tier = newTier;
        }

        /// <summary>
        /// Assign an existing disk's data to a new Guid (for disk restoration).
        /// </summary>
        public bool RestoreDisk(Guid targetDiskId, Guid sourceDiskId)
        {
            if (!_allDiskData.TryGetValue(sourceDiskId, out var data))
                return false;

            // Re-map the source disk's data under the target GUID so the physical item
            // (which carries targetDiskId) now points to the correct stored items
            _allDiskData[targetDiskId] = data;
            data.DiskId = targetDiskId;
            return true;
        }

        /// <summary>
        /// Replaces all disk data in-place from a backup tag. Used by the server restore command
        /// for immediate restore without a world reload.
        /// </summary>
        public void RestoreFromTag(TagCompound tag)
        {
            _allDiskData.Clear();
            _insertionCounter = tag.ContainsKey("insertionCounter") ? tag.GetLong("insertionCounter") : 0;

            if (tag.ContainsKey("disks"))
            {
                foreach (var diskTag in tag.GetList<TagCompound>("disks"))
                {
                    var data = DiskData.Load(diskTag);
                    _allDiskData[data.DiskId] = data;
                }
            }

            StorageVersion++;
        }

        public override void SaveWorldData(TagCompound tag)
        {
            var diskList = _allDiskData.Values.Select(d => d.Save()).ToList();
            tag["disks"] = diskList;
            tag["insertionCounter"] = _insertionCounter;

            DumpDiskData();
        }

        private void DumpDiskData()
        {
            try
            {
                string dumpDir = System.IO.Path.Combine(
                    AppContext.BaseDirectory, "tModLoader-Logs", "TerraStorage-DiskDumps");
                System.IO.Directory.CreateDirectory(dumpDir);

                foreach (var data in _allDiskData.Values)
                {
                    var sb = new System.Text.StringBuilder();
                    sb.AppendLine($"GUID:     {data.DiskId}");
                    sb.AppendLine($"Tier:     {data.Tier}");
                    sb.AppendLine($"Capacity: {data.MaxStacks} stacks");
                    sb.AppendLine($"Used:     {data.UsedStacks} / {data.MaxStacks} stacks");
                    sb.AppendLine($"Saved:    {System.DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                    sb.AppendLine();
                    sb.AppendLine("Items:");
                    if (data.Items.Count == 0)
                    {
                        sb.AppendLine("  (empty)");
                    }
                    else
                    {
                        foreach (var item in data.Items)
                        {
                            var name = Terraria.Lang.GetItemNameValue(item.ItemType);
                            sb.AppendLine($"  [{item.Stack,5}x] {name} (id={item.ItemType} prefix={item.PrefixId} order={item.InsertionOrder})");
                        }
                    }

                    string filePath = System.IO.Path.Combine(dumpDir, $"{data.DiskId}.txt");
                    System.IO.File.WriteAllText(filePath, sb.ToString());
                }
            }
            catch (System.Exception ex)
            {
                Terraria.ModLoader.ModContent.GetInstance<TerraStorage>()?.Logger.Warn($"DumpDiskData failed: {ex.Message}");
            }
        }

        public override void LoadWorldData(TagCompound tag)
        {
            var restoreTag = BackupSystem.TryConsumeRestoreOverride();
            if (restoreTag != null)
            {
                ModContent.GetInstance<TerraStorage>()?.Logger.Info("[TerraStorage] Restoring storage from backup.");
                tag = restoreTag;
            }

            _allDiskData.Clear();
            // Restore the insertion counter so newly inserted items always get a higher order value
            _insertionCounter = tag.ContainsKey("insertionCounter") ? tag.GetLong("insertionCounter") : 0;

            if (tag.ContainsKey("disks"))
            {
                var diskTags = tag.GetList<TagCompound>("disks");
                foreach (var diskTag in diskTags)
                {
                    var data = DiskData.Load(diskTag);
                    _allDiskData[data.DiskId] = data;
                }
            }

            // Purge empty disk entries on load. Disks in Drive Bays will re-register
            // themselves via GetInsertedDiskIds the next time they are accessed.
            var emptyKeys = _allDiskData
                .Where(kvp => kvp.Value.UsedStacks == 0)
                .Select(kvp => kvp.Key)
                .ToList();
            foreach (var key in emptyKeys)
                _allDiskData.Remove(key);
        }

        public override void OnWorldUnload()
        {
            _allDiskData.Clear();
            _diskSeqNums.Clear();
            _preModificationSnapshot = null;
            _modifiedTracker = null;
            Helpers.AdjTileHelper.ClearCache();
        }
    }

    /// <summary>
    /// Represents the combined totals of a single item type+prefix pair aggregated
    /// across all queried disks. Used by the Terminal UI to show one row per unique item.
    /// </summary>
    public class ConsolidatedItem
    {
        public int ItemType { get; set; }
        public int PrefixId { get; set; }
        /// <summary>Sum of all stack counts for this item across every source disk.</summary>
        public int TotalCount { get; set; }
        /// <summary>
        /// The highest InsertionOrder among all individual stacks of this item.
        /// Used for "recently added" sort — a higher value means more recently inserted.
        /// </summary>
        public long LatestInsertionOrder { get; set; }
        /// <summary>GUIDs of the disks that contributed to this consolidated entry.</summary>
        public HashSet<Guid> SourceDisks { get; set; } = new();
        /// <summary>
        /// For per-instance items (UnloadedItems, items with unique NBT), the exact ModData
        /// of the specific stack this entry represents. Used to extract the right instance.
        /// Null for regular stackable items.
        /// </summary>
        public TagCompound ModData { get; set; }
        /// <summary>
        /// Full ItemIO-serialized tag for items whose per-instance data comes from GlobalItem
        /// (e.g. enchantment mods). Non-null means this item must be extracted and restored
        /// via ItemIO.Load rather than reconstructed from type/prefix alone.
        /// </summary>
        public TagCompound FullItemTag { get; set; }
    }
}
