# Design specs

Grouped by category. Each spec names its status at the top (approved, implemented, or superseded).

| Folder | Contents |
|---|---|
| `m7-hub/` | M7 hub: persistence (7a), hub shell and stash (7b), economy and traders (7c), insurance (7d), quests (7e) |
| `m8-signature/` | M8 signature systems: skills (8a), reputation and friendly fire (8b), nemesis (8c), knowledge and journal (8d), Ewigstein (8e), bosses (8f) |
| `weapons/` | weapon modding (v1 superseded by v2) |
| `maps/` | raid map standard, world map and transits, map 1 requirements and revision, one spec per map |
| `story/` | story bible (spoilers): core truth, arc and missions, characters, endings, names |
| `factions/` | faction identities, loadout tiers, bosses, Die Troika; character visuals (player, traders, story and mission characters) |
| `ai/` | AI combat v2 (perception, under-fire reaction, aim through the gun, personalities, squads, performance) |
| `animation/` | animation system: layered graph, socket ADS and grip IK, spring recoil, wall tuck, interruptible reloads |
| `audio/` | audio: spatial cues, sound propagation through rooms and portals, ballistic sound, foley, mix |
| `world/` | world interaction: doors |
| `art/` | asset pipeline and art direction (used by the model-designer agent) |

New specs go into the matching folder, named `<topic>-design.md` with no date in the file name. The date goes on a `Date: YYYY-MM-DD` line under the title.
