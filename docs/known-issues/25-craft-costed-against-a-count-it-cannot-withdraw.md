# A step asked storage for twenty and took one

**Severity:** HIGH — a green CRAFT button that did nothing at all
**Area:** crafting transaction, withdrawal, crafting panel
**Status:** FIXED 2026-08-25 — READY FOR TESTING / HUMAN REVIEW

## Symptom

Band of Door (item 18120 = 1× Shackle + 20× Door Pants, at a Work Bench) showed as craftable and
the CRAFT button was green. Clicking it did nothing: no sound, no message, no item, no change to
storage. Indistinguishable from clicking dead panel background.

## Cause

`RefundLedger.TryTakeExact` paid for a step with **one** `Extract` call.

`Extract` is best-effort. `StackSelection.PlanWithdrawal` drains plain stacks, but a stack that
stands for itself is only ever taken **alone** — its mod state describes those units and no others,
so folding several into one returned item is [05](05-extractitem-stamps-tag-on-whole-withdrawal.md).
One call therefore answers a request for twenty with one.

Door Pants are armour (`maxStack = 1`), so 18 of them are 18 stacks. In build 0.5.15 — the one the
report came from, which predates [24](24-globaldata-treated-as-item-identity.md) — every stack
reported `IsUnique`. The step asked for 20 and got 1.

Everything downstream then behaved correctly and invisibly: `TryTakeExact` refused the short draw,
`PlanExecutor` aborted and refunded, `ExecutePlan` returned air, and `ExecuteCraft` returned without
telling anyone.

## Fix

`TryTakeExact` loops until the amount is met or storage stops yielding. The one-stack-at-a-time rule
is untouched — each draw is kept as **its own handle** in `_taken`, so no stack's state is folded
into another's. Twenty stacks pay for twenty units.

`RefundLedger.Refund` then had to change direction. It withheld conjured units from the **front** of
`_taken`, but a step's product is inserted after the stock the player already had, so extraction
hands it back **last**: the front of the list is always the player's own stacks. A failed craft put
the right count back and the wrong items — the player's stacks dropped, stateless copies in their
place. With one handle per type that was invisible; with one handle per stack it is the difference
between keeping an enchanted item and losing it. `Refund` now withholds from the end.

`PlanExecutor.Abort`, `TryStoreIntermediate` and `MaterialConsumer.TryStockUp` each carried their own
copy of the same take-back loop; they are now one `StorageRecovery.TakeBack`.

`IsDiskUpgradeStep` now refuses a step consuming more than one disk. Only one GUID is read and one
result Item built, so a batched step would stamp that GUID on every disk it made. Nothing registers
such a recipe today; before the loop, `TryTakeExact` refused the draw and hid it.

`StorageWorldSystem.ExtractItem` let the first disk fall back to a unique stack and **returned**,
so one such stack on disk 1 masked pooled stock on disk 2. It now drains pooled stock across the
whole network before the fallback applies.

Draining across disks then needed the guard `DiskData.AllDrawsShareModState` already applies within
one: `result ??= extracted; result.stack = totalExtracted` folded every disk's units into the first
disk's Item, wearing the first disk's tag. Two weapons of the same type on two disks came back as
one 2-stack carrying one weapon's state, and re-inserting split that state across both — the other
weapon's gone. `DiskData.ExtractItem` now reports the tag its result carries, and `ExtractItem`
stops folding when a disk's state would be discarded, putting that draw straight back into the disk
whose slots it just freed. Callers already handle a short return; `TryTakeExact` asks again.

`ExecuteCraft` had four bail-outs that returned in silence. Each now says why.

## Fix — 25-B, the reason now crosses the wire

`EndTrackingAndRespond` sent a success flag; `HandleOperationResponse` wrote it to the debug file and
discarded it. A client's denied operation was "click, nothing happens" no matter what the server
knew. The response now carries the reason.

**The wire.** `StorageOperationFailure` (`Common/StorageOperationFailure.cs`) is a `byte` enum, and
`OperationResponse` appends it after the existing `success` bool — **only when `success` is false**,
so a successful response is still the two bytes it always was and the state "denied, for no reason"
is unrepresentable rather than merely discouraged. A raw string was rejected: it is an unbounded
untrusted payload, and `Language.GetTextValue` of an unknown key returns the key verbatim, so a key
sent as text is a chat-injection vector. A new `PacketType` was rejected too — it costs four edit
points instead of two, carries the identical append-only irreversibility, and adds a second
uncorrelated packet that would have to be ordered against the correction packets that already follow
a denial. `Common/CraftingCondition.cs` is the precedent: a byte enum already crosses this wire.

**The numbers are the format.** Members may only ever be appended; renumbering one silently
mistranslates every refusal a peer reports. `DN-14` pins each value and the member count, because
every other assertion in the set passes under any numbering — without it the one irreversible
decision in this change would ship unguarded.

**Version skew is not a concern, and no stream guard is possible.** Decompiling tModLoader
(`1.4.4.9+2026.06.3.6`) settled both: `ModNet.ModHeader.Matches` requires name, version **and** the
20-byte SHA-1 of the `.tmod` to match before a client may join, so a peer that does not write the
byte cannot be in the session. And the `BinaryReader` handed to `Mod.HandlePacket` is one long-lived
stream over a shared 131 070-byte connection buffer, not a per-packet stream — a
`Position < Length` guard is *always true*, so it would not catch a short payload, it would return
the next packet's first byte. The guard that looked prudent was deleted on that evidence.

**One vocabulary, one decision.** The four singleplayer messages moved verbatim into
`UI.OperationFailed` in both catalogs, behind a shared `Prefix` key so `Requisition: ` has one
definition instead of sixteen. More importantly the *decision* is shared: `GetCraftFailure` is one
pure function that both `ExecuteCraft` and `HandleCraftRequest` call. Two hand-written copies of one
rule is exactly the shape of [23a, 23b and 23c](23-agent-audit-2026-08-25.md), and neither of those
two files can be compiled outside the game — so the branch table lives where the runner executes it.

**A denial burst is one line — but only off the wire.** "Deposit all" sends one packet per
inventory slot (`TerminalUIState.cs:838`), so a full network denies up to forty times per click. A
repeat of the same cause within 60 ticks is suppressed; a different cause never is. The throttle is
reached through `ReportServerDenial` and **only** from `HandleOperationResponse`. The panel's own
refusals go through `ReportFailure` unthrottled, because a locally decided refusal is already one
per click: a double-click is 12-30 ticks apart, well inside the window, so throttling it would have
swallowed the second click and restored the exact silence this issue is about.

**A denial that changed nothing does not drag a resync behind it.** `SendOperationResponse`'s
failure arm sends a full `SendDiskPacket` for every disk it is given, to repair client state the
server rejected. Quick-stacking into a full network used to report *success* (it counted slots
tried, not units moved), so it sent none; naively reporting the new `NothingDeposited` there would
have turned a spammable button into a full-network resync storm for an operation that modified
nothing. That path now passes no disk ids — the reason travels, the corrections do not. The
nothing-matched path keeps the corrections it always sent.

**Two defects found while doing it.** `HandleQuickStackToStorage` added to `results` even when the
whole stack bounced, so quick-stacking into a full network reported **success with zero deltas** and
said nothing — this issue's own symptom, alive in the path meant to report it. It now decides from
`DepositOutcome`, and distinguishes "nothing matched" from "nothing fitted". Separately, six refusals
in `HandleDepositItemAtPosition` and `HandleQuickStackToStorage` returned before tracking began and
answered nothing at all; `RefuseOperation` now answers them — `SendOperationResponse` touches no
tracking, so the helper they appeared to need never existed.

### Still silent

Roughly nineteen refusal points across four handlers still answer nothing:
`HandleUpgradeDiskRequest` (5), `HandleRestoreDiskRequest` (3-4), `HandleDefragRequest` (2, plus a
silent no-op when `modified.Count == 0`) and `HandleArchiveDiskRequest` (4 — its `ArchiveDiskResult`
packet is sent only on success, so it is not the feedback channel it looks like). All but Upgrade
have `whoAmI` in hand and are one `RefuseOperation` line each; **`HandleUpgradeDiskRequest(Mod,
BinaryReader)` has no `whoAmI` parameter at all**, so it needs a signature change first. They are
deferred because they are disk-management refusals needing their own vocabulary, not storage-operation
ones, and because a sibling agent is working in this file.

### Needs a real two-client session

Nothing below has a unit-test surface — [21](21-untested-fixes.md) explains why packet ordering does
not get one here, and `Main.NewText` cannot be linked into the runner.

1. A denied craft prints **one** line naming the right cause. Drive all four: no materials;
   craft-to-inventory with a full inventory; full storage **and** full inventory; a second client
   emptying the network between the plan and `ExecutePlan`.
2. A **successful** craft prints nothing, and its response is still two bytes.
3. "Deposit all" into a full network prints **one** line, not forty.
4. Denied withdraw, deposit, quick-stack, out-of-range and no-disks each print their own line.
5. Quick-stack into a full network now says so instead of silently confirming.
6. The correction packets after a denial still arrive and still resync the client — the appended
   byte did not disturb what follows it.
7. The server prints nothing locally; the denial sound is distinguishable from the send tick.
8. Two *different* denials in one tick: both are heard (the throttle keys on the cause).
9. **Singleplayer, clicked twice quickly** — a full inventory and two CRAFT clicks 300 ms apart must
   print **two** lines. This is the regression the throttle introduced and `ReportFailure` undoes;
   it has no unit-test surface because the split is in which entry point each caller uses.
10. Quick-stack into a full network prints its line and does **not** trigger a full-disk resync —
    watch the packet volume, not just the chat.

**Accepted risk:** the response carries no correlation id, so two operations denied at nearly the
same moment can attribute a reason to the wrong click. Today nothing is displayed at all, so this is
a new risk rather than a pre-existing one; it is accepted because the causes are distinct enough to
read and a correlation id is a much larger protocol change.

## Not fixed

- **A step needing N units walks every disk N times.** `TryTakeExact` loops, and each iteration
  re-runs the whole network sweep. Fine for the twenty stacks this was reported over; worth
  measuring before it matters on a large network. The fix both callers now want is for
  `ExtractItem` to drain up to `count` in one sweep and return a `List<Item>` of per-stack handles —
  which would also let the state-mismatch case above hand back both disks' units instead of
  stopping at the first.
- **`TakeBack` recovers by type, not by handle.** For a type whose stacks stand for themselves it
  may take back the player's own stack rather than the one the run conjured. The unit arithmetic
  balances; the identity does not. `ExtractItemWithFullItemTag` / `ExtractItemWithModData` already
  exist for handle-precise recovery. Pre-existing, unchanged in kind by this fix.
- **`StorageWorldSystem` and `DiskData` are not linked into the test project**, so the two-pass
  drain and everything above is verified only in-game.

## Verified by

`DN-*` covers 25-B's testable half: the wire codes and their pinned byte values, the craft decision's
full sixteen-row truth table, the quick-stack decision, the burst throttle (including the
`GameUpdateCount` wrap), both catalogs on disk, and a source scan asserting every named cause is a
real enum member and no site settles for `Unspecified`. That last one is deliberately the compiler
this change does not otherwise get. Reverting the enum's numbering turns `DN-14i`/`DN-14j` red;
changing `GetCraftFailure`'s `||` to `&&` turns `DN-09e`, `DN-09f` and `DN-09g` red.

`BD-*`, `ID-*`, `FX-*` and `PX-07` in `Tests/Program.cs`. Reverting only the loop in
`TryTakeExact` turns `BD-02*` and `FX-06*` red with the reported outcome — the craft produces
nothing and all 18 Door Pants stay put. Reverting only `Refund`'s direction turns `ID-04` red with
one of the player's three stacks left and two stateless copies in place of the others.

`ID-02` rules out merging handles, which `PlanWithdrawal` cannot produce today — it guards the rule,
not this change. `ID-04` is the guard on this change. `FX-*` runs against
`Tests/Fixtures/band-of-door.tsdump.txt`, a three-hop slice of the reported `/tsdump` that resolves
to the same three steps as the full 14,178-recipe graph.

`/tsdump` now writes item names on every storage and recipe line. Item type ids are assigned at load
time, so a dump without names cannot be read against anyone else's mod list.

Needs in-game testing: craft Band of Door; craft something whose material is spread over two disks;
upgrade a storage disk (always its own item, and consumed as an ingredient); confirm a craft that
cannot be paid for now prints a reason.

## Related

[05](05-extractitem-stamps-tag-on-whole-withdrawal.md),
[22](22-aborted-plan-keeps-its-intermediates.md),
[24](24-globaldata-treated-as-item-identity.md).
