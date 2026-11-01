# M7d: Insurance

Date: 2026-10-15

Status: approved design, not implemented. Fourth of five M7 sub-projects (7a persistence done; 7b hub shell and stash, spec approved; 7c economy and traders, spec approved; 7d this; 7e quests). Implements GDD §15 (Insurance) and §13 (insurance bought from quartermasters).

## Intent

Pay a quartermaster before a raid so lost gear has a chance to come back. Insurance is per raid: the flag exists only from purchase in the hub until the end of the next raid. On death or MIA each insured item rolls to return; returns arrive in the stash after the following raid.

## In scope

- `Insured` and `InsuredWith` on items; `ItemData.Insurable`.
- Insurance catalog block on quartermaster trader data (premium fraction and return chance per level).
- Pure `InsuranceService` (buy, raid-end roll, delivery queue).
- Save v4: item flags and the pending-returns queue; migration `3 -> 4`.
- Insurance tab in the trader screen, an "insured" badge in the hub loadout, result-screen line.
- Roll in both the normal death/MIA path and the crash-recovery path.

## Out of scope

Nemesis (M8; this spec only exposes the returned and not-returned split), quest items (7e), level-ups (7e; only level 1 is reachable), a return timer, partial or repair services, insuring stash items.

## 1. Data and save v4

- `ItemInstance` gains `Insured` (bool) and `InsuredWith` (trader id, null when not insured); both saved.
- `ItemData.Insurable` (default true). Set false for banknotes, ammo, medical, food, drink and the secure container.
- `TraderLevelData` gains an optional `Insurance` block: `PremiumFraction` (float) and `ReturnChance` (0..1), per level. Only the Allied and Soviet quartermasters define it. All four levels are authored **(tune)**; only level 1 is reachable until 7e.
- Save v4: item records add `insured` and `insuredWith`; the file adds `insuranceReturns[]` of `{ item, raidsRemaining }`. Migration `3 -> 4` adds an empty list. `SaveFile.CurrentVersion = 4`; frozen v1, v2 and v3 fixtures must still load.

## 2. Buying

- Only your side's quartermaster sells insurance (the opposite quartermaster is locked).
- Insurable items are carried items: equipped slots and pockets, vest, rig and backpack grids. Stash and secure container items are not listed.
- Premium: `ceil(BaseValue * PremiumFraction)`, paid in banknotes through the 7c money ledger; it adds to that trader's `spent`.
- Contents are separate items and are insured (and priced) separately. A weapon's inserted magazine is its own item.
- Failures: `NotInsurable`, `AlreadyInsured`, `NotEnoughMoney`, `NotCarried`, `TraderLocked`.

## 3. Raid end (pure `InsuranceService`, RNG injected)

Order at the end of any raid:

1. Decrement `raidsRemaining` on queue entries created in earlier raids; entries reaching 0 become deliveries (section 4).
2. Extracted: nothing is lost.
3. Killed or MIA (also the crash-recovery path): walk the lost items, including items nested inside lost containers. Each item with `Insured` rolls `ReturnChance` of the level of `InsuredWith`. A success queues a copy with `raidsRemaining = 1`. The copy is stripped of contents, inserted magazine, chambered round and loaded rounds; durability is kept; it is uninsured.
4. Clear `Insured` and `InsuredWith` on every item everywhere (character and stash). Unused insurance is wasted.

The roll returns `Returned` and `NotReturned` lists. M8's nemesis takes only `NotReturned`, so an item is never both returned and with a nemesis.

## 4. Delivery

- An entry created at the end of raid N is delivered on return to the hub from raid N+1, whatever its outcome (GDD: "after the next raid").
- Delivery goes to the stash. If the stash is full the entry stays queued and retries on the next hub entry or stash change. Nothing is dropped.

## 5. UI

- Trader screen: an Insurance tab on the quartermaster page with carried insurable items, their premium and an Insure button; already insured items show a badge.
- Hub loadout screen: an "insured" badge on insured items, and a header line with the number of returns waiting.
- Raid result screen: one line, "insured items returning after your next raid" with the names, or "no insured items returned".

## Units

| Unit | Kind | Responsibility |
|---|---|---|
| `InsuranceService` | pure | buy, roll, queue, deliver |
| `InsuranceQueue` | pure | persisted pending returns with `raidsRemaining` |
| `TraderLevelData.Insurance` | data | premium fraction and return chance per level |
| `ItemInstance.Insured`, `InsuredWith` | pure | per-item flag |
| Save v4 in `SaveSerializer` / `SaveMigrator` | pure | persistence |
| Insurance tab, badge, result line | UI | presentation |

Tests: buy (price, flags, spent, failure reasons), roll with a stub RNG at 0 and 1, nested insured item in a lost pack, stripped copy, all-flags-cleared after extraction and after death, queue decrement and delivery order, full stash keeps the entry, save v4 round trip and migration, crash-recovery path rolls.

## Decisions

1. Returns arrive on return from the raid after the loss.
2. A full stash makes a return wait.
3. Only carried items are insurable.
4. Nested insured items in a lost container still roll.
5. Returned weapons come back without magazines or rounds.
6. Premium and chance numbers are authored per level and tuned in playtest.

## Open items (none blocking)

- Insurance premium and return chance values are placeholder tuning.
- Whether an unused insurance flag should be refunded is not in the GDD; not included.
