# Monster pool progression

## Selection model

The location groups are progression gates, not physical spawn restrictions. Once a group is unlocked, its eligible monsters join one global random pool and can be spawned in any location where the spawner finds a valid tile.

The unlocked groups are combined as a set, so a monster family listed in more than one group does not receive extra selection weight merely because multiple groups are unlocked.

Farm monsters are always eligible. They do not have a Combat Level unlock gate; the possibility of an early, unusually strong farm monster is intentional.

Progression eligibility and difficulty weighting are separate:
- Existing pool unlocks determine which monster entries may be selected.
- A permanent three-stage save milestone sets the threat target: before Mines floor 120, after Mines floor 120, and after Skull Cavern floor 100.
- Combat Level is a secondary adjustment within the current stage, not the main progression gate.
- Easy, Normal and Hard shift the target threat and weighting curve; they do not directly alter monster stats.
- The active generated Mines area uses `Game1.netWorldState.Value.MinesDifficulty`; Skull Cavern uses `SkullCavesDifficulty`. Each level (clamped to 0–2) raises the target by 12%, and only applies in its matching area. Quarry Mine and all other locations use 0.
- The threat score is read from the vanilla `Data/Monsters` asset: health, attack, defence/resilience, speed, experience, miss chance and glider behaviour contribute. The old catalogue health estimate is only a fallback when a data key is missing or the asset format changes. Dangerous variants use a 1.5x threat estimate; no vanilla monster stats are modified.

## Location groups and unlock conditions

| Pool | Unlock |
|---|---|
| Mines | Always |
| Dangerous Mines | First successful completion of Danger in the Deep |
| Quarry Mine | Golden Scythe obtained |
| Skull Cavern | Skull Key obtained |
| Dangerous Skull Cavern | First successful completion of Skull Cavern Invasion |
| Bug Lair | Always |
| Farm | Always |
| Volcano Dungeon | Ginger Island unlocked |

## Vanilla save checks

The progression reader uses existing save state:

| Progression | Vanilla value |
|---|---|
| Mines floor 120 reached | `Game1.player.deepestMineLevel >= 120` |
| Skull Cavern floor 100 story reached | reflection lookup of `Farmer.questsCompleted` for quest ID `30` (completed Cryptic Note story quest; the API was added in Stardew 1.6.16) |
| Danger in the Deep completed | `Game1.player.team.completedSpecialOrders.Contains("QiChallenge9")` |
| Skull Cavern Invasion completed | `Game1.player.team.completedSpecialOrders.Contains("QiChallenge10")` |
| Golden Scythe obtained | `Game1.player.mailReceived.Contains("gotGoldenScythe")` |
| Skull Key obtained | `Game1.player.hasSkullKey` |
| Ginger Island unlocked | `Game1.player.hasOrWillReceiveMail("willyBoatFixed")` |

The Skull Cavern milestone uses the completed Cryptic Note story quest (quest ID 30), read through reflection because the completed-quest collection is not available in every supported game reference. On older builds, it falls back to the permanent `qiCave` mail flag, which is set after the player consumes the Iridium Snake Milk reward. This avoids relying on whether the consumable is still in inventory. The Mines milestone uses the save's deepest regular-mine floor. The Qi challenges are repeatable Special Orders, so their permanent completion history is read from `completedSpecialOrders`, not the active quest list or `questsCompleted`. The Skull Key property is used instead of checking whether the door was opened.

## Efficiency

`MonsterProgressionSnapshot.Read` runs when a monster is selected, not from the update-tick loop. It performs a small number of local save-state lookups and does not create a new saved flag, database record, or background polling task.

Reading on selection means progression acquired during a running save can be recognised on the next spawn request. The current save's `Farmer` is the source of truth, so loading a different save naturally uses that save's progress.

## Exclusions

These entries must never enter the candidate pool:
- The following are excluded from the catalogue: Mummy, normal Armored Bug, Lava Lurk, Duggy and Magma Duggy. The exclusions apply to dangerous forms too, so all Duggy variants and both Mummy forms remain out. Dangerous Armored Bug is the one intentional exception: it is included in Dangerous Skull Cavern while normal Armored Bug remains excluded.
- Armored Bug (normal only). Dangerous Armored Bug is eligible in Dangerous Skull Cavern.
- Lava Lurk.
- Duggy, Duggy (dangerous), and Magma Duggy.

Rock Crab and Rock Crab (dangerous) remain eligible. Other Rock Crab-family variants such as Lava Crab, Iridium Crab, Truffle Crab, Stick Bug, and False Magma Cap are not excluded by this rule.

## Explicit variant catalogue

The catalogue now contains individual spawn definitions instead of one entry per reflected C# class. It maps the reference roster to vanilla constructors and post-construction setup. The Dangerous Mines pool includes the regular Lava Bat, as in the supplied roster; it does not add a separate Dangerous Lava Bat. All catalogue slime variants (Green, Blue/Frost Jelly, Red/Purple Sludge, Copper, Iron and Tiger) remain represented where applicable. Dangerous Copper and Dangerous Iron Slimes are explicit mod variants using the corresponding vanilla slime setup followed by the dangerous stat treatment. Skeleton Mage is included in Dangerous Mines, and Dangerous Armored Bug is included in Dangerous Skull Cavern.

- Bat subtypes use the vanilla integer constructor codes for Frost Bat, Lava Bat, Iridium Bat, Haunted Skull, Magma Sprite and Magma Sparker.
- Blue, red and purple slimes use the vanilla GreenSlime mine-level constructor paths; the friendly display labels identify the Wiki colour names (Frost Jelly and Sludge are the corresponding internal names).
- Copper and Iron Slimes use the one-argument GreenSlime constructor, then set their colour and load the matching vanilla monster data entry for stats and drops.
- Carbon Ghost, Putrid Ghost, Royal Serpent, Lava Crab, Iridium Crab, Truffle Crab, False Magma Cap and Shadow Sniper use their named string constructor arguments.
- Mutant Fly and Mutant Grub use the hard-mode boolean constructor option.
- Skeleton Mage uses the Skeleton mage option; Spider maps to Stardew's Leaper class.
- Armored Bug uses the vanilla Bug constructor's area type 121. This sets the isArmoredBug network flag and loads the Armored Bug texture; the dangerous form then receives the dangerous stat treatment and optional dangerous texture. The normal Armored Bug remains excluded.
- Stick Bug and Tiger Slime use their vanilla post-construction methods.
- Iridium Golem uses the RockGolem base constructor followed by the variant's vanilla name, data and stat setup.
- Dangerous forms are separate definitions. They apply one additional-difficulty stat step and load the corresponding `_dangerous` texture when available.

The dangerous stat step is deliberately fixed at one because these monsters can spawn in any location. Vanilla mine generation may use a different additional-difficulty amount based on floor and mine state. We should revisit the dangerous multiplier after observing the first in-game encounters and difficulty scaling.

## Validation status

A successful build proves the code compiles against the pinned Stardew Valley 1.6.15 reference assemblies. It does not prove every constructor behaves correctly in a live save. In-game validation must explicitly cover every named variant and post-construction setup; dangerous stats and optional textures; Copper/Iron Slime data and colour; Iridium Golem stats and sprite; all exclusions; random weighting and duplicate pool membership; and spawning from several map types.

Use `spectrum_monster_pool_status` to inspect progression stages, current-area difficulty, selectable definitions, vanilla threat-data coverage and any missing constructor/setup support. Any unresolved data keys are listed and use the catalogue fallback score.
