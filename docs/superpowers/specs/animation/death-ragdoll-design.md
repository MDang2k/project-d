# Death, ragdolls and bodies

Date: 2026-11-12

Status: approved design, not implemented. The "death and ragdoll" item the animation spec left open (`docs/superpowers/specs/animation/animation-system-design.md`, non-goals and open items). How characters die, what the player sees at their own death, and how bodies stay on the map for looting. Decisions from the brainstorm on 2026-11-12. Numbers marked **(tune)**. Choices not stated by the designer are marked **(Claude's call)**.

## Intent

- A kill reads physically: the body reacts to the shot or the blast that killed it and comes to rest where it falls.
- Bodies are places: a fight leaves bodies you can come back to and loot, for the whole raid.
- The player's own death is short and clear: you see what happened to you, then the result.

### Non-goals

- Death animation clips (designer decision: physics ragdoll).
- Gore, dismemberment.
- Bodies persisting between raids (the nemesis keeps your gear differently).

## 0. Current state

| Today | Where | In this design |
|---|---|---|
| A dead bot is replaced by a flat box-shaped loot container where it fell | `AiHuman.OnDied`, `CorpseSpawner.SpawnBody` | the body itself stays, ragdolls, settles and is the lootable container (section 1) |
| That container is freed after 180 s, gear and all | `CorpseSpawner` | removed; bodies stay for the raid with a cap (section 3) |
| Player death ends the raid at once and opens the result screen | `RaidManager`, `RaidSession.PlayerDied` | a 3 s third-person death camera first (section 2) |
| Shared rig, animated AI bodies (`AiBodyRig`), characters as overlapping rigid boxes (one bone each) | animation spec, asset pipeline | ragdolls are built on the same skeleton |

## 1. Ragdolls (designer decision: physics ragdoll)

- **Which bodies ragdoll (Claude's call):** characters with a box body on the shared rig. Today that is the player and looters; **Allied and Soviet PMC bots get the player body with their side's outfit** (`player_outfit_allied.glb`, `player_outfit_soviet.glb`) as part of this work. Factions with no body model yet (bandits, deserters) keep a placeholder: the capsule tips over as one rigid body and then becomes a box-shaped body container as today (without the despawn), until their outfits exist.
- **A separate body node (Claude's call):** at death the ragdoll moves to its own `Body` node (skeleton, outfit mesh, inventory), spawned from the dying character; the AI node itself is reset as today (hitboxes, nav agent, `AutoRespawn` in the AI range keep working on the original node). The animation modifiers (locomotion, two-bone IK, foot IK, hand poses) are not copied to the body node, so nothing fights the simulation.
- **Rig:** each character skeleton gets a `PhysicalBoneSimulator3D` (verified present in the 4.7.2 C# API, with `PhysicalBone3D` joint types and masses; how Jolt handles the joints is checked in the first build step) with one `PhysicalBone3D` per major bone (hips, spine, chest, head, upper arms, forearms, thighs, shins); hands and feet follow their parent **(Claude's call)**. Verify the node names in Godot 4.7.2.
- **Colliders:** one box per segment matching the body's rigid boxes (asset pipeline: overlapping rigid boxes, one bone each), sized from each box's mesh bounds, so the collider is the box itself; masses split roughly as a human body (torso heavy, limbs light) **(tune)**.
- **Joints:** cone and hinge limits per joint (knees and elbows hinge one way, neck and shoulders cones), so bodies never fold into impossible poses **(tune)**.
- **Collision:** ragdoll bones collide with the world and with other ragdolls, not with living characters (no tripping the player over a falling body) **(Claude's call)**. Bullets that hit a body make an impact (sound and puff) and a small push while it is still simulating, but deal no damage.
- **The killing hit pushes:** the impulse comes from the hit that killed: a bullet adds its momentum (velocity x a factor **(tune)**) at the bone it hit, so a rifle shot topples the body away from the shooter; a grenade blast adds a radial impulse from the burst on every bone, scaled by distance; a fall, bleed-out or exposure death adds none (the body just collapses). Prone deaths slump in place.
- **Hitboxes off:** at death the body's hitboxes, perception and AI stop; the body boxes and outfit shells follow the physical bones.
- **Settling:** the ragdoll simulates until every bone's speed stays below 0.1 m/s for 0.5 s, or 4 s pass **(tune)**. Then it **freezes**: the simulation stops, the pose is kept, the physical bones are removed, and only the posed mesh remains (no physics cost).
- **Gear on the body:** worn helmet, vest, rig and backpack meshes stay on the bones and fall with the body once such meshes exist (today only the placeholder backpack on AI is drawn; it follows its bone). The held weapon is released and falls as a loose model beside the body (visual only: it stays in the body's inventory, so looting the body gets it) **(Claude's call)**.
- **Lootable where it lies:** once frozen, the body becomes the lootable container (the existing `LootContainer` with the dead character's inventory), with its interaction point at the chest bone and a box collider sized to the torso added for the interaction raycast (the posed mesh itself has no collider). Before freezing it can already be searched if the player reaches it (the search UI waits for it to settle, about 1 s) **(Claude's call)**.
- **AI looters** find bodies as today (`loot_containers`, `IsCorpse`).
- **Hollowed rising dead** (Hollowed spec): a frozen body that rises is replaced by a Hollowed actor in the same pose and outfit, standing up with its own clip.

## 2. The player's death (designer decision: third-person death cam)

1. **The body ragdolls** like any other (section 1); the head mesh, hidden while alive, is shown.
2. **The camera leaves the head:** over 0.8 s it rises and pulls back to a point about 3 m behind and 2 m above the body, looking down at it (a short ray test keeps it out of walls and ceilings) **(tune)**.
3. **It turns toward where the killing hit came from:** during the next 2 s the camera yaws slowly toward the direction of the killing hit (the shot's direction, or the blast's centre), so the player sees which way death came from. The camera does not fly toward the killer, and nothing marks them: a killer in plain view of that camera may be seen, one behind cover is not **(Claude's call, keeping "information is earned")**.
4. **Sound and image:** sound muffles to a low heartbeat, colour slowly drains; after 3 s total the screen fades to black.
5. **Result screen:** the outcome as today, plus a kill line **(Claude's call)**: the killer is the one the nemesis attribution names (`RaidNemesis` hit-attribution window), so the line and the nemesis always agree; then the weapon type, the rough distance in tens of metres, and the body part of the hit that killed. If the last damage was not a hit (bleeding, exposure), the line says so after the killer: "Allied PMC, FAL-type rifle, about 80 m, thorax; bled out". With no attributed killer: "a mine", "the Hollowed", "bleeding out", "exposure". The line is display only; the saved nemesis record does not change.

- **Your gear during the cam:** the raid outcome strips the inventory at the moment of death as today (the nemesis takes it). The ragdoll keeps a visual snapshot of the worn gear and weapon until the fade, so nothing vanishes on screen. The player's body is not a lootable container (the raid is over) and does not count toward the cap.
- **Input:** none during the cam; a mouse click or the interact key skips straight to the fade (not Escape, which belongs to the pause menu).
- This replaces today's death presentation: `Player.StepDeath` lowering the eye and the 2.5 s wait before the result screen in `RaidLevel`.
- **No cam** for MIA (time ran out) or Abandon raid: straight to the result screen.
- The raid outcome is decided at the moment of death as today; the cam is presentation only.

## 3. Bodies on the map (designer decision: stay for the raid, with a cap)

- Bodies stay until the raid ends. The 180 s despawn goes.
- **Cap:** about **30 frozen bodies** per map **(tune)**. When a new body would pass the cap, one old body is removed, chosen in this order:
  1. the oldest **empty** body (looted bare) out of the player's sight;
  2. the oldest body of any kind out of the player's sight;
  3. if all are in sight, wait until one is not.
- Removal is a short fade (0.5 s) while out of sight, so the player never sees bodies vanish **(Claude's call)**.
- **Active ragdolls:** at most **6** simulating at once **(tune)**; a death beyond that skips the simulation and freezes at once into a slumped pose matching its stance (a few stored poses: on the back, on the side, face down, sitting against cover), so a grenade on a squad or a Hollowed swarm cannot spike the physics. The stored poses are built in code as bone rotations on the shared rig **(Claude's call)**; authored clips can replace them later.
- The player's own body is never removed while the death cam runs.

## 4. Data and code

- `RagdollConfig` resource (`data/animation/ragdoll.tres`): masses, joint limits, impulse factors (bullet, blast), settle thresholds, cap, active limit, death cam distances and timings.
- Pure logic with xUnit tests: `BodyCap` (eviction order, sight rule, wait), `SettleDetector` (speed threshold and time-out), `DeathImpulse` (bullet momentum and blast falloff to an impulse per bone), `DeathCamPath` (camera offsets and the yaw toward the hit direction, wall clamp), `KillLine` (faction, weapon, rounded distance, part text).
- Nodes: a `Ragdoll` component on character bodies (player and AI), a `BodyManager` in the raid scene (cap and fades), `DeathCam` on the player.
- The killing hit's data (direction, impulse, bone, attacker) is recorded on the `HealthComponent` when damage kills, so the ragdoll, the death cam and the kill line all read the same hit. Today `Died` carries only the body part and the attacker lives in `ProjectileManager`'s events, so the hit data is threaded through `ReceiveHit`, `ReceiveExplosion` and melee.

## 5. Build order (Claude's call)

1. Ragdoll rig on the player and AI bodies, killing-hit impulse, settling and freezing; the body becomes the lootable container where it lies; despawn removed.
2. Body cap, out-of-sight fades, active ragdoll limit with stored poses.
3. Player death cam, sound and fade, kill line on the result screen.
4. Grenade blast impulses; Hollowed rising from a frozen body (with the Hollowed work).

## 6. Playtest

- Shoot bots standing, crouched and prone from the front and side at close and long range; watch how they fall and where they come to rest (stairs, windows, slopes).
- Kill a squad with a grenade; check the frame rate and that bodies settle in believable places.
- Leave a fight, come back five minutes later and loot the bodies.
- Die to a bot at range and to a close shot; does the death cam tell you which way it came from without showing you where the killer hides?

## Decisions

1. Characters die as physics ragdolls on the shared rig, pushed by the killing hit; they settle, freeze and become lootable where they lie.
2. The player's death shows a 3 s third-person camera turning toward where the killing hit came from, then the result screen with a kill line.
3. Bodies stay for the whole raid, capped at about 30 per map; the oldest empty bodies out of sight fade first; at most 6 ragdolls simulate at once.

## Open items

- Joint limits and masses tuned on the real body model.
- Stored fallback poses (a few) authored on the rig.
