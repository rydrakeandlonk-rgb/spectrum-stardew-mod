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
- Armored Bug and Armored Bug (dangerous).
- Lava Lurk.
- Duggy, Duggy (dangerous), and Magma Duggy.

Rock Crab and Rock Crab (dangerous) remain eligible. Other Rock Crab-family variants such as Lava Crab, Iridium Crab, Truffle Crab, Stick Bug, and False Magma Cap are not excluded by this rule.

## Variant implementation status

The first progression layer now groups vanilla monster *class families* and gates those groups using the save checks above. The existing runtime catalogue still discovers only classes with a public `Vector2` constructor, so it does not yet represent every named monster variant in the roster.

The next implementation step is an explicit variant-aware spawn definition. It must cover:
- Named variants which use additional constructor arguments (for example Carbon Ghost, Putrid Ghost, Royal Serpent, Iridium Crab, and Truffle Crab).
- Variants which use a different constructor overload or setup method (for example Skeleton Mage, Stick Bug, Tiger Slime, and the Bat subtypes).
- Dangerous variants, which Stardew creates by applying `BuffForAdditionalDifficulty` and loading a matching `_dangerous` texture where available.
- Types whose vanilla constructor requires a location or mine-area argument.

Do not treat the roster as complete until every non-excluded entry has an explicit spawn definition and has been validated in-game. Use `spectrum_monster_pool_status` to inspect unlocked groups, currently selectable discovered classes, and class families still missing from the simple constructor catalogue.
