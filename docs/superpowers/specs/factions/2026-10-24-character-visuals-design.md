# Character visuals: player, traders, story characters, mission NPCs

Status: proposal written at the designer's request ("just propose the whole set"); not yet reviewed. Every choice here is Claude's call until the designer approves or changes it. Part 3 of the factions/characters content pass. Builds on the factions and bosses spec (`2026-10-23-factions-bosses-design.md`), the story characters and names specs (`docs/superpowers/specs/story/`), the asset pipeline spec (`docs/superpowers/specs/art/2026-10-21-asset-pipeline-art-direction-design.md`) and GDD §4 (menu hub) and §19 (art). Spoilers (Weiss, Albrecht). Numbers marked **(tune)**.

## Intent

- Every named character reads in one glance: one silhouette hook and one colour or prop nobody else has.
- Same low-poly look, palette and shared rig as everyone else; no character gets special shaders or hand-painted textures.
- Faces tell the story where words don't: the player learns things about traders by looking (pillar: information is earned).

## 1. The player

The silent contractor (story part 3). No face or body customisation; the side chosen in the hub sets the base outfit.

### Base outfit by side

| Part | Allied contractor | Soviet contractor |
|---|---|---|
| Jacket | olive M65-pattern field jacket, sleeves rolled once | olive-brown M69-pattern field jacket ("afghanka" cut), sleeves down |
| Trousers | olive OG-107-style, bloused into boots | olive-brown field trousers, tucked |
| Boots | black leather lace-up combat boots | brown lace-up boots |
| Hands | olive leather-palm gloves, cuffs at the wrist | bare hands, field watch on the left wrist |
| Side marker | **pale blue armband**, left upper arm | **red armband**, left upper arm |
| Head (hidden locally) | short hair, stubble, field cap when no helmet | short hair, stubble, pilotka-style cap when no helmet |

- Same armband as the AI PMCs of that side (factions spec), so the player is a commission contractor like them.
- The arms and sleeves are on screen all the time in first person, so sleeve colour, cuff and glove shape get the most care. The armband sits high enough on the left arm to show when the weapon is raised and the player looks slightly left, a quiet reminder of the side.
- The Allied gloves and the Soviet bare hand plus watch make the two sides feel different in first person even without armor.

### Worn gear on the body

- Helmet, vest, rig and backpack from the catalog are swappable meshes on the shared rig (pipeline budget 300-2,000 triangles each), each with an `Attach` marker at its parent bone.
- In first person the player sees the vest front and rig pouches when looking down, the backpack straps on the shoulders, and the helmet only as a shadow and in the shadow outline. Everything casts shadows.
- The head mesh and helmet are hidden for the local camera (CLAUDE.md); the helmet still casts a shadow.
- Gear that changes what the body looks like is visible on AI too: the same meshes are used by every faction, so a looter wearing a looted titanium vest looks like it.

### Where the player sees themself

- In raid: legs, torso, arms, worn gear, and the shadow.
- In the hub: the loadout screen stays 2D (7b). A small 3D preview of the character in the loadout screen is an open item, not part of this spec.

## 2. Traders (hub portraits)

The hub is menu-based (GDD §4); traders appear as portraits on their trade screens.

### Portrait method (Claude's call)

- Each trader is a low-poly **3D bust** made on the shared rig and palette, posed and rendered to a 512 x 512 **(tune)** PNG by the model designer. Same style as the game; one consistent pipeline; easy to re-render with a different expression later.
- Framing: chest up, three-quarter view facing screen left, eye line at the upper third, one warm key light and cool fill, a few props behind them hinting at their trade, slight fog falloff into the background.
- Files: source `assets/source/characters/portraits/<trader_id>.blend`, render `assets/textures/portraits/<trader_id>.png` (plus `<trader_id>_revealed.png` where section 2.7 applies).
- The bust is not a gameplay character and has no markers.

### 2.1 Captain Harold "Hal" Brennan, Allied quartermaster

- Mid-40s, receding sandy hair, heavy tired eyes, reading glasses on a cord.
- Khaki shirt with tie loosened under an open olive field jacket, captain's bars, pale blue armband.
- Clipboard and a dented enamel coffee mug; behind him crates with stencilled lot numbers and a field telephone.
- Hook: the glasses and coffee mug: a desk man in a war zone.

### 2.2 Kapitan Yelena Sorokina, Soviet quartermaster

- Mid-30s, dark hair in a tight bun, a dry half-smile, papirosa between two fingers.
- Olive-brown officer's tunic with shoulder boards, red armband, a ledger under her arm.
- Behind her, out of focus over her shoulder: her political minder, a man in a grey greatcoat and peaked cap, always watching. He never speaks on her screen; he is the pressure behind mission 5.
- Hook: the figure in the background.

### 2.3 Ferdinand "Ferdl" Hölzl, smuggler

- Mid-50s, stocky, red cheeks, broad grin with one gold tooth, waxed moustache.
- Loden jacket with horn buttons over a checked shirt, a felt hat with a tuft, a gold signet ring; several wristwatches on his forearm (his stock).
- Behind him: a truck tarp, jerrycans and a crate of mixed East and West goods.
- Hook: the hat and the watches. He shows no armband: he sells to everyone, including Abteilung Ewig (revealed late in Act 2).

### 2.4 Sister Agnes Wendt, medic

- Late 50s, lined calm face, grey hair under a white nurse's cap with a faded red cross.
- Grey wool cardigan over a white apron with old stains; her hands show pale frost scars from tending the Hollowed.
- Behind her: a Landklinik medicine shelf, brown glass bottles, an enamel kidney dish.
- Hook: the cap, the only clean white in the cast, and the scarred hands.

### 2.5 Otto Brandl, engineer

- Early 60s, big grey beard, heavy shoulders, a jeweller's loupe pushed up on his forehead.
- Oil-stained grey coveralls with a faded Falkenberg garrison patch, leather apron; missing the tip of his left index finger.
- Behind him: a workbench with a field-stripped rifle, a vice and hanging tools.
- Hook: the loupe and beard; the garrison patch ties him to Falkenberg and Colonel Haig.

### 2.6 "Dr. Emil Kranz", scientist (Act 1)

- Mid-60s, thin, wire round glasses, neatly combed white hair, ink-stained fingers.
- Tweed jacket over a cardigan and a **scarf always wrapped high around his neck**, even indoors.
- Behind him: stacked documents, a desk lamp, a magnifier over a page of German script.
- Hook: the scarf. It hides a frost scar from 1965 (section 2.7). Observant players may wonder about it.

### 2.7 Dr. Konrad Weiss (after mission 4)

- When mission 4 (Resonanz) is complete, his portrait switches to `scientist_revealed.png` and his name label to "Dr. Konrad Weiss".
- Same bust: the scarf is loosened and the pale frost scar across his neck and jaw shows; glasses in his hand, eyes down.
- The only trader whose portrait changes. Data: `TraderData` gains `Portrait` and an optional `RevealedPortrait` plus the story flag that swaps them (section 6).

## 3. Story antagonists

### Professor Dr. Heinrich Albrecht

- In 1965 about 65; now about 70. Gaunt, very pale, skin with fine frost crazing from five years in the deep bunkers; hairless scalp, deep-set eyes, thin hands.
- A stained 1965 Project Ewigstein lab coat worn over an Abteilung Ewig dark grey field coat, frost-ring insignia pinned on the coat collar. Never wears a gas mask.
- Non-combat: appears in story missions (mission 7 and the ending missions) as a 3D character on the shared rig, always surrounded by Abteilung Ewig soldiers.
- Hook: the white lab coat among the grey coats.
- Document photo: a 1965 staff ID photo (full head of grey hair, healthy) for the personnel roster (8d), so the player can compare and see what the bunkers did.

### Oberst Viktor Rauch

- Boss look from the Bosses sheet, expanded: mid-50s, tall, upright, close-cropped grey hair, a frost scar across the jaw from Event 65.
- Long grey coat, frost-ring collar insignia, Ewigstein-composite vest under the coat (matte pale crystalline inlay: no glow, no green), Gerät 69.
- **The only Abteilung Ewig soldier without a gas mask**: he does not need one, and the player notices.
- Document photo: 1965 security-detachment ID photo, no scar.

## 4. Bosses: silhouette hooks

Full looks are in the Bosses sheet (`Look` column). The one hook each boss is recognised by before any name appears:

| Boss | Hook |
|---|---|
| Kurt "Eisenhand" Vogt | steel prosthetic right hand, black-painted helmet, the cut-down MG |
| Old Mattes | doctor's bag on his hip, stooped walk |
| Die Jägerin | leafy shoulder cape, the long scoped G43 |
| Major Dale Harker | bandaged face, insignia torn off |
| Bogdan Král | fur coat, glint of gold teeth, coal-black hands |
| Colonel Walter Haig | spotless 1965 officer's uniform among ragged holdouts |
| Ernst Kolb | grey lab-security uniform and gas mask |
| Oberst Viktor Rauch | unmasked among masked Abteilung Ewig |
| Wolf | long coat, binoculars on his chest |
| Bär | biggest body in the game (scale 1.12 **(tune)**), titanium plates |
| Fuchs | scarf mask, Gen 0 NVG at night |

## 5. Mission characters

### The survey team (mission 2, The Silent Survey)

- Four-person 1970 joint geology survey team, found frozen at the Hochwald radar station.
- **Bright orange survey parkas**: the only saturated orange in the game, so they stand out under a torch at night; white hard hats, instrument cases, a theodolite.
- Bodies are rime-covered and frozen mid-motion (one still holding a headset to his ear). Lootable: the team logbook (frequency knowledge).

### The hollowed survivor (mission 3, The Kreuzdorf Ward)

- A former Landklinik patient strapped to a morgue gurney in a faded hospital gown with a patient wristband.
- Frost-rimed grey skin like the Hollowed, but **the eyes track the player** and the lips move (whispered numbers). Never attacks.
- Same mesh family as the Hollowed (post-slice) with a unique face; scripted, not an AI preset.

### Sorokina's minder

- Unnamed (writing pass). Grey greatcoat, peaked cap with a red band, round face, small eyes. Appears in Sorokina's portrait background and in mission 5.

## 6. Production

- **Rig:** every 3D character uses the shared game rig (pipeline section 4 bone names); outfits are swappable meshes (pipeline budget 6,000-10,000 triangles for body + outfit).
- **Order (Claude's call):** player body with both base outfits (seen every raid), then the six trader portraits (needed by the hub now), then boss models in map order, then Rauch, Albrecht and the mission characters.
- **Data:**
  - `TraderData`: `Portrait` (`Texture2D`), optional `RevealedPortrait` and `RevealFlag` (the story or quest id that swaps them; Weiss: mission 4 complete).
  - Player outfit: one outfit mesh per side, chosen from the profile's `Side`.
- **Rules carried over:** no real Nazi symbols (Abteilung Ewig uses the fictional frost ring); Ewigstein never glows and is never green; original work only.

## Decisions (Claude's calls, pending review)

1. Player base outfit per side: Allied M65 jacket, gloves and pale blue armband; Soviet M69 jacket, bare hands with watch and red armband. No customisation.
2. Worn gear meshes are shared by the player and every faction.
3. Trader portraits are rendered 3D busts in the game style, 512 x 512.
4. Weiss is the only trader whose portrait changes (scarf off, frost scar, after mission 4).
5. Rauch is the only unmasked Abteilung Ewig soldier; Albrecht wears his old lab coat.
6. Survey team in bright orange parkas; the hollowed survivor's eyes track the player.

## Open items

- 3D character preview in the hub loadout screen.
- Whether the Soviet watch could tell the raid time (no HUD); a gameplay idea, not decided here.
- Greeting lines and dialogue (writing pass).
- Die Wächter visuals (late, art pass).
