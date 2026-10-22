# Story part 5: names

Status: approved design (story bible, part 5 of 5). Final names chosen by the designer. Earlier specs use working names; this table is the mapping. Player-facing text uses the final names (through `GameText` keys); internal ids may keep old names where changing them would churn code and data.

## Names

| What | Final name | Replaces (working name in earlier specs) |
|---|---|---|
| Game title | **Ewigstein** | "Project D" (stays the repo and code codename) |
| The zone | **Das Sperrgebiet**, "the Sperr" to contractors | "the zone" |
| The 1965 disaster | **Event 65** | "the Cascade" |
| Zombies | **the Hollowed** | "zombies" |
| The imprisoned intelligence | **Der Gefangene** | "the Prisoner" |
| The alien jailers | **Die Wächter** | "the Wardens" |
| Late-Act-2 enemy faction | **Abteilung Ewig** | "Kommando Resonanz" |
| Ewigstein, by faction | Allied "Material HALCYON", Soviet "Izdeliye 7", scavenger slang "frostglass" | confirmed from the GDD |
| The hub | **Camp Lindenhof** | "the hub" |
| Map 1 | **Bunker Gate 7** | "Bunker Entrance", "map 1" (internal map id stays `map1`) |
| Currency | **marks** | "marks" (working name confirmed) |
| Other maps | Kreuzdorf, Hochwald, Border Strip, Rail Yard, Falkenberg base, The Labs, Cascade Site | kept as named in the world map spec |

Characters keep the names in part 3 (`docs/superpowers/specs/story/2026-10-22-story-characters-design.md`).

## Notes

- "Cascade Site" keeps its name although the disaster is now Event 65; the designer may rename it later.
- The "Resonanz" experiment and lab note keep their name; only the faction was renamed.
- Story missions referring to "the Voice" keep that in-world label (what the broadcasts are called before Der Gefangene is understood).
- The GDD is updated in the same commit (title, timeline, Hollowed, Sperrgebiet, map 1 name, Ewigstein faction names, resolved open questions).

## Story bible index

1. `docs/superpowers/specs/story/2026-10-21-story-core-truth-design.md` — the truth
2. `docs/superpowers/specs/story/2026-10-21-story-arc-missions-design.md` — acts, lock meter, story missions, Abteilung Ewig (written as Kommando Resonanz)
3. `docs/superpowers/specs/story/2026-10-22-story-characters-design.md` — characters
4. `docs/superpowers/specs/story/2026-10-22-story-endings-design.md` — endings
5. this file — names
