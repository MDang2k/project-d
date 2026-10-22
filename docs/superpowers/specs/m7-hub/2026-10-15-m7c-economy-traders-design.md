# M7c: Economy and traders

Status: approved design, not implemented. Third of five M7 sub-projects (7a persistence done; 7b hub shell and stash, spec approved; 7c this; 7d insurance; 7e quests). Implements GDD §13 (traders), §15 (extraction payment), §10 (stash).

## Intent

Money, six traders and buy, sell and barter in the hub. Money is an item, so the GDD's risk rules (death loses carried gear, secure container is safe, extracts can charge payment) apply with no new rule. Every trader stays at level 1 in 7c; level-ups arrive with quests (7e).

## In scope

- `banknotes` item, `ItemData.BaseValue`, a currency text key.
- `TraderData` and `TraderLevelData` resources and the six slice traders (level 1 stock; levels 2-4 authored but unreachable).
- Pure `TradeEngine`: buy, sell, barter, pricing.
- `ExtractKind.RequiresPayment` with a cost.
- Hub `TraderScreen`.
- Save v3 (per-trader `level` and `spent`), migration `2 -> 3`.
- Banknote entries in loot tables.

## Out of scope

Insurance (7d), quests and level-ups (7e), reputation sources (M8), stock limits and restocking, the opposite quartermaster's unlock, Ewigstein and document sales, attachments and gunsmithing, changing map 1's extract definitions.

## 1. Money and prices

- **Banknotes:** item id `banknotes`, category Misc, 1x1, negligible weight, very large `MaxStack` **(tune)**, `BaseValue` 1. Lives in the stash, carried grids and the secure container; lootable and lost on death like any item.
- **Starting money:** the new-profile stash includes a banknotes stack **(tune)**. The starter kit resource gains an entry for it.
- **`BaseValue`:** new int on `ItemData` (and `ItemSpec`). Zero means "not sellable".
- **Currency name:** text key `currency.unit`, working name "marks" (GDD naming TBD).
- **Buy price:** `ceil(BaseValue * BuyMultiplier)`.
- **Sell payout:** `floor(BaseValue * SellFraction * condition)`.
  - Armor and helmets: `condition` is current durability over maximum; everything else is 1.
  - A magazine adds `rounds * round value` (the loaded ammo's `BaseValue`).
  - A weapon sells together with its inserted magazine and chambered round.
  - An item with non-empty nested grids (rig, pack, secure container with contents) cannot be sold until emptied.

## 2. Traders (data-driven)

- **Resources:** `TraderData` (`Id`, `NameKey`, `Unlock`, `Levels[4]`), `TraderLevelData` (`Sells[]` of item id and optional price multiplier, `Buys[]` item categories, `BuyMultiplier`, `SellFraction`, `Barters[]`), `BarterOffer` (`Give[]` item id and count, `Get` item id and count). Files in `data/traders/`.
- **Levels:** four entries per trader; the save stores `level` (default 1) and `spent` (default 0). Only level 1 is reachable in 7c.
- **Unlock rule:** `Always`, `ForSide` (your side's quartermaster), `Locked` (opposite quartermaster until 7e or M8).
- **Stock:** unlimited; scarcity is price and barter-only rare items.

| Trader | Availability | Sells (level 1) | Buys | Barters (examples **(tune)**) |
|---|---|---|---|---|
| Allied quartermaster | Allied side | FAL-type, pistol, FAL mags, .51 and 9x19 ammo, flak vests and helmets T1-T2, rigs, packs, food | Weapons, ammo, armor, rigs, packs, food | none |
| Soviet quartermaster | Soviet side | AK-type, pistol, AK mags, 7.62x39 and 9x19 ammo, flak vests and helmets T1-T2, rigs, packs, food | same categories | none |
| Smuggler | Always | .280 and 7.92x57 ammo, EM-2 mags, MG belts, secure pouch upgrades if they exist | Weapons, armor, ammo (low fraction) | EM-2, MG-type, prototype vest T4 for stacks of existing gear |
| Medic | Always | All medical items, food, water | Medical, food, drink | none |
| Engineer | Always | All magazines, all ammo, melee weapons | Magazines, ammo, melee | rare-weapon barters from existing items |
| Scientist | Always (no business yet) | nothing | nothing | none; UI shows a "nothing for you yet" line |

## 3. Trade engine (pure)

`TradeEngine` works on a `GameProfile`-style stash grid plus the character inventory and a `TraderLevelData`; item specs come from a lookup.

- `Quote(item or listing)` returns price or payout.
- `Buy(listing, count)`: pays banknotes (stash first, then carried grids), puts the item in the stash. All-or-nothing.
- `Sell(item)`: removes the item from the stash or carried grids, pays banknotes into the stash.
- `Barter(offer)`: removes the Give items, adds the Get item to the stash. All-or-nothing.
- Banknote payouts split into stacks of `MaxStack`.
- Each completed trade adds the money value traded to that trader's `spent`.
- Failure reasons: `NotEnoughMoney`, `StashFull`, `NotBuyableHere`, `NotSellableHere`, `HasContents`, `MissingBarterItems`.

## 4. Extract payment

- `ExtractKind.RequiresPayment`; `ExtractionSpec` gains `Cost`; `ExtractionZone` gets a `Cost` export.
- While the player stands in the zone without enough banknotes in carried **storage grids** (pockets, rig, vest, backpack; not the secure container), the session reports `ExtractBlock.CannotPay`.
- On a successful extraction the cost is removed from carried banknotes before the outcome rules run.
- Map 1's extracts are unchanged; making one paid is a one-line data edit for a map spec.

## 5. Hub UI and save v3

- **`TraderScreen`** (built in code, opened from the hub): a tab per trader; Buy, Sell and Barter lists with a row per entry (name, price or payout, button); banknote total. Unavailable traders show their lock reason. No drag and drop.
- **Save v3:** `traders` map of id to `{ level, spent }`. Money needs no field. Migration `2 -> 3` adds an empty map. `SaveFile.CurrentVersion = 3`; the frozen v1 and v2 fixtures must still load.
- **Loot tables:** banknotes entry per tier in `data/loot/*.tres`.

## Units

| Unit | Kind | Responsibility |
|---|---|---|
| `TradeEngine`, `PriceModel` | pure | quotes, buy, sell, barter, results |
| `MoneyLedger` | pure | count and remove banknotes across grids |
| `TraderData`, `TraderLevelData`, `BarterOffer` | data resources | trader catalogs |
| `TraderState` in `GameProfile` | pure | per-trader level and spent |
| `RaidSession` + `RaidManager` | existing, extended | paid extracts |
| `TraderScreen` | UI node | trading UI |

Tests: price rounding, sell with condition and magazines, contents refusal, buy and sell round trip, barter all-or-nothing, stash full, money across stash and carried, stack splitting, paid extract blocked and paid, save v3 migration and round trip.

## Decisions

1. Bought items go to the stash.
2. Money in the secure container cannot pay for extracts.
3. Containers with contents cannot be sold.
4. No per-item buy limits.
5. Unlimited stock; level-ups only through quests (7e).

## Open items (none blocking)

- Starting money, prices, `BuyMultiplier` and `SellFraction` per trader, and the barter offers are placeholder tuning for the first playtest.
- Currency name is a working name.
