#!/usr/bin/env python3
"""Static checks for the Stardew monster catalogue and its progression pools."""

from pathlib import Path
import re
import sys

ROOT = Path(__file__).resolve().parents[2]
CATALOG_PATH = ROOT / "Gameplay" / "MonsterCatalog.cs"
POOLS_PATH = ROOT / "Gameplay" / "MonsterLocationPools.cs"
PROGRESSION_PATH = ROOT / "Gameplay" / "MonsterProgression.cs"
DIAGNOSTIC_PATH = ROOT / "Gameplay" / "PoolDiagnostic.cs"
ENTRY_PATH = ROOT / "ModEntry.cs"
SPAWNER_PATH = ROOT / "Gameplay" / "MonsterSpawner.cs"

catalog = CATALOG_PATH.read_text(encoding="utf-8")
pools_source = POOLS_PATH.read_text(encoding="utf-8")
progression_source = PROGRESSION_PATH.read_text(encoding="utf-8")
diagnostic_source = DIAGNOSTIC_PATH.read_text(encoding="utf-8")
entry_source = ENTRY_PATH.read_text(encoding="utf-8")
spawner_source = SPAWNER_PATH.read_text(encoding="utf-8")

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
    if base_name == "Armored Bug":
        errors.append(f"Excluded monster entered catalogue: {display_name}")

if "dangerous-armored-bug" in catalogue_ids:
    errors.append("Dangerous Armored Bug must remain excluded.")
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

# Progression test profiles must be temporary and must never persist to save data.
for preset in (
    '"early"', '"mines"', '"skull100"', '"danger-mines"', '"quarry"',
    '"skull-cavern"', '"danger-skull"', '"bug-lair"', '"volcano"',
    '"combat-0"', '"combat-5"', '"combat-10"', '"shrine-0"', '"shrine-1"', '"shrine-2"', '"all"'
):
    if preset not in progression_source:
        errors.append(f"Progression test preset is missing: {preset}")
if "ClearTestOverride" not in progression_source or "testOverride ?? ReadActual(farmer)" not in progression_source:
    errors.append("Progression test override/reset path is missing.")
if "WriteGlobalData" in progression_source or "WriteSaveData" in progression_source:
    errors.append("Progression test simulation must not write to save data.")

for command in (
    '"spectrum_progression_test"',
    '"spectrum_spawn_monster_force_test"',
    '"spectrum_monster_catalog_test"'
):
    if command not in entry_source:
        errors.append(f"Developer validation command is missing: {command}")
if "ignoreProgression" not in spawner_source:
    errors.append("Progression-bypass spawn test path is missing.")
if "useTestProgression: false" not in spawner_source:
    errors.append("Live redemption path must explicitly ignore local progression test overrides.")
for live_default in (
    "MonsterCatalog.GetRandomType(command.Difficulty);",
    "MonsterCatalog.TryGetCanonicalType(command.UserInput, out candidateId)",
    "MonsterCatalog.TryGetCanonicalType(command.MonsterType, out candidateId)"
):
    if live_default in spawner_source:
        errors.append(f"Live redemption path accidentally uses test progression defaults: {live_default}")

# Live pool overrides must flow through every random, configured and horde selection path.
for required in ('"force_enabled"', '"force_disabled"', "GetDiagnostics("):
    if required not in pools_source:
        errors.append(f"Spawn pool override support is missing: {required}")
for required in ("ExpectedEnabled", "EffectiveEnabled"):
    if required not in diagnostic_source:
        errors.append(f"Spawn pool diagnostic fields are missing: {required}")
for line in spawner_source.splitlines():
    if "MonsterCatalog.GetRandomType(command.Difficulty" in line and "poolOverrides: this.poolOverrides" not in line:
        errors.append("A live random-monster selection bypasses the session pool overrides.")
    if "MonsterCatalog.TryGetCanonicalType(command." in line and "poolOverrides: this.poolOverrides" not in line:
        errors.append("A live configured-monster selection bypasses the session pool overrides.")
if "GetRandomDistinctTypes(safeHordeCount" in spawner_source and "poolOverrides: this.poolOverrides" not in spawner_source:
    errors.append("Horde selection bypasses the session pool overrides.")
if "TryCreateMonster(selectedId" in spawner_source and "poolOverrides: this.poolOverrides" not in spawner_source:
    errors.append("Monster construction does not revalidate the session pool overrides.")

# Multiplayer relay identity must keep the host session separate from the Twitch broadcaster.
if "HostSessionId" not in entry_source or "message.HostSessionId != this.gameSessionId" not in entry_source:
    errors.append("Forwarded multiplayer actions are not validated against the active host session.")
if "SendSessionContextAsync" not in entry_source:
    errors.append("Farmhand host-session association is missing.")
client_path = ROOT / "Networking" / "GameModsClient.cs"
client_source = client_path.read_text(encoding="utf-8")
if "session_id" not in client_source or "spawn.pools.state" not in client_source:
    errors.append("Gateway session identity and pool override sync are missing.")
if "SendPoolDiagnosticsAsync" not in entry_source:
    errors.append("Host progression/pool diagnostics are not reported to Control Center.")

# Custom variants must use an existing vanilla threat-data key, not their display name.
for expected in (
    'return "Green Slime";',
    'return "Fly";',
    'return "Grub";'
):
    if expected not in catalog:
        errors.append(f"Vanilla threat-data fallback mapping is missing: {expected}")

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
