# Factions and bosses

Status: approved design, not implemented. Visual identity, equipment and behaviour for every faction, all bosses, and the roaming boss squad Die Troika. Data lives in the catalog: sheets **Faction Loadouts** and **Bosses** in `docs/design/item_catalog.xlsx` (CSV: `docs/design/catalog/faction_loadouts.csv`, `bosses.csv`). Builds on GDD §16, the 8f bosses spec, the story bible (final names) and the asset pipeline spec. Numbers marked **(tune)**.

## 1. Reading factions without a HUD

Every faction is identifiable at about 50 m by silhouette, colour and one clear marker. The two PMC sides are commission contractors and wear armbands.

| Faction | Silhouette and colours | Clear marker | Behaviour |
|---|---|---|---|
| Looters | hunched, ragged civilian work jackets, flat caps, scarves, sacks; mismatched browns and greys | no uniform, duffel sack | flee when hurt |
| Bandits | leather coats, black and dark brown, bandanas, sunglasses, surplus pieces | leather and bandana, no armband | aggressive in groups, hold camps |
| Allied PMC | olive and khaki NATO kit, M1956 webbing, M1 helmet with cover, radio packs | **pale blue armband** | disciplined patrols in pairs |
| Soviet PMC | olive-brown Warsaw Pact kit, chest rigs, SSh-68 helmets | **red armband** | disciplined, flank in pairs |
| Garrison holdouts | faded patched 1965 Allied uniforms, beards, old M1 helmets, gas masks at the hip | tattered unit patches | defensive, hold buildings |
| Abteilung Ewig | dark grey long field coats, round-eyed gas masks, rounded dark composite helmets, tight 3-4 squads | pale frost-crystal-in-a-ring insignia (fictional; no real Nazi symbols) | elite; hunt Ewigstein carriers (story part 2) |
| The Hollowed (post-slice) | frost-rimed civilians and soldiers, grey skin, slow jerky movement | rime and stillness | sound-driven, swarm |
| Die Wächter (late) | tall, smooth, non-human geometry (art pass) | shape language unlike anything else | hostile to all humans |

## 2. Gear tiers

- Each human faction has loadout tables per tier (sheet **Faction Loadouts**: faction, tier, slot, item id, weight, ammo id).
- Tier by map ring: outer = 1, middle = 2, inner and core = 3. Act 2 and Act 3 each raise every faction one tier (cap 3). Abteilung Ewig starts at tier 2.
- Each AI rolls one item per slot by weight; empty results mean the slot stays empty (looters often have no armor).
- Weapons spawn with their default build (weapon modding v2); tier 2+ PMCs add optics from their table.

## 3. Bosses

One boss per map (8f) plus the roaming Troika (section 4). Full rows in sheet **Bosses**.

| Map | Boss | Signature | Key item | Notes |
|---|---|---|---|---|
| Bunker Gate 7 | Kurt "Eisenhand" Vogt (bandit chief) + 3 | **Vogts Säge** (cut-down MG42), prototype vest T4 | officer's strongbox key | suppresses while escorts flank |
| Kreuzdorf | "Old Mattes" (looter leader) + 2 | **AO-12 Kartech** (Saiga-inspired Soviet prototype auto shotgun) | pharmacy key or vault code note | roams the Landklinik; flees when badly hurt |
| Hochwald | Die Jägerin (ex-forester sniper) + 2 spotters | **Gewehr 43 with ZF 4**, MP40 | station key | moves perches after every shot |
| Border Strip | Major Dale Harker (deserter) + 3 | **XM177E2** with starlight scope | radio room key | ambushes from trench and convoy |
| Rail Yard | Bogdan "der Kohlenkönig" Král (warlord) + 3 | **gold-inlaid Stechkin APS**, PPSh drum | strongroom key | whistles escorts in |
| Falkenberg base | Colonel Walter Haig (garrison commander) + 3 | **M14E2** with ranging telescope | armory key, officer's pass | layered HQ defence; never leaves |
| The Labs | Sicherheitsleiter Ernst Kolb (lab security) + 3 | **StG 44 with ZG 1229 Vampir** IR scope | red lab keycard | locks doors while falling back |
| Cascade Site | Oberst Viktor Rauch (Abteilung Ewig) + 3 | Gerät 69, Ewigstein-composite vest | (service tunnel code in his orders) | hunts carriers; races you to the keystone |

- Every boss drops its signature gear, key, guaranteed loot and a roll from its boss loot table (8f).
- Each boss has a distinct look recognisable before any name appears (GDD pillar: information is earned).

### Health pools

- Every human starts from the player body (GDD §9): head 35, thorax 85, stomach 70, arms 60, legs 65 (440 total).
- Each boss has an **HP multiplier** **(tune)** applied to every body part, head included, rounded to whole HP. Faction grunts and escorts stay at 1.0. This replaces 8f's flat +25% body HP.
- Scaling the head is deliberate: tanky bosses (Bär, Rauch, Král) can survive one penetrating headshot from weaker rounds.
- Columns in sheet **Bosses**: `HP multiplier`, `Head HP`, `Thorax HP`, `Stomach HP`, `Arm HP (each)`, `Leg HP (each)`, `Total HP` (per-part columns are formulas off the multiplier).

| Boss | Multiplier | Head | Thorax | Stomach | Arm | Leg | Total |
|---|---|---|---|---|---|---|---|
| Die Jägerin, Fuchs | 1.1 | 39 | 94 | 77 | 66 | 72 | 486 |
| Old Mattes | 1.15 | 40 | 98 | 81 | 69 | 75 | 507 |
| Haig, Kolb | 1.25 | 44 | 106 | 88 | 75 | 81 | 550 |
| Harker, Wolf | 1.3 | 46 | 111 | 91 | 78 | 85 | 574 |
| Vogt | 1.35 | 47 | 115 | 95 | 81 | 88 | 595 |
| Král | 1.4 | 49 | 119 | 98 | 84 | 91 | 616 |
| Rauch | 1.5 | 53 | 128 | 105 | 90 | 98 | 662 |
| Bär | 1.6 | 56 | 136 | 112 | 96 | 104 | 704 |

## 4. Die Troika (roaming boss squad)

- Three ex-special-forces freelancers, one deserter from each side and one who will not say; hostile to everyone; they deal with the Smuggler.

| Member | Role | Look | Gear |
|---|---|---|---|
| Wolf | leader, tactics | long coat, binoculars | TKB-022PM bullpup, ceramic chest plate |
| Bär | heavy, suppression | huge, titanium vest and helmet | PK |
| Fuchs | scout, flanker | light kit, scarf mask, Gen 0 NVG at night | suppressed Skorpion vz. 61, AO-46 |

- **Spawning:** independent roll of about 15% **(tune)** per raid on each of Kreuzdorf, Hochwald, Border Strip, Rail Yard and Falkenberg base.
- They can appear in the same raid as the map's own boss: the only exception to 8f's one-boss-per-raid rule.
- They move as a squad through every point of interest on the map (Tarkov "Goons" style).

## 5. New weapons and parts (catalog)

- Weapons: `ao_12_kartech`, `gewehr_43`, `vogts_saege`, `xm177e2`, `stechkin_aps_gold`, `m14e2`.
- Parts: `zg1229_vampir` (IR scope with battery pack, M9 night use), `skorpion_suppressor`.
- Fixed catalog id: `ger_t_69` is now `geraet_69`.

## 6. System needs

- AI presets per faction and tier rolling from the loadout sheet.
- Boss data (8f `BossData`) filled from the Bosses sheet, plus an `HpMultiplier` field scaling the `HealthConfig` body HP.
- Troika: a roaming multi-member boss with per-map spawn chance that ignores the one-boss limit.
- Armband and insignia materials on the shared palette (asset pipeline).

## Decisions

1. Armbands identify the PMC sides (pale blue Allied, red Soviet); bandits drop the red armband.
2. Gear tier by map ring plus one per act.
3. Every boss carries a unique or rare signature weapon.
4. Die Troika roam five maps with an independent 15% roll each and may coexist with a map boss.
5. Boss health is a per-boss multiplier on the player body, head included; grunts and escorts use the base body.

## Open items

- Trader and story character visuals (part 3 of this content pass) come next.
- Loot table contents per boss (`boss_*` tables) are tuning.
