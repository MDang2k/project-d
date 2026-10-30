"""One-off: write the four AiPreset resources (data/ai/*.tres) into the item catalog workbook as the
"Actors" and "Modifier Sets" sheets (composable actors spec, phase 2).

    python tools/migrate_ai_presets_to_sheet.py

The sheets are added by splicing the xlsx zip (openpyxl would drop the cached formula results of every other
sheet and the CSV export would then show formulas). Re-running replaces both sheets with a fresh migration, so run it only before the designer edits them.
Stat columns come from scripts/systems/actors/Stats.cs; human_base lists every stat at its default and each
actor fills only the cells that differ.
"""
import re
from pathlib import Path


ROOT = Path(__file__).resolve().parent.parent
XLSX = ROOT / "docs" / "design" / "item_catalog.xlsx"
STATS_CS = ROOT / "scripts" / "systems" / "actors" / "Stats.cs"

FACTION_NAMES = {0: "Looters", 1: "Bandits", 2: "Allied PMC", 3: "Soviet PMC"}
TAGS = {0: "faction.looters", 1: "faction.bandits", 2: "faction.allied_pmc", 3: "faction.soviet_pmc"}

# AiPreset field -> stat id
FIELD_TO_STAT = {
    "ViewDistance": "perception.view_distance",
    "FovDeg": "perception.fov_deg",
    "HearingMultiplier": "awareness.hearing_mult",
    "NoticeSeconds": "awareness.notice_s",
    "Accuracy": "aim.accuracy",
    "Courage": "tactics.courage",
    "Aggression": "tactics.aggression",
    "ArmorChance": "loadout.armor_chance",
    "BackpackChance": "carry.backpack_chance",
    "SpareMagazines": "carry.spare_magazines",
}


def stat_registry():
    """(ids in registry order, defaults) read from Stats.cs."""
    text = STATS_CS.read_text(encoding="utf-8")
    ids, by_name = [], {}
    group = None
    for line in text.splitlines():
        m = re.match(r"\s*public static class (\w+)", line)
        if m:
            group = m.group(1)
        m = re.match(r'\s*public const string (\w+) = "([a-z_]+\.[a-z0-9_]+)";', line)
        if m:
            ids.append(m.group(2))
            by_name[f"{group}.{m.group(1)}"] = m.group(2)
    defaults = {}
    for m in re.finditer(r"\b[FI]\((\w+\.\w+), (-?[0-9.]+)f", text):
        defaults[by_name[m.group(1)]] = float(m.group(2))
    return ids, defaults


def parse_tres(path):
    text = path.read_text(encoding="utf-8")
    ext = {m.group(2): m.group(1) for m in re.finditer(r'\[ext_resource [^\]]*path="([^"]+)" id="(\d+)"\]', text)}
    res = text.split("[resource]", 1)[1]
    values = {m.group(1): m.group(2).strip() for m in re.finditer(r"^(\w+) = (.*)$", res, re.M)}
    return ext, values


def resource_id(ext, ref):
    """Id of the resource an ExtResource("n") points at (its Id field, else the file stem)."""
    ref_id = re.search(r'ExtResource\("(\d+)"\)', ref).group(1)
    target = ROOT / ext[ref_id].replace("res://", "")
    _, values = parse_tres(target)
    ident = values.get("Id", "").strip('"')
    return ident or target.stem


def refs(ext, value):
    return [resource_id(ext, f'ExtResource("{n}")') for n in re.findall(r'ExtResource\("(\d+)"\)', value)
            if ext[n].endswith(".tres")]


def num(value):
    return int(float(value)) if float(value) == int(float(value)) and "." not in value else float(value)


def main():
    ids, defaults = stat_registry()
    columns = (["Id", "Parent", "Name", "Kind", "Faction", "Tags", "Modifier sets",
                "Weapon", "Weapon choices", "Armor", "Heal item", "Rig", "Backpack", "Pocket table",
                "Loots corpses", "Body color"]
               + ids + ["Boss row", "Loadout faction", "Loadout tier", "Notes"])
    rows = []
    base = {c: "" for c in columns}
    base.update({"Id": "human_base", "Name": "Human base", "Kind": "template", "Loots corpses": "FALSE",
                 "Notes": "Template: every stat at its default. Actors fill only the cells that differ; blank cells inherit."})
    for stat in ids:
        base[stat] = defaults[stat]
    rows.append(base)

    for name in ("looter", "bandit", "allied_pmc", "soviet_pmc"):
        ext, v = parse_tres(ROOT / "data" / "ai" / f"{name}.tres")
        row = {c: "" for c in columns}
        faction = int(v["Faction"])
        color = re.search(r"Color\(([^)]*)\)", v["BodyColor"]).group(1).split(",")[:3]
        row.update({
            "Id": name, "Parent": "human_base", "Name": name.replace("_", " ").title().replace("Pmc", "PMC"),
            "Kind": "actor", "Faction": FACTION_NAMES[faction], "Tags": TAGS[faction],
            "Weapon": (refs(ext, v["Weapon"]) or [""])[0] if "Weapon" in v else "",
            "Weapon choices": "; ".join(refs(ext, v["WeaponChoices"])) if "WeaponChoices" in v else "",
            "Armor": "; ".join(refs(ext, v["Armor"])) if "Armor" in v else "",
            "Heal item": (refs(ext, v["HealItem"]) or [""])[0] if "HealItem" in v else "",
            "Rig": v.get("RigId", '""').strip('"'),
            "Backpack": v.get("BackpackId", '""').strip('"'),
            "Pocket table": (refs(ext, v["PocketTable"]) or [""])[0] if "PocketTable" in v else "",
            "Loots corpses": v.get("LootsCorpses", "false").upper(),
            "Body color": ",".join(c.strip() for c in color),
            "Notes": "Migrated from data/ai/%s.tres." % name,
        })
        for field, stat in FIELD_TO_STAT.items():
            if field in v and float(v[field]) != defaults[stat]:
                row[stat] = float(v[field])
        rows.append(row)

    mod_rows = [
        ["veteran", "aim.accuracy", "Add", 0.1, "Example, tune: steadier shot."],
        ["veteran", "awareness.notice_s", "Pct", -0.15, "Example, tune: notices 15% faster."],
        ["veteran", "aim.moving_mult", "Pct", -0.1, "Example, tune: less spread while moving."],
        ["rookie", "aim.accuracy", "Add", -0.1, "Example, tune."],
        ["rookie", "awareness.notice_s", "Pct", 0.2, "Example, tune: notices 20% slower."],
    ]

    widths_actors = [34 if col in ("Notes", "Weapon choices", "Armor") else max(11, min(26, len(col) + 2)) for col in columns]
    table_actors = [columns] + [[r[c] if r[c] != "" else None for c in columns] for r in rows]
    table_mods = [["Set", "Stat", "Op", "Value", "Notes"]] + mod_rows
    splice(XLSX, {
        "Actors": (table_actors, widths_actors, 2),
        "Modifier Sets": (table_mods, [16, 28, 8, 9, 60], 2),
    })
    print(f"wrote {len(rows)} actors, {len(mod_rows)} modifiers, {len(ids)} stat columns")


# --- adding sheets without openpyxl: saving with it would drop the cached formula results of every other sheet ---

def col_letter(n):
    out = ""
    while n:
        n, r = divmod(n - 1, 26)
        out = chr(65 + r) + out
    return out


def sheet_xml(table, widths, freeze_cols, header_style):
    from xml.sax.saxutils import escape
    rows_xml = []
    for r_index, row in enumerate(table, start=1):
        cells = []
        for c_index, value in enumerate(row, start=1):
            ref = f"{col_letter(c_index)}{r_index}"
            style = f' s="{header_style}"' if r_index == 1 else ""
            if value is None:
                continue
            if isinstance(value, (int, float)) and not isinstance(value, bool):
                cells.append(f'<c r="{ref}"{style}><v>{value}</v></c>')
            else:
                cells.append(f'<c r="{ref}"{style} t="inlineStr"><is><t xml:space="preserve">{escape(str(value))}</t></is></c>')
        rows_xml.append(f'<row r="{r_index}">{"".join(cells)}</row>')
    cols = "".join(f'<col min="{i}" max="{i}" width="{w}" customWidth="1"/>' for i, w in enumerate(widths, start=1))
    top_left = f"{col_letter(freeze_cols + 1)}2"
    return ('<?xml version="1.0" encoding="UTF-8" standalone="yes"?>'
            '<worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">'
            '<sheetViews><sheetView workbookViewId="0">'
            f'<pane xSplit="{freeze_cols}" ySplit="1" topLeftCell="{top_left}" activePane="bottomRight" state="frozen"/>'
            '</sheetView></sheetViews>'
            f'<cols>{cols}</cols><sheetData>{"".join(rows_xml)}</sheetData></worksheet>')


def splice(path, sheets):
    """Add or replace worksheets in the xlsx zip, leaving every other part byte for byte."""
    import zipfile
    from xml.sax.saxutils import escape
    with zipfile.ZipFile(path) as z:
        parts = {n: z.read(n) for n in z.namelist()}
        infos = {i.filename: i for i in z.infolist()}
    wb = parts["xl/workbook.xml"].decode("utf-8")
    rels = parts["xl/_rels/workbook.xml.rels"].decode("utf-8")
    types = parts["[Content_Types].xml"].decode("utf-8")

    def target_of(name):
        m = re.search(rf'<sheet name="{re.escape(escape(name))}" sheetId="\d+"[^>]*r:id="(rId\d+)"', wb)
        if not m:
            return None
        t = re.search(rf'<Relationship Id="{m.group(1)}"[^>]*Target="([^"]+)"', rels).group(1)
        return "xl/" + t

    bosses = parts[target_of("Bosses")].decode("utf-8")
    header_style = re.search(r'<c r="A1"[^>]* s="(\d+)"', bosses).group(1)
    for name, (table, widths, freeze) in sheets.items():
        xml = sheet_xml(table, widths, freeze, header_style).encode("utf-8")
        existing = target_of(name)
        if existing:
            parts[existing] = xml
            continue
        n = 1 + max(int(m) for m in re.findall(r"xl/worksheets/sheet(\d+)\.xml", " ".join(parts)))
        sheet_id = 1 + max(int(m) for m in re.findall(r'sheetId="(\d+)"', wb))
        rid = f"rId{1 + max(int(m) for m in re.findall(r'Id=.rId(\d+)', rels))}"
        parts[f"xl/worksheets/sheet{n}.xml"] = xml
        wb = wb.replace("</sheets>", f'<sheet name="{escape(name)}" sheetId="{sheet_id}" state="visible" r:id="{rid}"/></sheets>')
        rels = rels.replace("</Relationships>", f'<Relationship Id="{rid}" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet{n}.xml"/></Relationships>')
        types = types.replace("</Types>", f'<Override PartName="/xl/worksheets/sheet{n}.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/></Types>')
    parts["xl/workbook.xml"], parts["xl/_rels/workbook.xml.rels"], parts["[Content_Types].xml"] = wb.encode("utf-8"), rels.encode("utf-8"), types.encode("utf-8")
    with zipfile.ZipFile(path, "w", zipfile.ZIP_DEFLATED) as out:
        for name, data in parts.items():
            out.writestr(name, data)


if __name__ == "__main__":
    main()
