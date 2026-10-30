"""Adds a column to the Actors sheet for every stat in scripts/systems/actors/Stats.cs that has none yet.

    python tools/sync_actor_columns.py [stat.id ...]

Stat ids given on the command line also have their human_base cell reset to the registry default.

Run it after adding or removing a stat (a removed stat's column goes, unless an actor still sets it). Existing cells (the designer's edits) are kept; the new column is filled with the
default in the human_base template row and left blank in the others, so every actor inherits it. Stat columns
are put in registry order; other sheets of the workbook are not touched.
"""
import sys

from openpyxl import load_workbook

from migrate_ai_presets_to_sheet import XLSX, splice, stat_registry

FIRST = ["Id", "Parent", "Name", "Kind", "Faction", "Tags", "Modifier sets", "Weapon", "Weapon choices", "Armor",
         "Heal item", "Rig", "Backpack", "Pocket table", "Loots corpses", "Body color", "Archetypes"]
LAST = ["Boss row", "Loadout faction", "Loadout tier", "Notes"]


def main():
    reset = [a for a in sys.argv[1:] if not a.startswith("-")]  # stat ids whose human_base cell goes back to the registry default
    ids, defaults = stat_registry()
    sheet = load_workbook(XLSX, data_only=True)["Actors"]
    header = [c.value for c in sheet[1]]
    rows = [{h: c.value for h, c in zip(header, row)} for row in sheet.iter_rows(min_row=2)]
    rows = [r for r in rows if r.get("Id")]

    columns = FIRST + ids + LAST
    dropped = [h for h in header if h and h not in columns]
    for r in rows:
        for h in dropped:
            if r.get(h) not in (None, "") and r["Id"] != "human_base":
                raise SystemExit(f"column {h} left the registry but {r['Id']} still sets it: {r[h]}")
    added = [c for c in ids if c not in header]
    for r in rows:
        if r["Id"] == "human_base":
            for c in added + reset:
                r[c] = defaults[c]
    table = [columns] + [[r.get(c) for c in columns] for r in rows]
    widths = [34 if col in ("Notes", "Weapon choices", "Armor") else max(11, min(26, len(col) + 2)) for col in columns]
    splice(XLSX, {"Actors": (table, widths, 2)})
    print(f"Actors: {len(rows)} rows, new columns: {', '.join(added) or '-'}; dropped: {', '.join(dropped) or '-'}")


if __name__ == "__main__":
    main()
