# Six handlers drained any disk in the world, because the packet named the disks instead of the Terminal

**Severity:** CRITICAL — a GUID plus one packet drained any disk in the world from any distance
**Area:** networking (storage operations, Drive Bay slots, disk management, refusal vocabulary)
**Status:** FIXED 2026-08-26 — READY FOR TESTING / HUMAN REVIEW

These are the five holes [23](23-agent-audit-2026-08-25.md) confirmed and deliberately left open at
the end of its file, plus the ~20 silent refusal points
[25](25-craft-costed-against-a-count-it-cannot-withdraw.md) recorded under "Still silent".

Doc 23 left them open because each needed a design decision, and doc
[20](20-depth-origin-off-by-one.md) is this project's standing reminder that a correct-looking
speculative fix is the expensive kind of mistake. The decisions are below, with what each costs.

---

## 1. The packet named the disks; now it names the Terminal — CRITICAL

`HandleWithdrawItem`, `HandleWithdrawItemByModData`, `HandleWithdrawItemByFullItemTag`,
`HandleDepositItem`, `HandleCraftRequest` and `HandleRequestDiskData` acted on whatever GUIDs the
packet listed. Disk GUIDs reach every client — `DriveBayEntity.NetSend` sends all 40 slot items and
`StorageDiskBase.NetSend` writes the full 16 bytes — so naming one proved nothing. A GUID read out of
ordinary bay traffic plus one `WithdrawItem` drained that disk from anywhere in the world.

**The obvious fix does not work, and doc 23 said so.** Filtering every list through
`GetReachableDiskIds` breaks the Remote Terminal, which opens a Terminal's UI from anywhere
(`RemoteTerminal.cs:48` → `TerminalUISystem.OpenTerminalRemote:67-71`, whose `_remoteOpen` flag skips
the 15-tile close at `:93-102`).

That was not hypothetical. **Defragment was already broken this way on `master`:** it was the one
handler scoped by range, its only trigger is the Terminal's Disks tab, and a Remote Terminal user
computed an empty reachable set and was silently refused. Repairing that is part of this change.

### What was built instead

> **Every storage packet carries the tile-entity id of the Terminal it was issued from, and no disk
> GUID list. The server resolves the entity, checks the sender may operate it, and derives the disk
> list itself.**

Three steps, one encoding (`TryResolveOperableTerminal`), and all three refusals are causes the file
already had — the same three `HandleDepositItemAtPosition` and `HandleQuickStackToStorage` have always
sent for the same three conditions:

| step | refusal |
|---|---|
| the id names a live `TerminalEntity` | `NoTerminalFound` |
| the sender may operate it | `NoStorageInRange` |
| its network holds at least one disk | `NoStorageConnected` |

This is the model 23g already called correct and three shipped handlers already used —
`HandleUpgradeDiskRequest` read the packet's list *only to advance the stream* and re-derived the
network from the entity. The change generalises that rather than inventing a validator.

**What it deletes** rather than adds: the GUID list from seven packet formats, `GetReachableDiskIds`'s
world scan, `EnsureDisksRegistered`'s sweep over every bay in the world, and `ReadGuidList` — with it,
the wire-count allocation surface [26](26-forged-disk-packets.md) had to bound on six handlers.

### Proximity: what it means, and what it is honestly worth

> **A Terminal is operable when the sender is within 15 tiles of it, or the sender's inventory holds
> a Remote Terminal.**

Not "a Remote Terminal *bound to that Terminal*". The bound id is item mod data, and a client writes
its own inventory slots to the server (`MessageBuffer.GetData` case 5 forces the sender's index), so
"holds one bound to T" and "holds one" are forgeable at identical cost. Requiring the binding would
have meant syncing it on every bind, a one-re-bind-per-session migration, and an unverified
assumption — for no security at all.

**The residual, stated plainly.** Both arms are client-assertable. So is position: a Terraria client
is authoritative over its own, which is the same calibration [23](23-agent-audit-2026-08-25.md)
applies to inventory. **A modified client can still reach any Terminal-connected network.** This
change does not claim otherwise.

What it does remove:

- **Disks addressable through no Terminal at all.** A Drive Bay with no Terminal within
  `StorageNetwork.SearchRadius` is now unreachable by any packet; before, it was drainable by GUID.
- **Naming disks with no Terminal named at all** — the sniff-a-GUID-and-withdraw sequence.
- **Invisibility.** Asserting a position broadcasts it to every client, so drain-from-anywhere
  becomes a visible teleport into someone's base. Griefing others can see is a different problem
  from griefing they cannot.

A server-issued HMAC bind token would close the item arm. It would not close the position arm, so on
its own it buys nothing. **Recorded, not built.**

### `HandleRequestDiskData` needed a different rule

It cannot be proximity-scoped at all: `DriveBayEntity.NetReceive:355-360` sends one per bay the
client is told about, and a bay need not be near a Terminal — gating it blanks distant bays' status
lights, which [26](26-forged-disk-packets.md)'s session list calls out.

It now names **one block**, and the server answers with what that block holds: a `DriveBayEntity`
with its own disks, a `TerminalEntity` with its network's. Distant bay lights are unaffected — a
bay's disks are by construction in that bay. Dumping the contents of a disk sitting in a **chest**,
a piggy bank, on the ground, or in an **offline** player's inventory is now refused, because no block
names it. That rule holds by construction rather than by a scan, and `GetInsertedDiskIds` registers
missing disks as a side effect, which is what the old world-wide sweep existed to do.

### Two more holes on the same packets, same root cause

`CraftRequest` and `UpgradeDiskRequest` also carried `stations` and `conditions`. A client could name
any crafting station in the game and spend the network's materials on a recipe it had no station for.
Both now come from `StorageNetwork.GetAllStationsAndConditions` around the named Terminal.
**Found while making this change; the rule is the same one, applied at every site that encodes it.**

---

## 2. The two bay handlers had no sender check — HIGH

`HandleSyncDiskRemove` had 23g's bounds check and nothing else: a forged packet cleared any bay slot
in the world, destroying the disk item while its `DiskData` survived orphaned.
`HandleUpgradeDiskRequest` did not even take `whoAmI` — the dispatcher called it
`HandleUpgradeDiskRequest(mod, reader)`.

Doc 23 recorded the blocker: *"whether the Drive Bay UI stays usable at a distance decides its shape,
and guessing wrong breaks disk removal."*

**It does not stay usable.** `DriveBayUISystem.UpdateUI:126-143` closes the panel beyond 15 tiles
unconditionally — there is no `_remoteOpen` escape, unlike the Terminal's. Its packets have no other
senders. So requiring the sender to be at the bay costs a legitimate player nothing, and both
`HandleSyncDiskInsert` and `HandleSyncDiskRemove` now require it.

`HandleUpgradeDiskRequest` is **not** a bay-proximity case: it is issued from the Terminal's Disks
tab, which is legitimately remote. It takes `whoAmI`, names the Terminal, and goes through the same
three steps as every other storage packet.

That also fixed a pre-existing divergence nobody had noticed: the server paid for the upgrade out of
the network around **the bay**, while the panel's affordability check counted the network around
**the Terminal**. Two different sets — a green Upgrade button that spent disks the player was never
shown. Both are now the Terminal's network.

---

## 3. The forged-disk gate — narrowed, not closed — HIGH

[26](26-forged-disk-packets.md) left this open: a forged disk carrying a GUID whose physical disk sits
in a chest or an offline player's inventory passes `SenderMayClaimDisk`, because `IsDiskGuidInUse`
cannot see either, and lands in the attacker's bay — after which `GetInsertedDiskIds` puts the
victim's disk in the attacker's own Terminal network.

Doc 23 recorded why both obvious fixes are unsafe: widening `IsDiskGuidInUse` cannot reach an offline
inventory at all, and refusing any GUID the world already knows unless the sender holds it breaks the
ordinary bay-to-bay move, because this mod never calls `NetMessage.SendData` for equipment so the
server's view of a cursor or inventory slot is stale in both directions.

**Both of those still hold, and neither was attempted.** What changed is the reach: the insert must
now come from a sender standing at the bay, so the attacker must be physically at a Drive Bay rather
than anywhere in the world. That narrows it; it does not close it, because the attacker can place
their own bay. **Left open deliberately, for the reason doc 23 gave.** Closing it needs a live
two-client session to establish what the server's inventory view actually contains at the moment a
bay-to-bay move lands — see "Needs a two-client session" below.

---

## 4. Registry growth — the empty half is closed, the other half is not — MEDIUM

A forged insert registered a disk; a forged remove freed the slot and orphaned the entry; repeat.
`_allDiskData` grew without bound and was persisted to the world save.

The cost is larger than a big dictionary: `BeginModificationTracking` snapshots **every** entry on
**every** deposit, withdrawal, craft and quick-stack, so registry size is a per-operation server cost
multiplier.

**Fixed for the empty case.** When a disk leaves a bay on the server, its entry is dropped if it holds
no items and no other bay holds that GUID (`PruneEmptyDiskData`). Both arms are the safety argument,
and the tempting weaker rule — "no disk in the world carries this id" — is exactly the one that
cannot be answered: a disk in a chest, a bank, on the ground or in an offline inventory is invisible
to the server, so that rule deletes their storage. An empty entry loses nothing: `DiskData` is a
GUID, a tier and its items, and the tier is re-read off the disk item on the next insert.

**Not fixed for the with-items case.** A forged disk carrying items re-mints under a fresh GUID
(doc 26's design) and the resulting entry is non-empty, so the prune must keep it. That loop still
grows the registry. Recorded rather than closed.

**The real remedy for the multiplier is a different change** and is not in this pass:
`BeginModificationTracking` taking the operation's disk ids would turn O(all disks in the world) into
O(disks in this network) permanently, whatever the registry holds. It touches the delta path
[23f](23-agent-audit-2026-08-25.md) shows is fragile, so it is recorded as the recommended follow-up.

No `SaveWorldData` filter was added. `BackupSystem.cs:68` is a second write site over the same set and
`RestoreFromTag` does not apply `LoadWorldData`'s empty-purge, so filtering one would make the two
disagree. The prune removes at source instead.

---

## 5. `HandleDepositItemAtPosition` returning the wire item — NOT A DEFECT

Re-assessed as doc 23 asked. Its four paths return the item `ItemIO.Receive` built from the wire.
Under this file's own calibration a modified client is already authoritative over its own inventory,
so handing a client an item it declared is not an escalation over `QuickSpawnItem` on its own machine.
Three further reasons it stays:

1. The deposit *is* the client asserting it holds the item. Refusing to hand it back on a failure
   path is the destructive direction — doc 26 §1's *"a refusal that stayed silent would delete the
   disk"*.
2. `RefuseInsert` already restricts return-to-sender to Storage Disks for the one case where handing
   back arbitrary items would matter: a bay insert, which is otherwise a no-op and would become a
   faucet.
3. Nothing on those paths touches shared state — all four returns precede
   `BeginModificationTracking`.

The only available tightening, the `HandleQuickStackToStorage` model of depositing the server's copy
of the sender's slot, does not apply: `SendDepositItemAtPosition` carries no slot index, and its
caller sources items from a vacuum bag's own array rather than `player.inventory`. **Agreed with doc
23's reading; left as it is.**

---

## 6. One range rule, one origin

Found while wiring the proximity checks, and worth its own section because it is
[20](20-depth-origin-off-by-one.md)'s defect in a new place.

| | measured from |
|---|---|
| Terminal UI close (`TerminalUISystem:95-97`) | the entity's stored `Position`, in tiles |
| Drive Bay UI close (`DriveBayUISystem:136-138`) | same |
| the three server range checks | the multi-tile **centre** (`Position * 16 + 24`), in pixels |

The centre is 1.5 tiles down-right of the stored position, so a player up-and-left of a Terminal at
15.0 tiles by the panel's reckoning is ~17.1 by the server's: **panel open, every packet refused.**
It was invisible because only position-deposit and quick-stack used the centre and neither runs with
a panel open — and this change would have moved that origin onto every UI-driven handler.

`Common/TerminalReach.cs` is now the only encoding, and it is the UI's origin, because that is the one
a player can see. Both position-deposit and quick-stack become up to ~2 tiles *more* permissive on two
sides, which is the safe direction. `TR-08` sweeps the boundary against the panel's own formula and
fails at 213 points if the centre offset comes back.

---

## 7. The refusal vocabulary is finished

[25](25-craft-costed-against-a-count-it-cannot-withdraw.md) recorded ~19 refusal points across four
disk-management handlers that answered the client nothing. Re-counted at `e7f2273`: **20**
(`HandleUpgradeDiskRequest` 7, `HandleRestoreDiskRequest` 4, `HandleDefragRequest` 5,
`HandleArchiveDiskRequest` 4). All of them now answer, and so do the three refusals in
`HandleSyncDiskInsert` and the new one in `HandleSyncDiskRemove`.

Nine members appended — **never renumbered**, because the byte is the wire format a peer maps back to
a localization key. `DN-14n`..`DN-14v` pin each value, `DN-14l` the count, `DN-03` sweeps all 256
bytes.

| member | byte | condition |
|---|---|---|
| `NotAtDriveBay` | 12 | a bay packet from beyond 15 tiles |
| `DiskNotInSlot` | 13 | the named slot — bay or inventory — does not hold the named disk |
| `UpgradeUnavailable` | 14 | no upgrade option, or the index is out of range |
| `MaterialsNoLongerAvailable` | 15 | `TryConsumeMaterials` found a shortfall |
| `DiskNotFound` | 16 | the named Drive Bay does not exist |
| `DiskRecoveryRefused` | 17 | recovery failed its holder / lost-disk / fresh-GUID gate |
| `NothingToDefragment` | 18 | the defrag moved nothing |
| `DiskClaimRefused` | 19 | `SenderMayClaimDisk` said no |
| `DriveBaySlotUnavailable` | 20 | the slot filled before the packet arrived |

The storage handlers needed **no** new members: naming the Terminal made their three refusals the
three that already existed.

**Malformed wire input stays silent, deliberately.** A count that cannot describe a real list is a
forgery, not a request; naming a cause for it would invent a line no honest client can reach. Every
*other* return names one.

**`RefuseInsert` now carries the cause** alongside the disk return and the bay correction, so a future
refusal cannot pick up two of the three.

### The correlation id — assessed, not built

Doc 25 accepted the risk that a denial carries no correlation id, so two operations denied in the same
tick can attribute a reason to the wrong click. Re-assessed now that something is actually displayed:
**still accepted.** `StorageOperationFailureThrottle` already collapses a burst of one cause, and this
change makes the causes far more specific — a `NotAtDriveBay` cannot be mistaken for a
`NoRoomInInventory`. What survives is "two operations, two causes, one tick", where both lines print
and both are true; the reader sees two accurate messages in an ambiguous order. That is not worth a
request id on six packets plus a client-side pending table. **The judgement is recorded either way.**

---

## What a legitimate player notices

1. A disk in a Drive Bay with no Terminal within 30 tiles is no longer reachable. No UI could address
   it before either.
2. Walking out of range in the tick between click and packet turns a silent success into a named
   refusal — and a deposit hands the item back rather than eating it.
3. Position-deposit and quick-stack become ~2 tiles more permissive on two sides (§6).
4. **Defragment works again through a Remote Terminal.**
5. A defrag that moves nothing now says so instead of doing nothing visible.
6. The Terminal's top-up disk-data request answers with the whole network rather than the missing
   subset — a few more packets on a rare trigger.
7. If the server's copy of the sender's inventory has not yet seen a freshly acquired Remote Terminal,
   a remote operation is refused until it syncs. Walking to the Terminal always works.

## Verified by

`TR-*` and `DA-*` in `Tests/Program.cs` — 25 assertions over `Common/TerminalReach.cs`,
`Common/DiskAccess.cs` and the handler wiring, plus nine new `DN-14*` byte pins. Suite 667 → 729,
zero failures.

Four were **mutation-checked** — the rule was broken, the assertion watched to go red, then restored:

| mutation | caught by |
|---|---|
| `MayOperateTerminal`'s `||` → `&&` | `DA-02` (the Remote Terminal regression) |
| `MayPruneDiskData`'s `&&` → `||` | `DA-07` (the disk-in-a-chest destruction case), `DA-10` |
| the 1.5-tile centre offset restored | `TR-08`, at 213 swept points |
| one handler's authorization removed; the deposit's item return removed | `DA-12`, `DA-16a` |

The handler wiring has no unit-test surface for the reasons [21](21-untested-fixes.md) sets out, so
`DA-12`..`DA-16a` read the source the way `DN-06`/`DN-07`/`DN-08` already do. The whole mod also
**compiles clean** — 0 errors, 0 warnings against `tModLoader.dll` via a throwaway project.

**`Systems/AndroLibCompat.cs` compiles too, for the first time.** It was excluded from the recorded
type-check recipe because of its `androLib` weak reference; it needs exactly two types
(`androLib.StorageManager` and `androLib.UI.BagUI`), and a stub declaring the four members it touches
brings the whole mod under the compiler. That matters here because this change alters a signature that
file calls.

## Needs a two-client session

- A `WithdrawItem` naming a Terminal the sender is nowhere near: refused, nothing leaves storage.
- The same through a **Remote Terminal**: succeeds. Then withdraw, deposit, craft, **defragment** and
  upgrade, all remotely — defragment is the one that was broken before this change.
- A deposit refused for range: the item comes back, and comes back with its mod data intact.
- With the inventory **full** when that refusal lands: the item goes to the cursor, not the floor.
- Drive Bay insert and remove from 14 tiles (works) and from 16 (refused, disk returned, bay
  corrected, and exactly **one** item with that GUID exists afterwards).
- Drive Bay status lights on a bay far from any Terminal still update — this is the one the
  `RequestDiskData` rule was shaped around.
- Remove an **empty** disk from a bay, then re-insert it: it still works and reports its tier.
- Remove a **full** disk from a bay, reload the world: its contents are intact. This is the prune's
  destructive direction.
- An upgrade whose materials are spread across the Terminal's network but not the bay's: the panel
  and the server now agree.
- Two clients race for the same bay slot: the loser gets the disk back, their bay is corrected, and
  they are told **why**.
- Ordinary singleplayer: every path above, unchanged.

## Related

[23](23-agent-audit-2026-08-25.md) (the five open holes, now dispositioned),
[26](26-forged-disk-packets.md) (the previous pass; §3 here supersedes its "What it does NOT cover"),
[25](25-craft-costed-against-a-count-it-cannot-withdraw.md) (the silent refusals, now closed),
[20](20-depth-origin-off-by-one.md) (why §6 is its defect in a new place, and why §3 was not guessed at),
[21](21-untested-fixes.md) (what has no unit-test surface).
