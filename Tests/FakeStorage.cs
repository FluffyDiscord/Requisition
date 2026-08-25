using System;
using System.Collections.Generic;
using System.Linq;
using TerraStorage.Common;
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

    // A stack-level storage network. Capacity is a total unit count, which is all the transaction
    // core ever needs to know about "storage is full".
    //
    // Withdrawals go through StackSelection.PlanWithdrawal, the same rule DiskData.ExtractItem
    // carries out, so a type held as several stacks that each stand for themselves behaves here
    // exactly as it does in a real network: the count says one thing, the withdrawal another.
    public sealed class FakeStorage : ICraftingStorage<FakeItem>
    {
        private sealed class Stack
        {
            public int Type;
            public int Count;
            public bool IsUnique;
        }

        private readonly List<Stack> _stacks = new();
        private readonly HashSet<int> _uniqueTypes = new();

        public int Capacity = int.MaxValue;
        public readonly List<string> Log = new();

        public FakeStorage With(int itemType, int count)
        {
            AddStacks(itemType, count);
            return this;
        }

        // Every stack of this type stands for itself and holds a single unit - a storage disk, an
        // unloaded item, anything a mod refuses to stack.
        public FakeStorage WithUniqueType(int itemType)
        {
            _uniqueTypes.Add(itemType);
            return this;
        }

        // Stock laid out as given rather than as one stack: armour holds one unit per stack, so 18
        // pieces are 18 stacks whether or not any of them stands for itself.
        public FakeStorage WithStacks(int itemType, params int[] sizes)
        {
            foreach (int size in sizes)
                _stacks.Add(new Stack { Type = itemType, Count = size, IsUnique = _uniqueTypes.Contains(itemType) });
            return this;
        }

        public int TotalUnits => _stacks.Sum(s => s.Count);

        public FakeItem Nothing => null;

        public int CountItem(int itemType)
            => _stacks.Where(s => s.Type == itemType).Sum(s => s.Count);

        // What a withdrawal of this type could actually hand over, which is not the same number as
        // CountItem whenever unique stacks are involved.
        public int WithdrawableCount(int itemType)
            => StackSelection.WithdrawableCount(MatchingSlots(itemType));

        public FakeItem Extract(int itemType, int amount)
        {
            var matching = MatchingSlots(itemType);
            var draws = StackSelection.PlanWithdrawal(matching, amount, allowUniqueFallback: true, out _);

            int taken = 0;
            foreach (var draw in draws)
            {
                var stack = _stacks[draw.Index];
                stack.Count -= draw.Count;
                taken += draw.Count;
            }
            _stacks.RemoveAll(s => s.Count <= 0);

            Log.Add($"extract {itemType}x{amount}->{taken}");

            if (taken <= 0)
                return null;

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
                AddStacks(item.Type, stored);

            return item.Stack - stored;
        }

        public int StackOf(FakeItem item) => item == null ? 0 : item.Stack;

        public FakeItem SplitOff(FakeItem item, int count)
        {
            var part = new FakeItem { Type = item.Type, Stack = count, Mark = item.Mark };
            item.Stack -= count;
            return part;
        }

        private void AddStacks(int itemType, int count)
        {
            if (!_uniqueTypes.Contains(itemType))
            {
                _stacks.Add(new Stack { Type = itemType, Count = count });
                return;
            }

            for (int unit = 0; unit < count; unit++)
                _stacks.Add(new Stack { Type = itemType, Count = 1, IsUnique = true });
        }

        private List<StackSlot> MatchingSlots(int itemType)
        {
            var matching = new List<StackSlot>();
            for (int index = 0; index < _stacks.Count; index++)
            {
                if (_stacks[index].Type != itemType)
                    continue;

                matching.Add(new StackSlot
                {
                    Index = index,
                    Stack = _stacks[index].Count,
                    IsUnique = _stacks[index].IsUnique
                });
            }
            return matching;
        }
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
    }
}
