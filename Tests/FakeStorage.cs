using System;
using System.Collections.Generic;
using System.Linq;
using TerraStorage.Helpers.Resolver;

namespace TerraStorage.Tests
{
    // An item handle with no Terraria in it. Mark stands in for per-instance state (prefix, mod
    // data), so a refund can be shown to put back the same units rather than equivalent ones.
    public sealed class FakeItem
    {
        public int Type;
        public int Stack;
        public string Mark;

        public override string ToString() => Mark == null ? $"{Type}x{Stack}" : $"{Type}x{Stack}[{Mark}]";
    }

    // A dictionary-backed storage network. Capacity is a total unit count, which is all the
    // transaction core ever needs to know about "storage is full".
    public sealed class FakeStorage : ICraftingStorage<FakeItem>
    {
        private readonly Dictionary<int, int> _counts = new();

        public int Capacity = int.MaxValue;
        public readonly List<string> Log = new();

        public FakeStorage With(int itemType, int count)
        {
            _counts.TryGetValue(itemType, out int have);
            _counts[itemType] = have + count;
            return this;
        }

        public int TotalUnits => _counts.Values.Sum();

        public FakeItem Nothing => null;

        public int CountItem(int itemType)
        {
            _counts.TryGetValue(itemType, out int have);
            return have;
        }

        public FakeItem Extract(int itemType, int amount)
        {
            _counts.TryGetValue(itemType, out int have);
            int taken = Math.Min(have, amount);
            Log.Add($"extract {itemType}x{amount}->{taken}");

            if (taken <= 0)
                return null;

            _counts[itemType] = have - taken;
            return new FakeItem { Type = itemType, Stack = taken };
        }

        // Mirrors StorageWorldSystem.InsertItem: reports what did not fit and leaves the caller's
        // handle untouched, so a partial insert stays undoable.
        public int Insert(FakeItem item)
        {
            if (item == null || item.Stack <= 0)
                return 0;

            int space = Capacity - TotalUnits;
            int stored = Math.Min(space, item.Stack);
            Log.Add($"insert {item}->{stored}");

            if (stored > 0)
            {
                _counts.TryGetValue(item.Type, out int have);
                _counts[item.Type] = have + stored;
            }

            return item.Stack - stored;
        }

        public int StackOf(FakeItem item) => item == null ? 0 : item.Stack;
    }

    // Produces each step's output from a fixed table, with no Terraria item construction.
    public sealed class FakeStepProducer : IStepProducer<FakeItem>
    {
        private readonly IReadOnlyList<ExecutionStep> _steps;

        public readonly List<int> Prepared = new();

        public FakeStepProducer(IReadOnlyList<ExecutionStep> steps)
        {
            _steps = steps;
        }

        public void PrepareStep(int stepIndex) => Prepared.Add(stepIndex);

        public FakeItem ProduceStep(int stepIndex)
            => new FakeItem { Type = _steps[stepIndex].ProducedType, Stack = _steps[stepIndex].ProducedCount };

        public (FakeItem excess, FakeItem kept) SplitOffExcess(FakeItem produced, int excess)
        {
            var excessItem = new FakeItem { Type = produced.Type, Stack = excess, Mark = produced.Mark };
            produced.Stack -= excess;
            return (excessItem, produced);
        }
    }
}
