using System;
using System.Collections.Generic;
using System.IO;

namespace TerraStorage.Common
{
    /// <summary>
    /// Represents the item-level changes to a single disk between two points in time.
    /// Used by delta sync to send only what changed instead of the entire disk state.
    /// </summary>
    public class DiskDelta
    {
        /// <summary>Sequence number for this delta (set by the server on commit).</summary>
        public int SeqNum { get; set; }

        /// <summary>
        /// Changed stackable items (no mod data). Each entry is the new total stack count
        /// for that item type+prefix on this disk. A NewStack of 0 means the item was fully removed.
        /// </summary>
        public List<DeltaItemEntry> ChangedItems { get; set; } = new();

        /// <summary>
        /// Complete list of unique (mod-data-bearing) items on this disk after the change.
        /// Sent in full because diffing mod data blobs is complex and these items are rare.
        /// </summary>
        public List<StoredItemStack> UniqueItemsAfter { get; set; } = new();

        public void WriteNet(BinaryWriter writer)
        {
            writer.Write(SeqNum);
            writer.Write(ChangedItems.Count);
            foreach (var entry in ChangedItems)
            {
                writer.Write(entry.ItemType);
                writer.Write(entry.PrefixId);
                writer.Write(entry.NewStack);
            }
            writer.Write(UniqueItemsAfter.Count);
            foreach (var item in UniqueItemsAfter)
                item.WriteNet(writer);
        }

        public static DiskDelta ReadNet(BinaryReader reader)
        {
            var delta = new DiskDelta
            {
                SeqNum = reader.ReadInt32()
            };

            int changedCount = reader.ReadInt32();
            for (int i = 0; i < changedCount; i++)
            {
                delta.ChangedItems.Add(new DeltaItemEntry
                {
                    ItemType = reader.ReadInt32(),
                    PrefixId = reader.ReadInt32(),
                    NewStack = reader.ReadInt32()
                });
            }

            int uniqueCount = reader.ReadInt32();
            for (int i = 0; i < uniqueCount; i++)
                delta.UniqueItemsAfter.Add(StoredItemStack.ReadNet(reader));

            return delta;
        }
    }

    /// <summary>
    /// A single item change within a disk delta: the new total count of an item type+prefix.
    /// </summary>
    public class DeltaItemEntry
    {
        public int ItemType { get; set; }
        public int PrefixId { get; set; }
        /// <summary>New total stack count on this disk. 0 = item no longer present.</summary>
        public int NewStack { get; set; }
    }
}
