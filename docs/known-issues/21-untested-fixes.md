# Half the 2026-08-24 fixes had no unit-test surface

**Severity:** HIGH — process gap, not a runtime defect
**Area:** `Tests/` project scope
**Status:** FIXED 2026-08-25 — READY FOR TESTING / HUMAN REVIEW

## The problem

`Tests/Tests.csproj` compiled a deliberately tiny set of files: the resolver core plus four
Terraria-free UI helpers. Everything else in the mod touches `Terraria.Item`, `TagCompound`,
`Main.*` or `ModPacket`, so it could not be linked into the runner and was not covered at all.

Of the 20 fixes made on 2026-08-24, **9 were pinned by assertions and 11 rested on reading the code
and one in-game build**. The untested half was also the half that moves items:

| Issue | Fix | Now covered by |
|---|---|---|
| [01](01-disk-upgrade-undercharges.md) disk upgrade undercharge | `TryConsumeMaterials` | `TX-01`..`TX-07` |
| [02](02-server-upgrade-no-material-check.md) server upgrade gate | same | `TX-*` |
| [03](03-executeplan-unchecked-extract-insert.md) `ExecutePlan` refund | checked extract + abort | `PX-01`..`PX-06` |
| [04](04-defragment-destroys-per-instance-data.md) defragment identity | `CanMergeStacks` | `DF-01`..`DF-06` |
| [05](05-extractitem-stamps-tag-on-whole-withdrawal.md) extract tag stamping | two-pass extract | `SL-01`..`SL-06` |
| [09](09-output-slot-cache-ignores-disk-set.md) output-slot cache | version reset | `RC-01`..`RC-04a` |
| [12](12-storagediskbase-clone-drops-fullitemtag.md) clone drops tag | one field | still in-game only |
| [13](13-partial-deposit-reports-failure.md) deposit flag | `DepositOutcome` | `DP-01`..`DP-06` |
| [14](14-recipe-conditions-snapshotted-once.md) live conditions | timed re-check | `RC-08`..`RC-10a` |
| [15](15-favorites-version-not-polled.md) favorites version | version poll | `RC-05`..`RC-06` |
| [16](16-favorites-hit-rects-outside-clip.md) clipped hit rects | visibility test | `HR-01`..`HR-08` |

An item-duplication fix that nothing asserts is one refactor away from silently coming back — and
these are the bugs that cost a player their save, not their patience.

## It paid for itself immediately

Writing the first assertion this file asked for found
[22](22-aborted-plan-keeps-its-intermediates.md): a multi-step craft that aborts refunded the
materials **and** kept the intermediate made from them. Shipped, reachable, and a duplication bug.

It was not found by reading the code. The refund path had been read carefully twice, most recently
while writing the fix for [03](03-executeplan-unchecked-extract-insert.md), and looked right both
times. What found it was writing down "extraction comes up short mid-plan → every earlier
extraction refunded" as an executable sentence and then asking the obvious follow-up: *and what
happened to the plank?*

A second, smaller leak in the same shape (`TryConsumeMaterials` dropping a crafted shortfall that
would not fit) came out of the same exercise.

## What the codebase already did right

The resolver was made testable by pushing the algorithm behind `IRecipeEnvironment` and keeping
`CoreResolver` free of Terraria. `WindowStackCore`, `DepositGate`, `UIClickBlocker` and
`FavoritesRowCache` follow the same pattern — Terraria-free precisely so they can be linked into
the runner.

**Every extraction below is the same move applied once more.** None needed a mocking framework, and
none changed shipped behaviour except where it fixed [22](22-aborted-plan-keeps-its-intermediates.md).

## Extractions made

### 1. `ICraftingStorage<TItem>` for the consume/execute transaction — covers 01, 02, 03

`Helpers/Resolver/CraftingTransaction.cs`. Storage reduced to four operations over opaque item
handles: `CountItem`, `Extract`, `Insert`, `StackOf`. Terraria binds `TItem` to `Item`; the tests
bind it to a plain class and a dictionary.

- `RefundLedger<TItem>` — everything taken this run, and putting it back
- `MaterialConsumer<TItem>` — the all-or-nothing material list behind both disk-upgrade paths
- `PlanExecutor<TItem>` — the plan's step loop
- `IStepProducer<TItem>` — the Terraria-only half (building an item, carrying a disk GUID across an
  upgrade, splitting batch excess), so it stays out of the core

`RecipeResolver` keeps `WorldCraftingStorage` and `PlanStepProducer` as the live bindings. Assertions
cover every bullet this file originally listed, including the two that turned out to be wrong.

### 2. `StackSelection` — covers 04, 05

`Common/StackSelection.cs`. Deciding whether a stack has per-instance data needs NBT and stays on
`DiskData`; deciding what to *do* about that verdict does not.

- `PlanWithdrawal` — which stacks a bulk extract draws from, in what order, and when the unique
  fallback applies. `DiskData.ExtractItem` now carries out its plan.
- `PlanDonorMove` — what moving one donor stack onto a target disk comes to: merges into partials,
  fresh slots, what stays behind, and the rule that a unique stack moves whole or not at all.
  `StorageWorldSystem.Defragment` now carries out its plan.

`SL-01` is the reported shape verbatim: a unique stack sorted first, 300 plain units behind it,
withdrawn as 300 — returns 300 plain and leaves the unique stack alone.

### 3. `PanelRefreshCache` — covers 09, 14, 15

`Content/UI/PanelRefreshCache.cs`, extracted the way `FavoritesRowCache` already was. Every stamp
that says whether the panel's derived state has gone stale: the output slot's `(storageVersion,
outputType)` pair plus an explicit `InvalidateOutputStock()` for a disk-set change, the favorites
version, the storage version, and the condition re-check interval. `ApplyFlags` carries the
"only re-filter when a flag actually flipped" rule and the stale-array guard.

`RC-10` covers the `uint` tick wrap, which nothing had looked at.

### 4. Row visibility on `FavoritesRowCache` — covers 16

`IsHitRectVisible` and `GetBodyBottom`. The rule turned out to be simpler than it read: a row
registers a hit rect when its rect's **bottom edge** lies within the clipped body. `HR-06` builds 40
rows into a 200px body and asserts exactly 5 register.

### 5. `DepositOutcome` — covers 13

`Common/DepositOutcome.cs`. The offered count and the leftover held as one value, with `Deposited`,
`AnyDeposited` and `NeedsReturn` derived from them, so reading the offered count after overwriting
it is unspellable. Both deposit sites in `NetworkHandler` build one before touching `item.stack`.

## Still not extracted

Packet read/write ordering, `SendSyncDriveBay`, prefix rolling and mod `OnCreated` hooks. These are
thin adapters over tModLoader and a fake would only assert that the fake was called.

[12](12-storagediskbase-clone-drops-fullitemtag.md) is one field assignment in a `Clone` override
and stays in-game only.

Multiplayer behaviour ([02](02-server-upgrade-no-material-check.md),
[12](12-storagediskbase-clone-drops-fullitemtag.md),
[13](13-partial-deposit-reports-failure.md)) still needs a real two-client session — but what that
session has to prove has shrunk to "the packet reaches the transaction", not "the transaction is
correct".

## Lesson worth keeping

[20](20-depth-origin-off-by-one.md) shipped a passing test over a defect because the test sampled
`{1, 2, 5, 10, 20}` and the only divergent value was 11. Where a rule has a boundary, sweep it
contiguously; where two components encode the same rule, assert they agree across a range rather
than at hand-picked points. `SA-*` and `LF-*` are the pattern to copy.

And the one this file added: **the assertion you write to confirm a fix is where you find the next
bug.** Reading the code twice did not find [22](22-aborted-plan-keeps-its-intermediates.md).
Writing down what the code was supposed to guarantee did.
