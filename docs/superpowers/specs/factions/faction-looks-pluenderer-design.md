# Faction looks and the Plünderer merge

Date: 2026-11-21

Status: approved design, not implemented. Merges the looter and bandit factions into one faction, the **Plünderer**, led by elite **Bandenführer**, and specifies how every human faction and the Hollowed look, at the level of the player outfit table (`docs/superpowers/specs/factions/character-visuals-design.md` section 1). Decisions from the brainstorm on 2026-11-19 to 2026-11-21. Numbers marked **(tune)**. Choices not stated by the designer are marked **(Claude's call)**.

## Intent

- **Simpler factions (designer):** looters and bandits were two hostile scavenger factions that differed mostly in gear. They become one faction whose elites behave like the old bandits.
- **Read a bot before it shoots:** faction from the silhouette and marker, rank (leader or follower) from the clothes, and for the PMCs even the behaviour type from the outfit. No HUD, no name tags: information is earned.
- **One wardrobe:** every outfit is built from per-part shells on the shared box body (asset pipeline "Character body: overlapping rigid boxes"), so pieces are reused across factions and variety costs little.

### Non-goals

- Die Wächter (wait for the alien design, GDD §21).
- Boss looks beyond the hooks already in the visuals spec section 4 (bosses wear their faction's wardrobe plus their hook).
- Cold-weather and vehicle-crew outfits (M-1951 parka, telogreika, tanker coveralls): wait until cold or night maps or vehicles need them.

### Anti-goals

- No real unit insignia and no real Nazi symbols; every patch and badge is fictional.
- Nothing on any character glows; Ewigstein-touched things are never green (GDD).
- Variety must never hide the faction: the faction marker is on every variant.

## 0. Current state

| Today | Where | In this design |
|---|---|---|
| `Faction { Looters, Bandits, AlliedPmc, SovietPmc, Deserters }`; looters and bandits hostile to each other | `scripts/systems/Faction.cs`, GDD §16 hostility matrix | `Bandits` removed; `Looters` is the Plünderer (section 1) |
| Reputation: one faction standing `bandits` (Allied PMC, Soviet PMC, Bandits are tracked, GDD §5); looters have none (`ReputationBook.FactionId` maps Looters to null). Only the `--rep` dev flag changes `bandits` today | `ReputationBook`, `ReputationSummary` | the `bandits` standing is renamed `looters` and shown as Plünderer (section 1) |
| Actor rows `looter` and `bandit`; Faction Loadouts rows for Looters and Bandits | catalog (designer's xlsx), `data/actors/actors.json` | `looter` stays the follower; `bandit` becomes `bandenfuehrer`; the Bandits loadout rows become the Bandenführer's (section 5, designer) |
| Quest `q03_bandits` (kill bandits; rewards `side_qm` +10 and `side_faction` +5, no bandit reputation). Kill objectives match by faction only (`KilledEvent`, `ObjectiveSpec` have no actor field) | `data/quests/q03_bandits.tres`, quest model | retargeted to Bandenführer through a new actor filter (section 1) |
| Border Strip bandit waves; `WaveSpawner` kind `bandit` (flare telegraph, goes for the player's area) | map spec, `WaveSpawner` | gang waves: a Bandenführer with followers, same telegraph |
| Vogt, map 1 boss, "bandit chief": catalog rows only (`bosses.csv`: vogt, vogt_escort, kral have faction Bandits); not in the game yet (only Harker spawns) | factions spec section 3, catalog | catalog rows move to Looters; spawning Vogt stays with 8f |
| AI bodies use the old skinned `player_body.glb` plus one whole `outfit_looter.glb` (4 recolour textures, 5 faces painted on the old body's texture layout); the box body is loaded for the player only | `AiBodyVisual.Kinds` | AI move to the box body; outfits become per-slot rolled pieces (sections 3 and 5) |
| Allied and Soviet bots planned to wear the player outfits | death and ragdoll spec section 1 | four outfits per side by behaviour type (section 2) |
| Archetype weights: PMC Operator 75 / Infiltrator 25; Looters Conscript 70 / Rat 30; Bandits Infiltrator 50 / Conscript 50 | AI combat v2 spec, actors sheet | PMC weights change with the outfits; Bandenführer take the Bandits weights (section 2) |
| Squads: same-faction bots within reach join one `SquadCoordinator` (max 4); the first to join is `Leader`, and its death already fires `MoraleBroken`; then each low-tier bot (`LowTier`: Looters and Bandits) rolls against its panic to flee. Faction-only rules elsewhere: nemesis actor (`RaidNemesis.ActorFor`), nemesis name lists, grenade odds and shouting (`AiGrenade`) | `AiHuman.Squad`, `SquadCoordinator`, AI combat v2 spec section 8 | gangs are closed squads led by the Bandenführer; rank-aware rules by actor id (section 1) |

## 1. The Plünderer (designer decisions)

### Names (designer decision: German in-world names)

- Faction name shown in game: **Plünderer** (singular and plural). Elite: **Bandenführer** (singular and plural). Ordinary members are just "Plünderer".
- **Code and save id stay `looters` (Claude's call):** `Faction.Looters`, reputation key `looters`, actor `looter`; only the displayed text changes (through `GameText`), so code, saves and data change least. The UI font must render `ü`; check it with the first build.

### One faction

- `Faction.Bandits` is removed. Plünderer are neutral to each other and hostile to everyone else (Allied PMC, Soviet PMC, holdouts, Abteilung Ewig, Deserters, the Hollowed).
- GDD §3 line (Claude's wording): "Plünderer: desperate scavengers, some led by armed Bandenführer who hold camps and routes. Hostile; the ragged ones flee, the leaders do not."
- **Reputation (Claude's call):** the tracked faction standing `bandits` is renamed `looters` and shown as "Plünderer" on the Character tab and in GDD §5; the value carries over unchanged. `ReputationBook.FactionId` maps `Looters` to `looters`.
- **Save migration (Claude's call):** save version +1: the `bandits` standing key becomes `looters` (same value); nemeses whose faction was `Bandits` become `Looters` with actor `bandenfuehrer` (below).

### Gangs (designer decision: mixed groups led by an elite)

- A **gang** is one Bandenführer and 1 to 3 Plünderer **(tune)**. Spawn points and waves that used bandits spawn gangs; looter spawn points still spawn lone Plünderer or pairs as today **(Claude's call)**.
- **Rank is the actor, not the faction (Claude's call):** with both ranks in `Faction.Looters`, every rule that must tell them apart reads the bot's actor id (`looter` or `bandenfuehrer`): squad fleeing, nemesis actor, nemesis names, grenade odds and shouting (`AiGrenade` keeps the old bandit values for `bandenfuehrer`), quest kills.
- **The Bandenführer** fights like the old bandit (the `bandit` actor's stats, archetypes Infiltrator 50 / Conscript 50): holds ground, armour and a rifle, and is **exempt from fleeing** when its squad breaks or it is hurt (the old bandits did flee; this changes that, matching the elite the designer asked for).
- **The followers** are ordinary Plünderer (the `looter` actor): poor gear, panic easily, loot bodies.
- **The leader holds them together:** the gang spawns as one **closed** squad with the Bandenführer as `SquadCoordinator.Leader` (set explicitly, not first-to-join); lone Plünderer nearby do not join it, and gang members do not join other squads **(Claude's call)**. When the Bandenführer dies, the existing `MoraleBroken` fires and **every follower flees for certain** (no panic roll), then goes back to scavenging alone **(Claude's call)**. Followers within 30 m **(tune)** of their leader get a courage bonus (+0.2 **(tune)**) so they stand and fight while the leader stands.
- **Quests (Claude's call):** kill objectives get an optional actor filter (`ObjectiveSpec.ActorId`, `KilledEvent.ActorId`). `q03_bandits` becomes "Break a gang": kill Bandenführer (same count, same rewards); the id stays. `q01_looters` counts every Plünderer kill, Bandenführer included.
- **Nemesis (Claude's call):** `Nemesis` saves the killer's actor id, so a Bandenführer who kills the player comes back as a Bandenführer (`RaidNemesis.ActorFor` reads it); name lists by actor.
- **Vogt** (map 1 boss, not in the game yet): chief of the biggest Plünderer gang; his escorts are Bandenführer **(Claude's call)**. This is a catalog change only (the vogt, vogt_escort and kral rows move to Looters); spawning him stays with 8f.
- **Border Strip waves:** "bandit waves" become gang waves (a Bandenführer and 2-3 followers per entry **(tune)**), with the same telegraph and route.

## 2. Allied and Soviet PMC: four outfits per side (designer decision)

Every PMC bot wears its side's armband on the left upper arm (Allied pale blue, Soviet red) on every outfit. The outfit tells the bot's behaviour type, so a player learns to read who will flank and who will rush (designer chose the core set of four; the link to behaviour is Claude's call).

| Role | Behaviour type (archetype) | Allied | Soviet |
|---|---|---|---|
| Rifleman | Operator | **M65 field jacket** (the player's Allied outfit), olive; sleeves rolled or down | **M69 afghanka** (the player's Soviet outfit), olive-brown, sleeves down |
| Flanker | Rat (silent ambush, holds angles) | **ERDL leaf-pattern jungle jacket**, green and brown leaf camouflage; boonie hat | **KLMK camouflage coverall**, green and sand "sunray" overalls over the uniform; hood down |
| Marksman | Operator now; the marksman behaviour (AI combat v2 open item) when built | **British DPM 1968 smock**, brown and green brushstroke camouflage; beret or bare head | **East German rain-pattern (Strichtarn) uniform**, grey-green with thin vertical stripes |
| Assault | Infiltrator (pushes, jump-peeks) | **OG-107 shirt sleeves**, no jacket, sleeves rolled, olive T-shirt at the neck | **Telnyashka**: blue and white striped undershirt showing, sleeves rolled; blue beret |

- **Shared pieces:** trousers, boots and hands come from the side's player outfit (Allied olive OG-107 trousers, black boots, olive gloves; Soviet olive-brown trousers, brown boots, bare hands); only the upper body and headwear change, except the KLMK, which covers trousers too.
- **Headwear by role (Claude's call):** headwear the loadout gives (M1 helmet, SSh-68) replaces the outfit's hat or beret; with no helmet, the outfit's own (boonie, beret, bare) shows.
- **Archetype weights per side (Claude's call, tune):** Operator 55 (riflemen and marksmen split about 4 to 1), Infiltrator 25, Rat 20. Today's weights are Operator 75 / Infiltrator 25; adding Rat is a behaviour change, so it goes into the actors sheet by the designer (section 5).
- **Faces:** see section 4.

## 3. Ragged factions: rolled per bot (designer decision: mix and match)

Each bot rolls one option per slot and one of three muted tints per piece **(tune)**, so no two match. The faction marker is fixed.

### Plünderer (followers)

| Slot | Options |
|---|---|
| Jacket | blue cotton work jacket; brown corduroy coat; faded Bundeswehr olive moleskin parka |
| Trousers | grey work trousers; brown corduroy; faded jeans |
| Boots | brown work boots; green rubber boots |
| Hands | bare; grey wool gloves |
| Head | flat cap; knit watch cap; bare |
| Face cover | none; scarf over nose and mouth; rare (10% **(tune)**) old civil-defence gas mask |
| **Marker** | **a duffel sack slung on the back** (every follower) and a **hunched posture** (spine bent forward about 10° **(tune)** in idle and walk) |

### Bandenführer (elite)

| Slot | Options |
|---|---|
| Coat | long black leather coat, open or buttoned |
| Trousers | dark brown trousers |
| Boots | black boots |
| Hands | black leather gloves |
| Head | bare; flat cap; 1970s sunglasses with either |
| **Marker** | **an ochre neckerchief** (worn only by Bandenführer, the gang colour) and an **upright posture** |
| Armour | usually one piece of captured armour from the loadout table, worn over the coat (helmet or vest meshes as they exist) |

### Garrison holdouts

| Slot | Options |
|---|---|
| Jacket | faded, patched 1965 OG-107 field shirt; the same with torn-off sleeves; olive blanket poncho; grey civilian wool sweater |
| Trousers | faded OG-107 trousers, patched knees; civilian grey trousers |
| Boots | worn black combat boots |
| Hands | bare; cut-off wool gloves |
| Head | old M1 helmet with torn cover; bare with long hair |
| Face | long beard (most), gas-mask bag at the hip |
| **Marker** | **fictional garrison patch** on the right shoulder: a white tower on dark green |

### The Hollowed

- Rolled from three source wardrobes: **1965 villagers** (Plünderer jacket and trouser pieces in 1965 cuts: wool coats, aprons, dresses as a long-skirt shell), **lab staff** (white lab coat, grey trousers, gas mask on some) and **soldiers** (holdout pieces) **(Claude's call)**.
- Grey-blue skin tint on face and hands; white rime painted on the shoulders, hair and the tops of the boots; clothes darkened and torn.
- **Marker:** rime and stillness (the Hollowed spec's slow, jerky movement). Nothing glows; no green.

## 4. Uniformed factions: one outfit, small swaps (designer decision)

### Abteilung Ewig

| Slot | Look |
|---|---|
| Coat | knee-length dark grey field coat, belted |
| Trousers | dark grey, tucked |
| Boots | black jackboots |
| Hands | black gloves |
| Head | rounded dark composite helmet and a round-eyed gas mask, always on |
| **Marker** | **pale frost-crystal-in-a-ring** insignia (fictional) on the left sleeve and the helmet front |
| Role props | squad leader: map case at the hip; radio man: radio pack; spotter: binoculars on the chest |

Rauch keeps his hook: unmasked among masked men.

### Faces (Claude's call)

- **Four shared face textures** for all human factions (clean-shaven, stubble, moustache, beard), recoloured for three skin tones **(tune)**, painted new for the box head (the five old looter faces are on the old body's layout and do not carry over). Masked factions skip it. PMCs therefore never all look like the player.

## 5. Production, data and code

### Models (model designer)

- **AI move to the box body (Claude's call):** with this work every AI body uses the box body (one body file with bare hands; gloves are hand shells, so the hands slot never picks a different body). Until a faction's pieces exist it keeps today's body: the Plünderer keep the old `outfit_looter` look until their pieces arrive.
- Every piece is a per-part shell on the 19-bone box body, as the Allied and Soviet player outfits are built (Solidify shells copied from the body boxes). One piece per `.glb` slot file, named `outfit_<faction>_<slot>_<option>.glb` **(Claude's call)**, all sharing the faction's outfit texture atlas; tints are texture variants or a palette-row swap on the atlas.
- Order: Plünderer and Bandenführer, then the six new PMC outfits, then holdouts, Abteilung Ewig, the Hollowed.
- Previews per faction: one sheet with three rolled variants side by side, in the A-pose.

### Code

| Piece | What |
|---|---|
| `Faction` | remove `Bandits`; hostility table updated; every `Faction.Bandits` use moved to `Looters` or removed (reputation, quests, waves, nemesis names, AI range menu, `AiGrenade`) |
| `SaveSerializer` | version +1: `bandits` standing renamed `looters`; Bandits nemeses become Looters / `bandenfuehrer` |
| Reputation | `ReputationBook.FactionId` (Looters to `looters`), `ReputationSummary.Factions`, Character tab label |
| `OutfitRoll` (pure, tested) | from a faction's wardrobe (slots, options, weights, tints) and a seeded random, the list of pieces and textures for one bot; PMC outfit chosen by the bot's archetype |
| `AiBodyVisual` | builds the box body plus the rolled piece list instead of the old body and one outfit model; a faction with no wardrobe yet falls back to today's body; `Kinds` becomes data (`data/actors/wardrobes.json` or a resource, **builder's choice**) |
| Gangs | `AiHuman.Squad`, `SquadCoordinator`: closed gang squads with an explicit leader; certain flight on leader death; `LowTier` by actor (followers only); follower courage bonus near the leader |
| Rank rules | actor id on quest kills, `Nemesis` (saved), nemesis names, `AiGrenade` |
| Posture | a small spine offset per faction (hunched followers, upright leaders) on the body rig |
| `GameText` | faction names Plünderer and Bandenführer; quest text for "Break a gang" |

### Catalog and data (designer decision 2026-11-21: the build session edits the xlsx)

The build session edits `docs/design/item_catalog.xlsx` for these rows only, re-runs the exports (`tools/export_catalog.py`, `tools/export_actors.py`), and lists the changed rows in the commit message for the designer to check:

- Actors sheet: rename the `bandit` row to `bandenfuehrer` (Faction `Looters`); PMC archetype weights Operator 55 / Infiltrator 25 / Rat 20 (section 2).
- Faction Loadouts: the `Bandits` rows become a leader tier of `Looters`, and the `bandenfuehrer` actor links to it (`Loadout faction`, `Loadout tier`) instead of carrying its gear in its own cells.
- Bosses sheet: the vogt, vogt_escort and kral rows move from `Bandits` to `Looters`.
- Item rows that mention bandits as a source: wording only (Plünderer, Bandenführer).

### Docs updated with this spec

- GDD §3 timeline and factions table, §5 reputation factions, §16 hostility matrix (done with this commit).
- Factions spec section 1 table and decision 1: the Looters and Bandits rows point here (done with this commit).
- Death and ragdoll spec: "bandits" in the placeholder list becomes "factions with no body model yet" (done with this commit).

## 6. Build order (Claude's call)

1. The merge: enum, hostility, save migration, quest, waves, Vogt's escorts; gangs as squads with leader-death panic and the courage bonus.
2. `OutfitRoll` and the data-driven `AiBodyVisual` on the box body, with the fallback to today's body; the first wardrobe is the Allied and Soviet riflemen (the player outfits, already built), then each faction as its pieces arrive.
3. Models in the order of section 5; each faction's wardrobe data added as its pieces arrive.

## 7. Tests and playtest

- xUnit: hostility (Plünderer neutral to each other, hostile to all others); save migration (`bandits` value moved to `looters`, a Bandits nemesis becomes `bandenfuehrer`); quest actor filter; a Bandenführer never flees, followers always flee on leader death; `OutfitRoll` (seeded results stable, every roll has the faction marker, PMC outfit follows the archetype, weights respected over many seeds); gang squad: leader death breaks morale.
- Playtest: in the AI range, spawn gangs; kill the Bandenführer first and then a follower first: does the gang break and does it feel right? Walk past ten Plünderer: do they look different from each other but clearly the same faction? Fight PMC squads: can you tell the camouflaged flanker and the striped-shirt rusher apart before they act?

## Decisions

1. Looters and bandits merge into one faction, the Plünderer; elites are Bandenführer who behave like the old bandits.
2. Gangs are mixed: a Bandenführer leads 1 to 3 Plünderer, who flee when the leader dies.
3. German in-world names: Plünderer and Bandenführer.
4. Detailed looks for all human factions and the Hollowed; Die Wächter wait for the alien design.
5. Ragged factions roll their outfit per slot; uniformed factions keep one outfit with small swaps.
6. Each PMC side gets four outfits: rifleman, flanker, marksman, assault (Allied M65, ERDL, DPM, OG-107 shirt; Soviet afghanka, KLMK, Strichtarn, telnyashka).

## Open items

- Catalog edits by the build session (section 5), checked by the designer.
- Marksman behaviour (AI combat v2) for the marksman outfit's bots.
- Cold-weather and vehicle-crew outfits when maps or vehicles need them.
- Die Wächter's look with the alien design.
