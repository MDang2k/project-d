# Project D — Game Design Document

Version 0.1 · **Ewigstein** (project codename "Project D") · single-player · Godot 4 · PC

Values marked **(tune)** are starting points to adjust through playtesting. Items marked **(TBD)** are undecided — ask the designer before implementing them.

---

## 1. Overview

| | |
|---|---|
| Genre | Single-player tactical extraction shooter with RPG progression |
| Perspective | First person |
| Platform | PC. Target: 60 fps at 1080p, lowest settings, dedicated GPU with 6 GB VRAM (GTX 1060 6GB class) |
| Inspirations | Escape from Tarkov (loop, inventory, traders, quests), Rising Storm 2: Vietnam (movement, weapon resting, bipods), STALKER (exclusion zone), Half-Life (resonance cascade), BattleBit Remastered (art style) |
| Scope | Hobby project. First goal is a playable vertical slice (§20). |

## 2. Design pillars

1. **Weight is a choice.** A light kit moves like Rising Storm 2; a heavy kit moves like Tarkov. Every item you bring or loot trades firepower and protection against mobility.
2. **Information is earned.** No ammo counter, no minimap, minimal HUD. You check your magazine, listen for footsteps and read the terrain.
3. **Every raid risks gear — never knowledge.** Death loses everything outside the secure container. One autosave, no reloading. What you have learned stays with you, and what you lost can be hunted down.
4. **The zone hides a secret.** Progression is not just better gear — it uncovers what happened in 1965.

## 3. Setting

### Timeline

- **1939–1965:** The Second World War continues for twenty years longer than in our history.
- **1943:** German engineers find the outer alien ruins beneath central Germany. Crude salvage (prototype armor, power cells, relic weapons) keeps the Reich fighting and stalls the Allies.
- **1950s:** The dig reaches the inner ruins and the Ewigstein lattice; Project Ewigstein begins. The full truth (the ruins are a prison and Ewigstein is its lock) lives in `docs/superpowers/specs/story/2026-10-21-story-core-truth-design.md` and is revealed only through play.
- **1965 — Event 65**, the disaster. An experiment triggers a chain reaction, similar to Half-Life's resonance cascade. A series of massive explosions devastates central Europe and turns Germany into a wasteland. The war ends.
- **1965–1970:** The Allies and the Soviet Union jointly declare an exclusion zone. Both send private military contractors in to secure artifacts and research, officially to "contain" the zone. Scavengers, looters and bandits settle in the ruins. The fallout turns some people into zombies, the **Hollowed**. The zone is officially the Sperrgebiet, "the Sperr" to contractors.
- **1970:** The game begins. Late in the story, aliens begin to appear.

### Tone

Grounded Cold War military realism on the surface; creeping sci-fi horror underneath. The first maps feel like a ruined, contested Germany. Zombies appear in later milestones; aliens are a late-story escalation and should not be seen early.

### The crystal: Ewigstein

The central plot device.

- **What it is:** an alien crystal found in the ancient ruins. The Nazi research program codenamed it *Ewigstein* ("eternal stone"). It appears to be an **unlimited power source**. Nobody, including the original researchers, fully understands it.
- **Its role in history:** the Nazis' attempts to tap it caused Event 65. Exposure to it in the fallout turned people into zombies. Both superpowers now want it — the power that destroyed Germany could decide the Cold War. Historical parallel: the post-1945 race for German scientists and uranium.
- **Names by faction:** Allied codename "Material HALCYON", Soviet "Izdeliye 7", scavenger slang "frostglass".
- **Presentation:** restrained, never a bright neon glow. Near it: a faint hum, metal frosting over, a spinning compass needle (useful, since there is no compass), radio static.
- **Avoid:** making it look or sound like Call of Duty Zombies' Element 115 or C&C's Tiberium (no green glow, no element number, no stat-buff "artifact" use).

### Factions

| Faction | Role | Default attitude to player |
|---|---|---|
| Looters | Desperate scavengers, poorly armed, often flee | Hostile, cowardly |
| Bandits | Organized criminals controlling routes and camps | Hostile |
| Allied PMC | Western contractors, NATO gear | Friendly if player chose Allied, else hostile |
| Soviet PMC | Eastern contractors, Warsaw Pact gear | Friendly if player chose Soviet, else hostile |
| Zombies (post-slice) | Fallout victims, melee, swarm, sound-driven | Hostile to all humans |
| Aliens (late story) | TBD | Hostile to all |

## 4. Core loop

1. **Hub (menu-based):** manage stash, buy/sell/barter with traders, take and turn in quests, insure gear, equip loadout.
2. **Deploy:** choose map; game autosaves with loadout marked "at risk".
3. **Raid:** loot, complete objectives, fight AI, manage health and weight.
4. **Outcome:**
   - **Extracted:** keep everything carried.
   - **Died or timer expired (MIA):** lose all gear except the secure container's contents. Insured items roll for return (§15).
5. **Back in hub:** sell loot, turn in quests, gain trader reputation, skills have improved through use. Story missions unlock through progression.

## 5. Player identity and reputation

- At the start the player chooses a side: **Allied** or **Soviet** contractor. This sets the starting quartermaster and which PMCs are friendly.
- Reputation is tracked per trader and per faction (Allied PMC, Soviet PMC, Bandits). Range −1.0 to +1.0 **(tune)**.
- **Opposite quartermaster:** unlocks through quests or reputation later. Working with the other side costs some reputation with your own, but not much — it must be possible to be on good terms with both.
- **Friendly PMCs in raids:**
  - Do not target the player; fight enemies hostile to both.
  - Do not take orders (no squad system).
  - Friendly fire is possible. Hitting one costs a small amount of reputation; killing one costs a large amount. Repeated hits in one raid make that group hostile for the rest of the raid.

## 6. Movement

### Stances and actions

Walk, sprint, crouch, crouch-sprint, prone, mantle (low obstacles), lean left/right (any stance except prone and sprint), free look. Movement has inertia that scales with carried weight: heavier = slower acceleration, slower turning while sprinting.

### Player body

Full-body first person: looking down shows legs and torso, and the body casts shadows. Arms hold the weapon through IK so leaning, resting and bipods look physically correct without hand-made animations in the slice. Hand-made animations (reloads, mag checks) replace or layer on top of this when custom art arrives. The animation system (layered graph, socket-based ADS, spring recoil, interruptible phased reloads) is specified in `docs/superpowers/specs/animation/2026-10-25-animation-system-design.md`.

### Stamina

Separate pools for legs (sprinting, mantling) and arms (aiming, holding breath). Recovery slows under heavy load. Exhaustion increases sway and breathing noise.

### Weight tiers (tune)

| Carried weight | Effect |
|---|---|
| < 20 kg | Full mobility |
| 20–35 kg | Slower movement and sprint, faster stamina drain |
| 35–50 kg | Heavy: no sprint, slow movement, noticeable inertia |
| > 50 kg | Overloaded: barely able to walk |

The Strength skill raises these thresholds slightly.

## 7. Weapon handling

### Aiming

Hip fire, aim down sights, hold breath (reduces sway, drains arm stamina). Weapon sway depends on stance, stamina, weight of the weapon, injuries (arms) and pain.

**No crosshair.** Hip fire is aimed by where the weapon visibly points.

### Resting and bipods

- **Resting:** when the weapon is near a ledge, wall edge, window frame or similar surface, the player can rest it. Rested weapons have strongly reduced sway and reduced recoil.
- **Bipods:** weapons with a bipod can deploy it on a suitable surface or when prone. Deployed bipods give near-zero sway and much lower recoil, but restrict horizontal aim range.
- Level design must provide restable surfaces deliberately (see §19).

### Ammunition and magazines

- **No ammo counter.** Ammo state is never shown on the HUD.
- **Magazine check:** an animated action. Result is shown as an estimate ("full", "about half", "almost empty", "empty"). The Weapon Handling skill makes it faster and more precise; at high levels it shows an exact count.
- **Chamber check:** shows whether a round is chambered.
- Every magazine is a distinct item with its own round count and ammo types (load order matters).
- **Tactical reload:** removes the current magazine and keeps it in inventory.
- **Fast reload:** drops the current magazine on the ground. Faster.
- Loading rounds into magazines takes time and is done from the inventory.

### Malfunctions (post-slice)

Jams and misfeeds tied to weapon condition. Must have a clear cause the player can manage (condition, ammo quality). Include an option to disable. Not in the slice.

### Default controls (tune)

| Action | Key |
|---|---|
| Move | WASD |
| Sprint / hold breath while aiming | Shift |
| Crouch / prone | C / X (hold C + mouse wheel: granular crouch height) |
| Jump / mantle | Space |
| Lean left / right | Q / E |
| Aim | Right mouse |
| Fire | Left mouse |
| Reload (tactical) / fast reload | R / double-tap R |
| Magazine check / chamber check | T / Shift+T |
| Deploy bipod / rest | B (resting may be automatic — decide in playtest) |
| Fire mode | V |
| Melee | G (tune) |
| Flashlight toggle | L (tune) |
| Interact | F |
| Inventory | Tab |
| Free look | Hold Alt |

## 8. Ballistics and armor

Deliberately simpler than Tarkov.

- Bullets are simulated projectiles with gravity drop and muzzle velocity. No wind in the slice.
- **Ammo** has: damage, penetration tier (1–5), velocity.
- **Armor** (vests, helmets) has: armor tier (1–5), durability, protected zones.
- Resolution when a bullet hits an armored zone:
  - **Penetration tier ≥ armor tier:** bullet penetrates; damage reduced by a small fraction **(tune)**; armor loses durability.
  - **Penetration tier < armor tier:** bullet is stopped; the limb takes reduced blunt damage **(tune)**; armor loses durability.
  - At 0 durability the armor tier effectively drops to 0.
- **1970 context:** most armor is flak vests (tier 1–2) and steel helmets (tier 1–2). Full-power rifle rounds are tier 3–4 and defeat almost all period armor. Prototype and alien-derived armor (tier 4–5) are late-game exceptions. Rifles are very lethal by design.

## 9. Health

Tarkov-style per-limb health.

| Body part | HP (tune) | At 0 HP |
|---|---|---|
| Head | 35 | Death |
| Thorax | 85 | Death |
| Stomach | 70 | Destroyed: further damage spreads to other limbs |
| Left / right arm | 60 each | Destroyed: much more sway, slower reloads |
| Left / right leg | 65 each | Destroyed: severe limp, no sprint |

Damage to a destroyed limb is spread across the remaining parts (multiplier **(tune)**).

### Status effects

- **Light bleed:** slow HP loss. Fixed with a bandage.
- **Heavy bleed:** fast HP loss. Fixed with a tourniquet.
- **Fracture:** limb penalties (limp, sway). Fixed with a splint.
- **Pain:** blurred vision edges, tremor. Suppressed by morphine.

### Slice medical items

Bandage, tourniquet, splint, morphine, field medkit (restores HP to non-destroyed limbs over time), **surgery kit**.

### Destroyed limbs

- **Surgery kit (in raid):** restores a destroyed limb (except head/thorax, which are death). Slow to use, heavy and rare. The restored limb comes back with reduced maximum HP until the end of the raid **(tune)**.
- **Hub:** all limbs are fully restored between raids.

### Energy and hydration

- Two meters, 0–100 **(tune)**. Both drain slowly over time and faster when sprinting, carrying heavy loads or injured.
- Low energy: slower stamina recovery. Low hydration: more sway and slower stamina recovery. At 0, slow HP loss to the thorax/stomach.
- Restored by food and drink items (canned food, ration packs, water canteen, etc.). Items have weight and grid size like everything else.
- Both reset to full between raids (simplest for the slice; revisit later).

## 10. Inventory

Tarkov-style grid inventory.

- **Items** have a grid size (w × h), weight, and can be rotated.
- **Equipment slots:** head, armor vest, chest rig, backpack, primary weapon, secondary weapon, pistol, melee, secure container.
- Rigs and backpacks provide their own grids; total capacity comes from what you wear.
- **Secure container:** starts at **2×2**. Larger containers are unlocked through the story. Contents survive death.
- **Searching:** containers in the world (crates, lockers, bodies) take time to search; items are revealed one by one. Searching is noisy (tune).
- **Stash:** a large grid in the hub. Can be expanded later (TBD).
- **Weight** is the sum of everything worn and carried, and drives §6.

## 11. Weapons and attachments

### Attachment slots (slice)

Optic, muzzle, magazine, underbarrel/bipod, and tactical light where applicable. Each attachment modifies stats (sway, recoil, weight, ergonomics, noise). Full gunsmithing (barrels, stocks, handguards, receivers) comes after the slice.

Period-appropriate night optics (e.g. early starlight scopes, which existed in the 1960s) can appear as rare, heavy optics.

### Melee

One melee weapon slot (knife, bayonet, entrenching tool, etc.). Melee is a quick attack with its own key. Damage hits a single limb and skips armor entirely (full damage, no durability loss). Silent, but short range. Matters more once zombies arrive.

### Slice weapons (5)

| Weapon | Type | Caliber | Notes |
|---|---|---|---|
| Pistol (e.g. Walther P1 or Makarov) | Pistol | 9×19 / 9×18 | Sidearm |
| AK-type | Assault rifle | 7.62×39 | Soviet staple |
| FAL-type | Battle rifle | 7.62×51 | Allied staple |
| EM-2 | Bullpup | .280 British | British prototype, rare |
| MG42/MG3-type | Machine gun | 7.92×57 / 7.62×51 | Bipod, heavy |

Real-world weapon names may be used during development. Before any public release, review whether names should be fictionalized. Weapons designed after WW2 get alt-history names and slightly altered models; HK designs use WW2-style names (Gewehr 59, Sturmgewehr 68, MP 66, Gerät 69). Real dates are a guideline: post-1970 prototypes are allowed as zone research ahead of its time. The full item list lives in `docs/design/item_catalog.xlsx`.

### Later weapon pool (dates to verify before use)

Mix of real 1945–1970 weapons and Cold War prototypes from both sides, plus alt-history designs. Candidates:

- **Prototypes:** Stoner 63 system, SPIW program weapons, Gyrojet rocket pistol, TKB-022 and TKB-408 Soviet bullpups, an early HK G11 caseless prototype (program started late 1960s).
- **Zone-only relics:** German late-war designs such as the StG 45, found only in labs and bunkers.
- **Late game:** alien-artifact-enhanced prototypes (TBD).

## 12. Skills

Skills improve through use; there is no player level. Each skill has levels 1–10 **(tune)** with diminishing XP gain from repeating the same action within a raid, to prevent farming.

| Skill | Trained by | Effect |
|---|---|---|
| Endurance | Sprinting, mantling | Leg stamina pool and recovery |
| Strength | Carrying weight, throwing | Higher weight thresholds |
| Weapon Handling | Reloading, mag checks, loading mags | Faster/more precise mag checks, faster reloads |
| Recoil Control | Firing | Less recoil |
| First Aid | Using meds | Faster med use |
| Searching | Searching containers | Faster searching |
| Vitality | Taking hits from enemies | Higher HP on every body part, less hit damage |

Skills never decay. XP earned in a raid is kept on death or MIA.

## 13. Traders

Six traders, each with reputation levels 1–4 **(tune)**. Higher levels unlock better stock and quests. Level-ups require reputation plus money spent (and sometimes a quest).

| Trader | Sells / buys | Notes |
|---|---|---|
| Allied quartermaster | NATO weapons, ammo, gear | Starting trader if Allied |
| Soviet quartermaster | Warsaw Pact weapons, ammo, gear | Starting trader if Soviet |
| Smuggler | Black-market goods, prototypes | Neutral |
| Medic | Meds, health supplies | Neutral |
| Scientist | Buys alien artifacts and documents | Neutral; drives story missions |
| Engineer | Weapon parts, optics, rare weapon barters | Neutral |

Each trader has names and personalities TBD. Barter trades (items for items) are supported alongside money. Insurance is bought from quartermasters (§15).

## 14. Quests

### Quest types

- **Fetch:** find item X and hand it in.
- **Find in raid:** item must be found during a raid, not bought.
- **Eliminate:** kill N of faction X (optionally on map Y, with weapon type Z).
- **Locate/mark:** reach a location or place a marker.
- **Extract:** survive and extract with specific conditions.

Quests have prerequisites (trader level, previous quest), rewards (money, items, reputation, unlocks) and may be chained.

### Ewigstein in gameplay

- Ewigstein is a **story-progress resource only** — it cannot be sold for money. Delivering it to your government advances the story, unlocks story missions and larger secure containers.
- **Containment:** Ewigstein must be carried in a lead-lined containment canister (heavy, takes grid space). Canisters come in sizes **(tune)**.
- **Exposure (in raid only):** carrying Ewigstein unshielded, or being near deposits, builds an exposure meter. Rising exposure causes increasing sway, vision distortion and finally slow HP loss **(tune)**. Exposure resets after the raid; there is no long-term contamination.
- **Attracts zombies** (post-slice, once zombies exist): carrying it, especially unshielded, draws zombies from further away.
- **Double-agent option:** small quantities can be secretly delivered to the opposing side through special quests, at a reputation risk (post-slice).

### Story missions

Unique, scripted raids unlocked by progression, mostly through the Scientist. They can use unique maps, events and characters, and reveal what happened in 1965. Story missions also unlock larger secure containers. Story beats: **TBD** (separate writing pass).

### Endings (post-slice)

Where the player delivers the decisive Ewigstein at the end of the story determines the ending:

1. **Your own government:** your side wins the energy race.
2. **The other side:** betrayal; requires sufficient reputation with them.
3. **Return it and seal the ruins:** nobody gets it. (Full ending design, including a hidden fourth ending, in `docs/superpowers/specs/story/2026-10-22-story-endings-design.md`.)

Each ending needs its own final mission(s). A fourth, hidden ending exists (see the endings spec).

## 15. Raids, extraction, death, saving

- **Raid timer:** first map 20 minutes **(tune)**; medium maps 30+ minutes.
- **Spawns:** random spawn point from a set per map.
- **Extraction points:** 3–4 per map. Types: always open, conditional (requires item or payment), and possibly chance-based (TBD). Extracting takes a few seconds standing in the zone.
- **Timer expires:** counts as MIA = death outcome.
- **Reading the time:** no on-screen timer. The player checks an analog wristwatch (held key); raids start at a fixed clock time and the deploy screen states the deadline (character visuals spec).
- **Death:** lose everything except secure container contents.
- **Insurance:** flat return chance per insured item, based on the trader's level **(tune)**. Returned items arrive in the hub after the next raid. Not all items can be insured (e.g. quest items).
- **Time of day:** fixed per raid, chosen when deploying: **noon** or **night**. No day/night cycle within a raid. Night raids: dark with moonlight, flashlights and night optics matter, AI vision is strongly reduced in darkness but flashlights make the player very visible. Night raids could offer better loot or quest conditions (TBD).
- **Saving:** one autosave slot. Saved at raid start (loadout marked "at risk") and when back in the hub. Quitting or crashing mid-raid counts as death on next launch.

### Nemesis: your killer takes your gear

- When an AI kills the player, that AI becomes a **nemesis**: it gets a generated name and nickname and keeps the player's full loadout (minus the secure container and any insured items that later return).
- The nemesis is stored in the save. On later raids to the same map, it has a chance to spawn **(tune)**, wearing and using the player's gear.
- Killing the nemesis recovers whatever gear it still carries. A nemesis that survives long enough may "sell" pieces of the gear, which then appear in trader stock or on other enemies (post-slice).
- Nemesis enemies are slightly tougher than their faction baseline (courage, accuracy) **(tune)** and have a short dossier visible in the hub (name, faction, where they killed you, what they carry).
- Limit active nemeses (e.g. 3 per map) **(tune)**; the oldest is retired when a new one appears.
- If a nemesis is a friendly-side PMC (friendly fire incidents can cause this), they are hostile to the player only.
- Insured items and nemesis loot do not duplicate: an item is either with the nemesis or returned by insurance, never both.

### Persistent knowledge

Knowledge survives death; gear does not.

- **Knowledge items** (documents, lab notes, door codes, radio frequencies, stash locations) are picked up in raids. Reading one in raid (a few seconds, interrupted by damage) or extracting with it unread unlocks its knowledge is permanently unlocked in the save, even if the physical item is later lost.
- Some knowledge needs processing in the hub, e.g. German lab notes translated by the Scientist over one or more raids. Translation needs only the unlocked knowledge, not the physical item, and is free.
- Unlocked knowledge opens things: codes for locked doors, marked stash locations, new quests, story missions, lore entries in a **journal** in the hub.
- Knowledge is never sold and never lost.

## 16. AI

### Human AI (slice)

One shared human AI system. Factions differ through presets: loadout tables, accuracy, aggression, courage, and which factions they are hostile to.

**States:** idle/patrol → investigate (heard or glimpsed something) → combat → retreat/heal → loot (looters especially).

**Combat behaviors:** take cover, peek, suppress, flank in pairs, throw grenades (post-slice), retreat when badly injured.

**Senses:**
- Vision: cone with distance; modified by the target's stance, movement, lighting and cover.
- Hearing: footsteps (louder when sprinting or on loud surfaces), gunshots (very far), searching containers, reloads at close range.

AI uses the same health, armor and weapon systems as the player. Full combat design (under-fire reaction, aim through the weapon model, personalities, squads, performance bands): `docs/superpowers/specs/ai/2026-10-25-ai-combat-v2-design.md`.

### Hostility matrix (slice)

| | Looters | Bandits | Allied PMC | Soviet PMC |
|---|---|---|---|---|
| Looters | Neutral | Hostile | Hostile | Hostile |
| Bandits | Hostile | Neutral | Hostile | Hostile |
| Allied PMC | Hostile | Hostile | Friendly | Hostile |
| Soviet PMC | Hostile | Hostile | Hostile | Friendly |

The player inherits the attitudes of the chosen side, adjusted by reputation (§5).

### Bosses

Named leaders with 2-3 escorts that follow them as a squad. Per map, a boss either holds a compound (a fixed area with high-tier loot) or roams a route. At most one boss per raid, spawned by chance **(tune)**; bosses respawn in later raids. A boss wears a signature loadout of rare gear and always carries a unique single-use key or keycard that opens a locked high-level loot spot on its map. A boss that kills the player carries the player's gear until killed (nemesis rules, §15).

### Zombies (post-slice)

Separate AI: melee, sound-driven, swarming. Attracted by gunfire. Spec TBD.

## 17. Audio

Audio carries much of the information the HUD does not.

- Footsteps vary by surface and movement type, and are audible to the player and the AI.
- Gunshots are distinct per weapon and change with distance (near/far variants).
- Mag checks, reloads and armor hits have clear sounds.

## 18. UI and HUD

- **In raid:** no ammo counter, no crosshair, no minimap, no compass. Status icons (bleeding, fracture, pain, overweight, hungry, thirsty) appear only when active. Brief indicators when stance or fire mode changes. Orientation comes from landmarks, the sun and extraction knowledge.
- **Health screen:** body silhouette with per-limb HP, opened from the inventory.
- **Hub:** menu screens for stash, traders, quests, insurance, character (skills, reputation).
- **Readability:** clear item icons; tooltips with weight, size and key stats.

## 19. Art and level design

### Art direction

Low-poly but attractive, in the spirit of BattleBit Remastered: simple geometry, mostly flat or lightly textured materials, strong lighting, fog and ambient occlusion doing the heavy lifting. Muted, desaturated palette of a ruined 1970 Germany, with alien elements standing out through color and shape.

### Placeholders

Godot primitives and free CC0 packs (Kenney, Quaternius) until custom art. Every external asset is recorded in `assets/CREDITS.md` with its source and license.

### Map 1 (slice)

- About 180 × 180 m, 20-minute raids, noon and night variants.
- **Theme: bunker entrance.** A fortified compound around the entrance to one of the German research bunkers: fences, checkpoint, guard towers, barracks, vehicle depot, and the bunker portal itself. Name: **Bunker Gate 7**.
- The main bunker door is sealed in the slice; opening it can become a story-mission hook later.
- Landmarks visible from anywhere (guard tower, bunker portal, a crashed vehicle, etc.) are important, since there is no compass or minimap.
- 3–4 extraction points; several spawn points.
- At least one multi-story building and varied cover.
- Deliberate restable surfaces: ledges, windows, low walls, sandbags.
- Loot spots with different value tiers.

## 20. Milestones

### Vertical slice

- **M0 — Setup:** Git repo with LFS, project, folders, GUT, static typing settings, input map, renderer benchmark (target: 60 fps, 1080p, lowest settings, 6 GB GPU) including a night-lighting test scene.
- **M1 — Player:** movement, stances, lean, stamina, weight tiers, camera rig, full-body placeholder with IK arms.
- **M2 — Weapons:** firing, projectiles, recoil, sway, ADS, resting, bipod, magazines, mag/chamber checks, reloads.
- **M3 — Damage:** hitboxes, per-limb health, status effects, meds (incl. surgery kit), armor tiers, energy and hydration, melee.
- **M4 — Inventory and loot:** grid inventory, equipment slots (incl. melee), containers, searching, secure container, food and drink.
- **M5 — Human AI:** perception, state machine, combat behaviors, faction presets, hostility matrix.
- **M6 — Raid loop:** map 1 blockout, spawns, timer, extraction, death/MIA outcome.
- **M7 — Hub:** stash, six traders (basic level), barter, insurance, 5–6 quests, single autosave.
- **M8 — Skills, reputation and signature systems:** seven skills, trader and faction reputation, friendly-fire rules, nemesis system, bosses with escorts and keys, persistent knowledge and journal, first Ewigstein delivery quest with containment canister and in-raid exposure.
- **M9 — Night raids:** night lighting for map 1, flashlights, night optics, AI vision in darkness.
- **M10 — Slice polish:** tuning, audio pass, bug fixing.

### After the slice

Zombies; grenades; malfunctions; full gunsmithing; medium maps; story missions; larger secure containers; opposite-side quartermaster unlock; more weapons and prototypes; custom art; aliens (late).

## 21. Open questions

- Minimum CPU for the performance target.
- Do night raids give better rewards?
- Paper maps as an in-raid item?
- Chance-based extraction points?
- Economy: prices, sell rates, insurance cost.
- Alien design.
