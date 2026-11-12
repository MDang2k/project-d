# Economy and loot balance

Date: 2026-11-12

Status: approved design, not implemented. Answers GDD §21 "Economy: prices, sell rates, insurance cost" with targets every price and loot table is tuned against. Decisions from the brainstorm on 2026-11-12. Numbers marked **(tune)**. Choices not stated by the designer are marked **(Claude's call)**.

## Intent

- Dying stings but never wipes the player out: one death costs about one to two good raids (designer decision).
- Risk pays: inner maps are worth far more than outer ones (designer decision).
- Money flows through the six traders only; knowing which trader pays best for what is a small skill (designer decision).
- One price scale, one source: the catalog (designer decision), and a report tool measures the real data against the targets.

### Non-goals

- A player market or flea market (designer decision; the modding spec's black-market board stays a later small extra for rare parts).
- Dynamic prices driven by supply and demand.

## 0. Current state

| Exists | Where | Problem |
|---|---|---|
| Catalog base values (AKM 32,000, FAL 42,000, flak vest T1 12,000, steel helmet T1 6,000, bandage 600, morphine 3,500...) | catalog CSVs (designer's xlsx) | the anchor |
| Game item values about 1/50 of the catalog (ak_type 600, fal_type 700, flak_vest_t1 250, light_rig 80, bandage 40, a round 2) | `data/items/defs/*.tres`, hand-set | **a second price scale**; nothing syncs them from the catalog |
| Quest money (800-2,500), banknote stacks (60-200), starter kits, paid extracts | `data/quests`, `data/loot`, `data/player`, map data | tuned to the small game scale |
| Ammunition and magazines | no Base value column on the Ammo sheet; `ak_mag`, `fal_mag` and other magazines have no catalog row | values exist only in the game defs |
| Traders: four levels each. Quartermasters buy x1.25 / x1.2 / x1.15 / x1.1, sell 0.45 / 0.5 / 0.55 / 0.6; insurance premium 12 / 10 / 8 / 6% with return chance 35 / 50 / 65 / 80%. Other traders have their own sell fractions (Smuggler 0.3-0.4, Engineer up to 0.65) | `data/traders/*.tres` | kept as they are (ratios do not depend on the scale) |
| Catalog ids differ from game ids (`akm` vs `ak_type`, `walther_p1` vs `pistol`); the catalog notes say "Exists as 'x'" | catalog, `tools/export_actors.py` already maps them | reused |

## 1. One price scale: the catalog (designer decision 2026-11-12)

- The catalog's base values are the game's prices. The game defs' `BaseValue` is **generated from the catalog**, never hand-set: a generator (`tools/export_values.py` or a step of the existing export, run by the pre-commit hook like `export_actors.py`) writes `BaseValue` into each `data/items/defs/<id>.tres` that has a catalog row, mapping ids through the "Exists as" notes as the actor export does **(Claude's call)**.
- **Missing catalog values** (ammunition, magazines and any game item without a catalog row): the designer adds them to the xlsx (Ammo sheet gets a `Base value` column per round; magazines get rows in Weapon Parts or their own sheet). Until then the generator leaves those defs untouched and the report lists them.
- **Everything priced in marks is retuned to the catalog scale** in the same pass: quest money rewards, banknote stacks in loot tables, starter money, paid extract costs, repair prices (malfunctions spec). Section 4 and 5 give the bands; the report tool checks them.
- Ratios (trader multipliers, sell fractions, insurance premiums) do not change with the scale.

## 2. The standard loadout (the unit of measure)

Everything is measured in **standard loadouts (SL)**: what a player needs for a normal middle-ring raid.

| Item | Catalog base value |
|---|---|
| AKM-type rifle (common rifle) | 32,000 |
| 3 spare magazines and 90 rounds | about 4,000 (estimate until the catalog has ammo and magazine values) |
| Flak vest T1 | 12,000 |
| Steel helmet T1 | 6,000 |
| Light rig | 4,000 |
| Satchel | 5,000 |
| Meds: 2 bandages, tourniquet, splint, morphine | about 8,000 |
| **Total** | **about 71,000 base value** |

- Priced at trader level 2 (x1.2): **1 SL ≈ 85,000 marks**. The report tool recomputes the SL from the catalog whenever values change, so the targets stay consistent **(Claude's call)**.

## 3. What a raid should pay (designer decisions)

Net income of an **extracted** raid, in marks after selling the loot at trader level 2 rates (or the value kept if the player keeps it):

| Ring | Maps | Average extracted raid | Spread (tune) |
|---|---|---|---|
| Outer | Kreuzdorf, Border Strip | 0.4 SL (about 34,000) | 0.2-0.8 SL |
| Middle | Hochwald, Rail Yard, Bunker Gate 7 (map 1) | 1.0 SL (about 85,000) | 0.5-2 SL |
| Inner and core | Falkenberg base, The Labs, Cascade Site | 2.0+ SL (about 170,000+) | 1-4 SL; bosses more |

- **A death** loses the loadout minus the secure container and insurance returns: about 1 SL for a standard kit, so one to two good middle raids recover it.
- **Progression pace:** consistent tier 3 gear in about 10-15 hours of play **(tune)**.

### Where the money comes from (share of a raid's income, tune)

| Source | Share | How |
|---|---|---|
| Bodies, mostly PMC kits | about 50% | AI loadouts by tier (factions spec); a tier 2 PMC carries about 0.6 SL of gear |
| Containers and loose loot | about 33% | loot tables (section 6) |
| Quests | about 15% | money and item rewards (section 5) |
| Ewigstein | 0 marks | no money value (GDD); it pays reputation, story and secure containers |

## 4. Traders (designer decision: traders only, specialised)

- Levels, multipliers, sell fractions and insurance terms stay as in section 0.
- **Specialisation:** each trader pays **+10 percentage points** on its sell fraction for its specialty and sells its specialty at **-5%** **(tune)**. Specialties are **data on each trader** (`SpecialtyCategories` and `SpecialtyItemIds` in the trader resource), so no new item model fields are needed **(Claude's call)**:

| Trader | Specialty |
|---|---|
| Allied quartermaster | Allied weapons, ammo and armour (by item id list) |
| Soviet quartermaster | Soviet weapons, ammo and armour (by item id list) |
| Smuggler | valuables, relics, mixed East and West goods (by item id list) |
| Medic | medical items, food and drink (categories) |
| Engineer | weapon parts, tools, repair kits, electronics (categories and ids) |
| Scientist | knowledge items (already the only buyer), lab equipment |

- Documents (knowledge items) stay the Scientist's alone; electronics belong to the Engineer only.
- What each trader buys at all stays as today (`Buys` per level); the specialty only raises the rate. Reputation factors (M8b) apply on top.
- The trader screen shows the payout before selling, so the player learns where things sell best.

## 5. Money sinks and rewards (catalog scale)

| Sink | Target (tune) |
|---|---|
| Replacing gear after a death | the main sink, about 1 SL per death |
| Ammunition | a raid's ammo about 5% of an SL (about 4,000-5,000 marks); AP rounds about 3x ball |
| Insurance | premiums as today (12 / 10 / 8 / 6% of insured value by level): insuring a standard kit about 6,000-10,000 marks |
| Repairs (malfunctions spec) | an Engineer repair of a worn bolt about 2-5% of the weapon's value |
| Paid extracts | 5-15% of an SL by map (about 4,000-13,000 marks) |
| Trader levels | spending thresholds rescaled with the prices; level 2 with most traders in about 3-5 hours **(tune)** |

| Reward | Target (tune) |
|---|---|
| Early quests (first hours) | 0.15-0.3 SL each in money and items (about 13,000-26,000 marks) |
| Mid quests | 0.3-0.6 SL plus trader level or unlock rewards |
| Story missions | as their spec says (secure containers, access), money modest |

## 6. Loot tables (Claude's call: derived from the targets)

| Table | Expected base value per search | Used for |
|---|---|---|
| low_tier | 3,000-6,000 | loose shelves, trash, outer-ring containers |
| mid_tier | 10,000-20,000 | lockers, medical cabinets, middle-ring crates |
| high_tier | 30,000-60,000 | weapons crates, safes, inner-ring rooms |
| boss and keyed loot | 80,000+ | boss loot tables, locked loot spots (8f) |

- **Per-map container counts** are set so the container share (about 33%) of each ring's target is reachable by a player who searches about half the map **(tune)**.
- **Banknote stacks** by tier: low 200-800, mid 1,000-4,000, high 5,000-15,000 marks.
- **Body loot (`body_scav`)** keeps pocket junk; the real value of a body is its worn gear (loadout tables).

## 7. Starter kit and new game

- A new profile starts with its starter kit (about 1.5 SL at catalog values) plus **20,000 marks** in the stash **(tune)**, enough to replace a lost rifle once.
- Insurance on the starter kit is offered on the first visit to the quartermaster.

## 8. Economy report tool (Claude's call)

- `tools/economy_report.py` (read-only: it never edits the catalog or data) reads the catalog values, the game defs, trader `.tres` files, loot tables, faction loadout tables and map spawn markers, and prints:
  - the SL in base value and in marks at each trader level;
  - items whose game value differs from the catalog, and items with no catalog value (ammo, magazines);
  - expected base value per search for each loot table;
  - expected value of a body per faction and tier;
  - **for built maps only** (today map 1 and the Border Strip): expected income of an extracted raid (containers searched at 50%, an assumed 30% of the expected AI killed) against the ring target, flagged too low, on target or too high; maps not built are listed as missing, and any missing input (ring, AI count, container count) is flagged;
  - quest rewards as a share of an SL.
- Data retuning (quests, loot tables, starter money, paid extracts) is done by editing those `.tres` files after reading the report; catalog values only change through the designer's xlsx.
- The expected-value maths also exists in C# where the game needs it (`LootTable` expected value), with tests.

## 9. Build order (Claude's call)

1. Value generator from the catalog into the item defs; report tool.
2. Retune quests, banknotes, starter money, paid extracts, trader level thresholds to the catalog scale, checked with the report.
3. Trader specialties (data and pricing code, trader screen).
4. Loot table and per-map container tuning as maps are built.

## 10. Tests and playtest

- xUnit: expected value of a loot table against a hand-computed case; specialty pricing (+10 points sell, -5% buy) with reputation on top.
- Report: every built map within its ring band; every quest reward within its band; no item price left on the old scale.
- Playtest: start a new profile and play 5 middle-ring raids, writing down income per raid, deaths and money after each; compare with the targets (about 1 SL per extracted raid; one death recovered in one or two raids).

## Decisions

1. A death costs about one to two good raids; consistent tier 3 gear in about 10-15 hours.
2. Value grows with risk: outer 0.4 SL, middle 1 SL, inner 2+ SL per extracted raid; bodies about half the income, containers a third, quests the rest; Ewigstein pays no marks.
3. Traders only, no player market; each trader pays more for its specialty.
4. The catalog's values are the game's prices: item values are generated from the catalog, and everything priced in marks is retuned to that scale.
5. The standard loadout (about 85,000 marks at level 2) is the unit every target is measured in; a read-only report tool checks the data against the targets.

## Open items

- Catalog: `Base value` for ammunition (per round) and magazines (designer, xlsx).
- Per-map container counts as each map is built.
- GDD §21 "Economy" question resolved by this spec.
