using System;
using System.Collections.Generic;

namespace TerraStorage.Helpers.Resolver
{
    // Storage as the crafting transaction sees it: four operations over opaque item handles.
    // Terraria binds TItem to Item; the tests bind it to a plain struct, which is what makes the
    // consume/execute bookkeeping assertable without Terraria, TagCompound or a live world.
    //
    // Insert must NOT mutate the handle it is given - it reports how many units did not fit and
    // leaves the caller holding the original stack, so a partial insert can be undone.
    public interface ICraftingStorage<TItem>
    {
        TItem Nothing { get; }

        int CountItem(int itemType);

        // Best-effort: returns a handle for however much was actually removed, possibly nothing.
        TItem Extract(int itemType, int amount);

        // Returns the number of units that did not fit.
        int Insert(TItem item);

        // 0 for an empty handle.
        int StackOf(TItem item);

        // Splits `count` units off, returning a handle for them and leaving `item` describing the
        // rest. Only called with 0 < count < StackOf(item).
        TItem SplitOff(TItem item, int count);
    }

    // One crafting step's material bookkeeping, free of Terraria types.
    public class ExecutionStep
    {
        public List<(int itemType, int count)> Consumed { get; set; } = new();
        public int ProducedType { get; set; }
        public int ProducedCount { get; set; }
    }

    // Everything one transaction has pulled out of storage, so a failure at any point can put it
    // all back. The items came out of these disks moments ago, so the space is there; a leftover
    // would mean storage shrank underneath us, and dropping it is still better than consuming it
    // for nothing.
    public class RefundLedger<TItem>
    {
        private readonly ICraftingStorage<TItem> _storage;
        private readonly List<(TItem item, int itemType)> _taken = new();
        private readonly Dictionary<int, int> _conjured = new();

        public RefundLedger(ICraftingStorage<TItem> storage)
        {
            _storage = storage;
        }

        // Extracts exactly `amount`, or reports failure. Extract is a best-effort partial
        // extractor, so an unchecked call lets a step consume less than its recipe listed and
        // still produce the output. Whatever did come out is recorded either way.
        public bool TryTakeExact(int itemType, int amount)
        {
            TItem extracted = _storage.Extract(itemType, amount);
            int extractedStack = _storage.StackOf(extracted);

            if (extractedStack > 0)
                _taken.Add((extracted, itemType));

            return extractedStack >= amount;
        }

        // Units of this type that this run created rather than the player owning them. A later
        // step extracts them back out of storage mixed in with the player's own stock, so the
        // refund has to know how many of what it is holding must NOT be handed back.
        public void MarkConjured(int itemType, int count)
        {
            _conjured.TryGetValue(itemType, out int already);
            _conjured[itemType] = already + count;
        }

        // Puts back what the player owned, withholding conjured units as it goes. Withholding
        // during the refund rather than re-extracting afterwards is what keeps it correct on a
        // network with no spare room: the end state is the start state, which fits by definition,
        // whereas inserting everything first overflows by exactly the conjured amount and drops
        // real materials as leftover.
        public void Refund()
        {
            foreach (var (item, itemType) in _taken)
            {
                TItem toInsert = item;

                _conjured.TryGetValue(itemType, out int outstanding);
                if (outstanding > 0)
                {
                    int stack = _storage.StackOf(item);
                    int withheld = Math.Min(outstanding, stack);
                    _conjured[itemType] = outstanding - withheld;

                    if (withheld >= stack)
                        continue; // the whole handle was conjured

                    toInsert = _storage.SplitOff(item, stack - withheld);
                }

                _storage.Insert(toInsert);
            }

            _taken.Clear();
        }

        // Conjured units the refund never saw, because no later step consumed them - they are
        // still sitting in storage where the step that made them put them.
        public List<(int itemType, int count)> DrainRemainingConjured()
        {
            var remaining = new List<(int itemType, int count)>();

            foreach (var pair in _conjured)
            {
                if (pair.Value > 0)
                    remaining.Add((pair.Key, pair.Value));
            }

            _conjured.Clear();
            return remaining;
        }
    }

    // The transaction behind both disk-upgrade paths (the panel in single player, the packet
    // handler on a server): either the whole material list is taken and true is returned, or
    // nothing is consumed and everything already taken is put back.
    public class MaterialConsumer<TItem>
    {
        private readonly ICraftingStorage<TItem> _storage;
        private readonly Func<int, int, TItem> _craftShortfall;

        // craftShortfall(itemType, totalNeeded) crafts the material and returns what was produced,
        // or Nothing when the craft is impossible. It is asked for the FULL need, not the
        // shortfall: a resolver asked for `need - have` rebuilds its pool from all of storage,
        // sees the stock the caller already subtracted, and reports a direct extract with no
        // steps - feasible, free, and wrong.
        public MaterialConsumer(ICraftingStorage<TItem> storage, Func<int, int, TItem> craftShortfall)
        {
            _storage = storage;
            _craftShortfall = craftShortfall;
        }

        public bool TryConsume(IEnumerable<(int itemType, int count)> materials)
        {
            var ledger = new RefundLedger<TItem>(_storage);

            foreach (var (itemType, needed) in materials)
            {
                if (needed <= 0)
                    continue;

                if (!TryStockUp(itemType, needed))
                {
                    ledger.Refund();
                    return false;
                }

                if (ledger.TryTakeExact(itemType, needed))
                    continue;

                ledger.Refund();
                return false;
            }

            return true;
        }

        // Brings storage up to `needed` of this material, crafting the shortfall if there is one.
        private bool TryStockUp(int itemType, int needed)
        {
            int have = _storage.CountItem(itemType);
            if (have >= needed)
                return true;

            TItem crafted = _craftShortfall(itemType, needed);
            if (_storage.StackOf(crafted) <= 0)
                return false;

            // A leftover means storage is full, so the units that did not fit are gone and the
            // extract that follows would come up short anyway. Fail here instead, while the ledger
            // can still put back everything earlier materials cost.
            int craftedStack = _storage.StackOf(crafted);
            int leftover = _storage.Insert(crafted);
            if (leftover <= 0)
                return true;

            // Take back the part that did land, so the refund cannot be blocked by a product the
            // caller is about to abandon anyway.
            int stored = craftedStack - leftover;
            if (stored > 0)
                TakeBack(itemType, stored);

            return false;
        }

        // Extract hands back one stack per call for a type held as stacks that each stand for
        // themselves, so a single call would abandon the rest in storage.
        private void TakeBack(int itemType, int count)
        {
            int remaining = count;

            while (remaining > 0)
            {
                TItem returned = _storage.Extract(itemType, remaining);
                int taken = _storage.StackOf(returned);
                if (taken <= 0)
                    return;

                remaining -= taken;
            }
        }
    }

    // The Terraria-only half of executing a plan. Building an item, transferring a disk GUID and
    // rolling a prefix all need the real world, so they live behind this; everything that decides
    // whether materials move stays in PlanExecutor.
    public interface IStepProducer<TItem>
    {
        // Runs before a step's materials are taken, while storage still holds them - a disk
        // upgrade reads the source disk's GUID here, since extraction is about to remove it.
        void PrepareStep(int stepIndex);

        // Runs once the step is fully paid for.
        TItem ProduceStep(int stepIndex);
    }

    // The material bookkeeping of a crafting plan: pay for every step up front, store each
    // intermediate so the next step can consume it, and hand back the final product. Any shortfall
    // aborts with everything this run took already put back, so a failed craft never eats materials.
    public class PlanExecutor<TItem>
    {
        private readonly ICraftingStorage<TItem> _storage;

        public PlanExecutor(ICraftingStorage<TItem> storage)
        {
            _storage = storage;
        }

        public TItem Run(IReadOnlyList<ExecutionStep> steps, int finalItemCount, IStepProducer<TItem> producer)
        {
            var ledger = new RefundLedger<TItem>(_storage);
            var intermediates = new List<(int itemType, int count)>();
            TItem finalResult = _storage.Nothing;

            for (int stepIndex = 0; stepIndex < steps.Count; stepIndex++)
            {
                ExecutionStep step = steps[stepIndex];
                producer.PrepareStep(stepIndex);

                if (!TryPayFor(step, ledger))
                    return Abort(ledger, intermediates);

                TItem produced = producer.ProduceStep(stepIndex);
                bool isFinalStep = stepIndex == steps.Count - 1;

                if (isFinalStep)
                {
                    finalResult = StoreExcess(produced, finalItemCount);
                    continue;
                }

                int producedStack = _storage.StackOf(produced);
                if (!TryStoreIntermediate(produced, step.ProducedType))
                    return Abort(ledger, intermediates);

                intermediates.Add((step.ProducedType, producedStack));
            }

            return finalResult;
        }

        // Materials go back, but anything this run conjured must not. A later step consumes an
        // earlier step's intermediate, which puts it in the ledger; refunding alone would hand it
        // back alongside the ingredients it was made from and leave the player holding both.
        private TItem Abort(RefundLedger<TItem> ledger, List<(int itemType, int count)> intermediates)
        {
            foreach (var (itemType, count) in intermediates)
                ledger.MarkConjured(itemType, count);

            ledger.Refund();

            // Whatever no later step consumed is still where the step that made it left it.
            foreach (var (itemType, count) in ledger.DrainRemainingConjured())
                TakeBack(itemType, count);

            return _storage.Nothing;
        }

        // Extract is best-effort and hands back one stack per call when the type is held as stacks
        // that each stand for themselves. Taking a conjured product back in a single call therefore
        // left the rest in storage - the ingredients refunded AND the thing made from them kept.
        private void TakeBack(int itemType, int count)
        {
            int remaining = count;

            while (remaining > 0)
            {
                TItem returned = _storage.Extract(itemType, remaining);
                int taken = _storage.StackOf(returned);
                if (taken <= 0)
                    return;

                remaining -= taken;
            }
        }

        private bool TryPayFor(ExecutionStep step, RefundLedger<TItem> ledger)
        {
            foreach (var (itemType, count) in step.Consumed)
            {
                // Storage no longer holds what the plan was built against. The caller puts back
                // everything this run took and produces nothing, rather than hand over an
                // underpaid item.
                if (!ledger.TryTakeExact(itemType, count))
                    return false;
            }

            return true;
        }

        // Never routes the final item through storage: a full store would swallow the insert, the
        // following extract would return nothing, and the caller would get an empty handle with
        // the ingredients already spent. Only batch-rounded excess is stored, and losing that on a
        // full store is acceptable.
        private TItem StoreExcess(TItem produced, int finalItemCount)
        {
            int producedStack = _storage.StackOf(produced);
            int excess = producedStack - finalItemCount;
            if (excess <= 0)
                return produced;

            TItem excessItem = _storage.SplitOff(produced, excess);
            _storage.Insert(excessItem);
            return produced;
        }

        // An intermediate has to land in storage for the next step to consume it. A leftover means
        // storage is full, so the next step would extract less than it needs.
        private bool TryStoreIntermediate(TItem produced, int producedType)
        {
            int producedStack = _storage.StackOf(produced);
            int leftover = _storage.Insert(produced);
            if (leftover <= 0)
                return true;

            // Take back the part that did land, so refunding the materials cannot be blocked by
            // the very product they were spent on. The intermediate is then discarded, which loses
            // nothing: its ingredients go back untouched.
            int stored = producedStack - leftover;
            if (stored > 0)
                TakeBack(producedType, stored);

            return false;
        }
    }
}
