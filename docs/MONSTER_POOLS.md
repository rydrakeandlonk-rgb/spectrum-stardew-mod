# Monster pool progression

## Selection model

The location groups are progression gates, not physical spawn restrictions. Once a group is unlocked, its eligible monsters join one global random pool and can be spawned in any location where the spawner finds a valid tile.

The unlocked groups are combined as a set, so a monster family listed in more than one group does not receive extra selection weight merely because multiple groups are unlocked.

Farm monsters are always eligible. They do not have a Combat Level unlock gate; the possibility of an early, unusually strong farm monster is intentional.

Progression eligibility and difficulty weighting are separate:
- Progression determines which monster entries may be selected.
- Difficulty weighting controls the relative chance of selecting an eligible entry.
- The scaling system must keep encounters completable and avoid forcing a save reset.

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
| Danger in the Deep completed | `Game1.player.team.completedSpecialOrders.Contains("QiChallenge9")` |
| Skull Cavern Invasion completed | `Game1.player.team.completedSpecialOrders.Contains("QiChallenge10")` |
| Golden Scythe obtained | `Game1.player.mailReceived.Contains("gotGoldenScythe")` |
| Skull Key obtained | `Game1.player.hasSkullKey` |
| Ginger Island unlocked | `Game1.player.hasOrWillReceiveMail("willyBoatFixed")` |

The Qi challenges are repeatable Special Orders, so their permanent completion history is read from `completedSpecialOrders`, not the active quest list or `questsCompleted`. The Skull Key property is used instead of checking whether the door was opened.

## Efficiency

`MonsterProgressionSnapshot.Read` runs when a monster is selected, not from the update-tick loop. It performs a small number of local save-state lookups and does not create a new saved flag, database record, or background polling task.

Reading on selection means progression acquired during a running save can be recognised on the next spawn request. The current save's `Farmer` is the source of truth, so loading a different save naturally uses that save's progress.

## Exclusions

These entries must never enter the candidate pool:
- Mummy and Mummy (dangerous).
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

Use `spectrum_monster_pool_status` to inspect unlocked groups, selectable definitions and any missing constructor/setup support.
