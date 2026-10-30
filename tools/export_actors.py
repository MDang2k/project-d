"""Builds data/actors/actors.json from the Actors and Modifier Sets sheets (run export_catalog.py first).

    python tools/export_actors.py

Rows are written as authored (only filled cells); inheritance from Parent is resolved by the game's ActorCatalog.
Fails on unknown parents, cycles, unknown stat ids, bad ops and unparseable numbers so mistakes stop at commit time.
"""
import csv
import glob
import json
import re
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from migrate_ai_presets_to_sheet import stat_registry  # noqa: E402

ROOT = Path(__file__).resolve().parent.parent
CATALOG = ROOT / "docs" / "design" / "catalog"
OUT = ROOT / "data" / "actors" / "actors.json"

LISTS = {"Tags", "Modifier sets", "Weapon choices", "Armor", "Body color", "Archetypes"}
PROPS = {"Weapon": "weapon", "Weapon choices": "weapon_choices", "Armor": "armor", "Heal item": "heal_item",
         "Rig": "rig", "Backpack": "backpack", "Pocket table": "pocket_table", "Loots corpses": "loots_corpses",
         "Body color": "body_color", "Archetypes": "archetypes"}
LINKS = {"Boss row": "boss_row", "Loadout faction": "loadout_faction", "Loadout tier": "loadout_tier"}
OPS = {"add", "pct", "min", "max", "set"}


def split(text):
    return [p.strip() for p in text.replace(",", ";").split(";") if p.strip()]


SLOTS = {"Primary", "Pistol", "Vest", "Helmet", "Rig", "Backpack"}


def game_ids():
    """Ids that have a resource under data/ (items/defs holds the item definitions, the rest the typed data)."""
    return {Path(f).stem for f in glob.glob(str(ROOT / "data" / "**" / "*.tres"), recursive=True)}


def build_loadouts():
    """Faction Loadouts rows the game can use today: Slice items with a resource, by catalog id or the
    'Exists as' alias in the item's Notes (akm -> ak_type). Result: {faction: {tier: {slot: [{item, weight}]}}}."""
    info = {}
    for f in glob.glob(str(CATALOG / "*.csv")):
        if Path(f).name in ("faction_loadouts.csv", "actors.csv", "modifier_sets.csv", "bosses.csv"):
            continue
        for r in csv.DictReader(open(f, newline="", encoding="utf-8")):
            if r.get("Id"):
                info[r["Id"]] = r
    have = game_ids()
    out, skipped = {}, 0
    with open(CATALOG / "faction_loadouts.csv", newline="", encoding="utf-8") as f:
        for r in csv.DictReader(f):
            if r["Slot"] not in SLOTS:
                continue
            row = info.get(r["Item id"])
            game = None
            if row and row.get("Phase") == "Slice":
                alias = re.search(r"Exists as '(\w+)'", row.get("Notes", ""))
                game = alias.group(1) if alias else r["Item id"]
            if game not in have:
                skipped += 1
                continue
            out.setdefault(r["Faction"], {}).setdefault(r["Tier"], {}).setdefault(r["Slot"], []).append(
                {"item": game, "weight": float(r["Weight"] or 1)})
    return out, skipped


def main():
    ids, _ = stat_registry()
    known = set(ids)
    errors = []
    actors = []
    with open(CATALOG / "actors.csv", newline="", encoding="utf-8") as f:
        for n, row in enumerate(csv.DictReader(f), start=2):
            row = {k: (v or "").strip() for k, v in row.items()}
            if not row["Id"]:
                continue
            a = {"id": row["Id"], "parent": row["Parent"], "name": row["Name"], "kind": row["Kind"].lower() or "actor",
                 "faction": row["Faction"], "tags": split(row["Tags"]), "sets": split(row["Modifier sets"]),
                 "props": {}, "stats": {}, "links": {}, "notes": row["Notes"]}
            if a["kind"] not in ("template", "actor"):
                errors.append(f"actors row {n} ({a['id']}): Kind must be template or actor")
            for col, key in PROPS.items():
                if row[col]:
                    a["props"][key] = split(row[col]) if col in LISTS else row[col]
            for col, key in LINKS.items():
                if row[col]:
                    a["links"][key] = row[col]
            for stat in ids:
                if row[stat]:
                    try:
                        a["stats"][stat] = float(row[stat])
                    except ValueError:
                        errors.append(f"actors row {n} ({a['id']}): {stat} is not a number: {row[stat]!r}")
            actors.append(a)

    by_id = {a["id"]: a for a in actors}
    if len(by_id) != len(actors):
        errors.append("duplicate actor ids")
    for a in actors:
        if a["parent"] and a["parent"] not in by_id:
            errors.append(f"{a['id']}: unknown parent {a['parent']!r}")
        seen, cur = set(), a
        while cur and cur["parent"]:
            if cur["id"] in seen:
                errors.append(f"{a['id']}: parent cycle")
                break
            seen.add(cur["id"])
            cur = by_id.get(cur["parent"])

    sets = {}
    with open(CATALOG / "modifier_sets.csv", newline="", encoding="utf-8") as f:
        for n, row in enumerate(csv.DictReader(f), start=2):
            row = {k: (v or "").strip() for k, v in row.items()}
            if not row["Set"]:
                continue
            if row["Stat"] not in known:
                errors.append(f"modifier_sets row {n}: unknown stat {row['Stat']!r}")
            if row["Op"].lower() not in OPS:
                errors.append(f"modifier_sets row {n}: unknown op {row['Op']!r}")
            try:
                value = float(row["Value"])
            except ValueError:
                errors.append(f"modifier_sets row {n}: Value is not a number: {row['Value']!r}")
                continue
            sets.setdefault(row["Set"], []).append({"stat": row["Stat"], "op": row["Op"].capitalize(), "value": value})
    # Bosses sheet: HP multiplier becomes boss_hp_<id>, a Pct modifier on the five limb HP stats (1.35 is +35%)
    with open(CATALOG / "bosses.csv", newline="", encoding="utf-8") as f:
        for row in csv.DictReader(f):
            try:
                mult = float(row["HP multiplier"])
            except (KeyError, ValueError):
                continue
            if mult != 1.0 and row["Boss id"].strip():
                sets["boss_hp_" + row["Boss id"].strip()] = [
                    {"stat": "health." + part, "op": "Pct", "value": round(mult - 1.0, 6)}
                    for part in ("head", "thorax", "stomach", "arm", "leg")]

    for a in actors:
        for s in a["sets"]:
            if s not in sets:
                errors.append(f"{a['id']}: unknown modifier set {s!r}")

    loadouts, skipped = build_loadouts()

    if errors:
        print("\n".join(errors), file=sys.stderr)
        sys.exit(1)
    OUT.parent.mkdir(parents=True, exist_ok=True)
    OUT.write_text(json.dumps({"actors": actors, "modifier_sets": sets, "loadouts": loadouts}, indent=1) + "\n", encoding="utf-8")
    rows = sum(len(items) for tiers in loadouts.values() for slots in tiers.values() for items in slots.values())
    print(f"wrote {len(actors)} actors, {len(sets)} modifier sets, {rows} loadout rows ({skipped} skipped: not Slice or no game resource) to {OUT.relative_to(ROOT)}")


if __name__ == "__main__":
    main()
