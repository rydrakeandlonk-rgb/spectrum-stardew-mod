#!/usr/bin/env python3
"""Static checks for the Stardew monster catalogue and its progression pools."""

from pathlib import Path
import re
import sys

ROOT = Path(__file__).resolve().parents[2]
CATALOG_PATH = ROOT / "Gameplay" / "MonsterCatalog.cs"
POOLS_PATH = ROOT / "Gameplay" / "MonsterLocationPools.cs"

catalog = CATALOG_PATH.read_text(encoding="utf-8")
pools_source = POOLS_PATH.read_text(encoding="utf-8")

definitions = re.findall(r'\bC\("([^"]+)",\s*"([^"]+)"', catalog)
ids = [monster_id for monster_id, _ in definitions]
names = {monster_id: display_name for monster_id, display_name in definitions}

pool_map = {}
for match in re.finditer(
    r'\[MonsterPool\.([A-Za-z]+)\]\s*=\s*new\[\]\s*\{([^}]*)\}',
    pools_source,
    re.DOTALL,
):
    pool_name, body = match.groups()
    pool_map[pool_name] = re.findall(r'"([^"]+)"', body)

errors = []

if len(ids) != len(set(ids)):
    duplicates = sorted({value for value in ids if ids.count(value) > 1})
    errors.append(f"Duplicate catalogue IDs: {', '.join(duplicates)}")

pool_ids = {monster_id for values in pool_map.values() for monster_id in values}
catalogue_ids = set(ids)

for monster_id in sorted(catalogue_ids - pool_ids):
    errors.append(f"Catalogue definition has no pool: {monster_id}")
for monster_id in sorted(pool_ids - catalogue_ids):
    errors.append(f"Pool references missing catalogue definition: {monster_id}")

required_assignments_block = re.search(
    r'RequiredPoolAssignments\s*=\s*\{(.*?)\n\s*\};',
    catalog,
    re.DOTALL,
)
if not required_assignments_block:
    errors.append("RequiredPoolAssignments guard is missing.")
else:
    for monster_id, pool_name in re.findall(
        r'\("([^"]+)",\s*MonsterPool\.([A-Za-z]+)\)',
        required_assignments_block.group(1),
    ):
        if monster_id not in pool_map.get(pool_name, []):
            errors.append(f"Required definition {monster_id} is missing from {pool_name}.")

forbidden_assignments_block = re.search(
    r'ForbiddenPoolAssignments\s*=\s*\{(.*?)\n\s*\};',
    catalog,
    re.DOTALL,
)
if forbidden_assignments_block:
    for monster_id, pool_name in re.findall(
        r'\("([^"]+)",\s*MonsterPool\.([A-Za-z]+)\)',
        forbidden_assignments_block.group(1),
    ):
        if monster_id in pool_map.get(pool_name, []):
            errors.append(f"Definition {monster_id} is incorrectly assigned to {pool_name}.")

excluded_block = re.search(
    r'ExcludedNames\s*=\s*new\([^)]*\)\s*\{(.*?)\};',
    catalog,
    re.DOTALL,
)
if not excluded_block:
    errors.append("ExcludedNames guard is missing.")
else:
    excluded = set(re.findall(r'"([^"]+)"', excluded_block.group(1)))
    if not {"Mummy", "Armored Bug", "Lava Lurk", "Duggy", "Magma Duggy"}.issubset(excluded):
        errors.append("One or more required exclusions are missing.")

for monster_id, display_name in definitions:
    base_name = display_name.removeprefix("Dangerous ")
    if base_name in {"Mummy", "Lava Lurk", "Duggy", "Magma Duggy"}:
        errors.append(f"Excluded monster entered catalogue: {display_name}")
    if base_name == "Armored Bug" and monster_id != "dangerous-armored-bug":
        errors.append(f"Only Dangerous Armored Bug is allowed, found: {display_name}")

if "dangerous-armored-bug" not in catalogue_ids:
    errors.append("Dangerous Armored Bug is missing.")
if "skeleton-mage" not in catalogue_ids:
    errors.append("Skeleton Mage is missing.")

# Copper and Iron Slimes are custom visual variants built on GreenSlime's
# initialized vanilla stats. Their custom display names must never be passed
# to parseMonsterInfo, because neither name exists in Data/Monsters.
named_slime_start = catalog.find("private static void ConfigureNamedSlime(")
named_slime_end = catalog.find("\n    }", named_slime_start)
if named_slime_start < 0 or named_slime_end < 0:
    errors.append("ConfigureNamedSlime implementation is missing.")
else:
    named_slime = catalog[named_slime_start:named_slime_end]
    if "ParseMonsterInfo" in named_slime or "parseMonsterInfo" in named_slime:
        errors.append("Custom Copper/Iron Slimes must not parse missing Data/Monsters keys.")

if errors:
    print("Monster catalogue validation failed:")
    for error in errors:
        print(f" - {error}")
    sys.exit(1)

print(
    "Monster catalogue validation passed: "
    f"{len(ids)} unique definitions across {len(pool_map)} pools "
    f"({len(pool_ids)} unique pool references)."
)
