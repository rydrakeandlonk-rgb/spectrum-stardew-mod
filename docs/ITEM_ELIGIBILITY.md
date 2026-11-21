# Item eligibility policy

**Owner:** SMAPI mod (`Gameplay/ItemEligibilityValidator.cs`)
**Current policy version:** 1
**Status:** Validator foundation; no delivery flow is implemented by this component.

## Purpose

All item identifiers submitted to a future gift feature must pass through one game-side policy gate. The game registry resolves identifiers; the validator inspects the resolved item rather than trusting the text supplied by a UI or remote service.

The validator does not deliver items, change inventory, edit mail, or mutate save data.

## Current eligibility policy

- Only standard Stardew object-type items are eligible.
- Unresolved identifiers are rejected.
- Empty identifiers are rejected.
- Registry exceptions and metadata/classification errors fail closed.
- Any resolved item outside the allowed object type is rejected.
- The validator returns a stable rule ID and reason for diagnostics.
- The rules version is incremented whenever eligibility policy changes.

The current allowlist is intentionally narrow. A new Stardew or modded item type is not eligible merely because it exists in the registry.

## In-game verification

Load a save as the multiplayer host and run:

```text
spectrum_item_validator_test
```

The command exercises the live Stardew item registry and validator. It does not add anything to the inventory, mailbox, or world.

The suite checks a known vanilla object, empty and unresolved identifiers, and an optional registered non-object fixture. If that fixture ID is unavailable in a future game version, the test reports a skip instead of a false failure.

This is an in-game integration smoke test, not a substitute for testing a future delivery flow.

## Adding or changing rules

Keep all policy rules in the Rules collection in ItemEligibilityValidator.cs.

For each new rule:
1. Give it a stable, descriptive ID.
2. Make the predicate inspect resolved item metadata, not the raw identifier string.
3. Fail closed if the relevant metadata cannot be read.
4. Add or update an in-game test case.
5. Increment RulesVersion.
6. Update this document with the reason and compatibility notes.

Avoid broad text matching as the primary classifier. Qualified IDs, numeric aliases, and mod-provided IDs can refer to the same resolved item.

## Stardew 1.7 maintenance checklist

When the 1.7 API or content data is available:

1. Review official SMAPI and Stardew API changes affecting ItemRegistry, Item, and TypeDefinitionId.
2. Confirm the registry creation signature and unresolved-item behaviour against the new reference assemblies.
3. Review all item type identifiers and any newly introduced item categories.
4. Confirm the current object-type allowlist still identifies only the intended category.
5. Review modded/content-pack compatibility and ensure unknown types remain rejected.
6. Update the optional test fixture if its qualified ID changes.
7. Run spectrum_item_validator_test on a clean 1.7 test save with and without representative content packs.
8. Build/package the mod and inspect SMAPI logs for unexpected resolution or classification errors.
9. Increment the policy version and record the compatibility review in the changelog.

Do not enable a newly introduced type until its classification and intended behaviour have been reviewed.

## Scope boundary

This file is only the eligibility policy and its diagnostics. Configuration, delivery timing, save flags, test delivery actions, and multiplayer delivery are separate work and must not be inferred to exist from a successful validator build.