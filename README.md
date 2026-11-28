# Ewigstein (codename Project D)

Single-player tactical extraction shooter with RPG progression, set in an alternate-history 1970 exclusion zone in former Germany. Godot 4.7.2 mono, C#. Design: [docs/GDD.md](docs/GDD.md). Specs: [docs/superpowers/specs/](docs/superpowers/specs/README.md).

## Current status

Last updated: 2026-11-01, checked against the code on `main` at `4f0d645`.

**In short:** the playable loop works from the hub through a raid and back (M0-M7). The current milestone is M8, and none of its six systems are built yet. Recent work went into three reworks outside the milestone list: AI combat v2, the animation system and weapon modding v2. Most of that still needs your playtest.

| Check | Result |
|---|---|
| `dotnet build` | 0 warnings, 0 errors |
| xUnit tests | 731 passed, 0 failed |
| GUT smoke test | 2 of 2 passed |
| Playtested in game | not checked here; only you can playtest |

## Milestones

| Milestone | Status | Notes |
|---|---|---|
| M0 Setup | **Code done** | benchmark on the weakest target machine still pending; renderer choice stays tentative until then |
| M1 Player | **Done** | movement, stances, granular crouch, lean, mantle, stamina, weight tiers, full-body first person |
| M2 Weapons | **Done** | projectiles, recoil, sway, ADS, resting, bipod, magazines, checks, phased reloads |
| M3 Damage | **Done** | hitboxes, per-limb health, status effects, meds, armor, energy and hydration, melee |
| M4 Inventory and loot | **Done** | grid inventory, equipment, containers, searching, secure container |
| M5 Human AI | **Done, being reworked** | see AI combat v2 below |
| M6 Raid loop | **Done on a test map** | spawns, timer, extraction, death and MIA. Map 1 is still a test blockout |
| M7 Hub | **Done** | stash, six traders, barter, insurance, six quests, autosave (save version 8 with skills) |
| **M8 Skills, reputation, signature systems** | **Current; 8a to 8e built; 8f bosses mostly covered** | table below |
| M9 Night raids | **Not started, no spec yet** | AI light zones are ready for a time-of-day value |
| M10 Slice polish | Not started | |

### M8 breakdown

All six specs are written; no system is built.

| Part | Status | Spec |
|---|---|---|
| 8a Skills, including Vitality | **Built (steps 1-4):** `SkillSpec`, `SkillBook`, `SkillEffects`, the seven `data/skills/*.tres`, save version 8 with the `skills` map; XP reporters (`PlayerSkills` feeds every skill from sprint, mantle, carry, shots, reloads, checks, loading, medical use, searching and enemy hits; the raid starts and ends the book; a level-up shows as a message). effect hooks (`Player.ApplySkillEffects` hands the levels to the leg stamina pool, weight tiers, reload and check times, exact mag check, recoil, medical use time, search speed, hit damage and, at raid start, max HP). level-up line on the HUD, a skills section on the raid result screen, and a Character tab in the hub. **8a is built; playtest pending** | [m8a](docs/superpowers/specs/m8-signature/m8a-skills-design.md) |
| 8b Reputation and friendly fire | **Built (steps 1-3):** `ReputationBook` (hundredths, clamped, price factors, refusal, raid-start hostility), `FriendlyFireTracker`, reputation in `TradeEngine`, `InsuranceService` and quest acceptance, the opposite-quartermaster trade penalty, reputation rewards on the six slice quests, trader screen prices and refusal text. raid side (`RaidFriendlyFire` feeds the player's bullet and melee hits on own-faction AI to the tracker and the reputation book; `RaidStanding` makes a hostile faction attack the player on sight through `AiHuman.HostileTo`; a low-standing own faction starts the raid hostile). HUD line when a faction turns hostile, reputation changes on the raid result screen, every trader and faction standing on the Character tab, standing and price effect on the trader screen (buttons disabled when refused), readable reputation rewards in quests. **8b is built; playtest pending** | [m8b](docs/superpowers/specs/m8-signature/m8b-reputation-friendly-fire-design.md) |
| 8c Nemesis | **Built (steps 1-4):** `KillAttribution`, `Nemesis`, `NemesisRoster` (create, grow to rank 3, cap 3 per map, spawn roll, removal), `NemesisGear` (salvage minus insurance returns, equip, best weapon), `NemesisNames` and `data/ai/nemesis_names.json`, save version 9 with `nemeses[]`. raid side (`RaidNemesis` records the AI that hits the player from `ProjectileManager.Hit`, and a death creates or grows the killer's nemesis before the profile is saved, using `RaidManager.LostSlots`; MIA and attackerless deaths make none; dev flag `--nemkill`). spawning and fighting (`RaidNemesis.SpawnFromRoster` rolls 30% per nemesis at raid start, one at most, at the navmesh point nearest the kill, patrolling 15 m, +10% accuracy and courage per rank; `AiHuman.Nemesis` makes it wear and fire your real armor and best weapon and drop exactly its inventory as the body; armor wear is written back if it survives; killed by the player, another AI or the zone it leaves the roster; dev flags `--nemseed`, `--nemspawn`, `--nemslay`). `MapLandmark` markers (Border Strip from its layout, four on map 1), the place of each kill saved with it, the hub Nemeses tab (rank, kills on you, where, what it carries, news of dead nemeses), the death-screen line and a gold HUD line when you kill one. **8c is built; playtest pending** | [m8c](docs/superpowers/specs/m8-signature/m8c-nemesis-design.md) |
| 8d Knowledge and journal | **Built (steps 1-3):** `KnowledgeBook` (unlock, one translation at a time counted in raids of any outcome, effects withheld until translated, items stop spawning once known), `ReadTimer` (4 s, damage cancels), extraction unlock for carried and secure items, `KnowledgeData` and the six slice entries in `data/knowledge`, their six knowledge items (new category `Knowledge`, only the Scientist buys), knowledge prerequisites on quests, save version 10. raid side: right-click a document in the inventory to read it (4 s, a hit breaks it, bar on the HUD), extraction unlocks carried documents, every raid end counts a translation down and the result screen lists what you learned, `KnowledgeSpot` plants documents unless known (six placeholder spots on map 1, the guard diary on the Border Strip), `LockedDoor.CodeKnowledgeId` opens a door by code in 2 s, `LootSpot.RequiresKnowledgeId` hides a cache until its stash is known (a tower cache on the Border Strip); dev flags `--giveknow`, `--readnow`, `--knowcode`, `--translate`, `--doorcode`. hub: Journal tab (`JournalScreen`, `KnowledgeSummary`: entries by kind, lab notes untranslated / translating / translated, stash entries name their landmark), Scientist Translate tab (one note at a time, free); Border Strip content: all six documents placed, the radio room also opens by the door code, the tower cache sits by the Allied watchtower; dev flags `--journal`, `--hubknow=id,id`. Map 1 keeps six placeholder spots and has no side room (it is a test blockout) | [m8d](docs/superpowers/specs/m8-signature/m8d-knowledge-journal-design.md) |
| 8e Ewigstein and exposure | **Built (steps 1-3):** items `ewigstein_shard`, `small_ewigstein_canister`, `large_ewigstein_canister`, `secure_case` (catalog shapes; categories `Ewigstein` and `Canister`), grid rules (`GridRule`: canister takes shards only, secure container refuses shards, canisters and anything holding them), pure `ExposureModel` + `ExposureTuning` (meter, deposit falloff, decay, sway curve, thresholds, HP loss), `EwigsteinRules` (unshielded shard count, shard into a free canister first), Scientist sells canisters (small L1, large L2). raid side: `EwigsteinDeposit` (scene `scenes/world/ewigstein_deposit.tscn`: interact to harvest 10 s, noise heard within 25 m, a hit breaks it, one shard per deposit, no room keeps the shard in the deposit), `RaidExposure` (feeds `Player.Exposure` each physics frame, HP loss straight to thorax and stomach, screen ripple and colour shift from 50, generated hum and radio static), sway multiplier in `Player`, HUD `EXPOSURE n%` tag and harvest bar; dev flags `--giveshard=n`, `--givecanister=small|large`, `--spawndeposit`, `--harvestnow`, `--expreport`, `--expscale=x`. delivery: quest `q07_first_delivery` (your quartermaster, needs the translated lab note, FindInRaid 1 shard, reward `secure_case` + rep +10 quartermaster and +10 faction + journal entry "First delivery" via new `RewardKind.Knowledge`), hand-in objectives now count items inside nested grids (a shard in a canister), the lab note text hints at the deposits; map 1 has two deposits (by the portal and at the culvert end), the Border Strip has none (its spec says so); dev flag `--translated` with `--hubknow` | [m8e](docs/superpowers/specs/m8-signature/m8e-ewigstein-design.md) |
| 8f Bosses | Not started; boss HP and data exist in the catalog, but no boss spawns | [m8f](docs/superpowers/specs/m8-signature/m8f-bosses-design.md) |

## Reworks in progress

Each spec has a detailed "Implementation status" table.

| System | Status | Main things left |
|---|---|---|
| [Save parts](docs/superpowers/specs/architecture/save-parts-design.md) (each piece of saved state saves itself; all-or-nothing load) | **Built, playtest pending** | file format unchanged (version 10) |
| [Composable actors](docs/superpowers/specs/ai/composable-actors-design.md) (AI stats from catalog sheets, F5 inspector) | **Built** | old `AiPreset` oracle removed 2026-11-10; player skills wait for M8a |
| [AI combat v2](docs/superpowers/specs/ai/ai-combat-v2-design.md) | **Steps 1-7 built, playtest pending** | bot stamina; corner pieing; armor retargeting; marksman behaviour; boss squads; voice barks; flashlight and NVG rules (M9); grenades (post-slice). No light zones, foliage or wall material tags are placed on maps yet |
| [Animation system](docs/superpowers/specs/animation/animation-system-design.md) | **Partly built** | see below |
| [Weapon modding v2](docs/superpowers/specs/weapons/weapon-modding-v2-design.md) | **Mostly built** | ammo recoil modifier; effective range display; drag and drop in the workshop; most part models |

**Animation, built:** locomotion graph, socket ADS, support-hand grips, spring recoil, inertia sway, high and low ready, view bob, foot IK, phased reloads with resume, whole-body turning, stance transitions, prone crawl and prone lean.

**Animation, not built:** additive layers (breathing, limp, tremor, flinch); authored clips for checks, melee, draw and item use; clips for pivot, stance and crawl; sprint carry pose; trigger finger; AI animation LOD. Bolt, pump, belt-fed and revolver mechanisms wait for the post-slice weapons.

## Not built yet

- **Maps:** the [raid map standard](docs/superpowers/specs/maps/raid-map-standard-design.md) pipeline, the [map 1 revision](docs/superpowers/specs/maps/map1-revision-design.md), transits and the seven other maps are design only, except the layout parser, validator and graybox generator, plus the first Border Strip layout and graybox (`MapLayout`, `MapValidator`, `GrayboxBuilder`, `GrayboxTscn`; run with `godot --headless --path . -s res://scripts/tools/GenerateMap.cs -- <map id>`). The game has one raid map.
- **Story:** acts, lock meter, story missions and endings are design only; the [story mission framework](docs/superpowers/specs/story/story-mission-framework-design.md) is written, not built. Mission 1 First Delivery exists as quest `q07`.
- **Paper maps and chance extracts:** the [spec](docs/superpowers/specs/maps/paper-maps-chance-extracts-design.md) is written, not built (map items wait for catalog rows).
- **Faction looks and the Plünderer merge:** the [spec](docs/superpowers/specs/factions/faction-looks-pluenderer-design.md) is written; the Plünderer, Bandenführer, PMC and garrison holdout outfit models are exported; PMC bots now wear their outfits by behaviour type, the rest are not worn yet (looters and bandits becoming one faction, outfit rolls for the ragged factions, Abteilung Ewig and the Hollowed are not built).
- **Raid end refactor:** the [spec](docs/superpowers/specs/architecture/raid-end-design.md) is written, not built (before faction rules).
- **Inventory transfer and found in raid:** the [spec](docs/superpowers/specs/architecture/inventory-transfer-design.md) is written, not built (after raid end; ground pickups now count as found).
- **Facial animation:** the [spec](docs/superpowers/specs/animation/facial-animation-design.md) is written, not built (after the Plünderer merge; needs the face atlas and a bare face panel on the body textures).
- **Faction rules refactor:** the [spec](docs/superpowers/specs/architecture/faction-rules-design.md) is written, not built (do before the Plünderer merge).
- **Factions:** the code has looters, bandits and both PMC sides. Garrison holdouts, Abteilung Ewig, the Hollowed, Die Wächter and Die Troika are design only.
- **Characters:** trader portraits are rendered and shown on the trade screen (Weiss's revealed portrait waits for the mission 4 quest id); story characters and mission NPCs are design only.
- **Audio and doors:** the game has no sound yet; the [audio spec](docs/superpowers/specs/audio/audio-design.md) and the [doors spec](docs/superpowers/specs/world/doors-design.md) are written, not built.
- **Menus and settings:** the game boots into the hub with no menus or settings; the [menus and settings spec](docs/superpowers/specs/ui/menus-settings-design.md) is written, not built.
- **Post-slice systems:** grenades (the [throwables spec](docs/superpowers/specs/weapons/throwables-design.md) is written), malfunctions, zombies, aliens.

## Content in the game

| Content | In the game | In the catalog |
|---|---|---|
| Firearms | 5: AK type, FAL type, EM-2, MG type, pistol | 64 weapon rows |
| Weapon parts | slice set | 78 rows |
| Traders and quests | 6 traders, 6 quests | |
| Raid maps | 1 test blockout | 8 designed |

| Art | Done | Still placeholder |
|---|---|---|
| Characters | player body, Allied and Soviet outfits, looter outfit with random faces, flat cap | other factions are capsules |
| Weapons | FAL model with parts; Makarov PM model, not in the game's weapon data yet | other weapons are boxes |
| Locomotion | Quaternius CC0 clips for forward stand and crouch movement | sides, diagonals and prone use a generated gait |

## Needs you

1. **Playtest AI combat v2.** Under-fire reaction, aim, cover, personalities and squads are all waiting on this. Removing the old AI presets waits on it too.
2. **Playtest the turning, stance and prone work.**
3. **Run the benchmark on your weakest machine.** This closes M0 and settles the renderer.
4. **Raid time readout** is decided (press O, [spec](docs/superpowers/specs/ui/raid-time-readout-design.md)); not built yet.
5. **Choose the next step:** build M8, or get the M9 night raids spec written.

## Housekeeping

- `main` has 36 commits not pushed to `origin/main`, counting this update.

## Commands

```bash
dotnet build "Project D.csproj"
```

```bash
dotnet test tests/ProjectD.Tests/ProjectD.Tests.csproj
```

```bash
godot --headless --path . -s addons/gut/gut_cmdln.gd -gdir=res://tests -ginclude_subdirs -gexit
```

```bash
python tools/export_catalog.py
```

## Debug tools (debug builds only)

In a raid: **F1** debug menu (god mode, noclip, heal, give an item by id, teleport to documents, deposits and exits, exposure, bots, game speed), **F2** noclip, **F4** bot cones. The menu also switches a **hit log** (bottom right: target, body part, range, damage, armor, HP left, bleeds and fractures for each shot you land; misses name what they hit) and a **debug map** (top left: radar with the German lab note in gold, other documents, crystal deposits and exits, plus a distance and bearing line for the lab note). A switch shows **NPC hitboxes** (head red, torso orange, limbs blue). Flags: `--debugmenu`, `--debugmap`, `--debughitboxes`, `--debugshot`, `--perf` and `--perfquit` (fps and draw calls at the spawn, exits and centre), `--flarenow` and `--helidelay=s` (fire a flare at the landing zone), `--waveclock=secs`.

## Throwables

Spec: [throwables](docs/superpowers/specs/weapons/throwables-design.md). Build steps 1 to 5 are in (stun, tear gas, molotov, thermite that burns doors open, thrown flares, the burning status; bots throw frags and smoke and dodge grenades; dev flag `--aithrow`; smoke blocks AI and player vision; bottle and rock draw AI to where they land): all five frags (M67, F-1, RGD-5, egg, M24 stick) and the MK3 concussion; Strength raises throw speed. The M67 (`frag_grenade`, Allied QM or the F1 give list). Bind it to a quick-use slot (hover it in the inventory and press 4 to 7), press the key to take it in hand, hold left mouse to pull the pin and throw overhand, hold right mouse for an underhand lob, press reload to cook it. Dev flag `--throwgrenade=cookSeconds` (a negative number sets one off 2 m from the nearest bot). Not yet: placing thermite with interact, gas masks, grenade kicks, real recordings.
