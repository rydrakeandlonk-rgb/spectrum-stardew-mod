# Starting Gift

Spectrum Stardew replaces the vanilla farmhouse starting gift with **one Cookie**.

The mod edits the FarmHouseStarterGift property to (O)223 1 on the farm map asset. Stardew reads this property from the farm map when it creates the protected farmhouse gift box for each farmer, including each farmer's own cabin in multiplayer. The patch covers the standard Maps/Farm asset and vanilla Maps/Farm_* variants.

There is no letter, mail queue, item identifier setting, test-send action, save-data entitlement, custom delivery state, or item eligibility validator. The item is a fixed constant in the mod, so the feature does not accept arbitrary item identifiers or perform item registry validation.

The property is used when Stardew creates the farmhouse/cabin gift box during save creation. Existing gift boxes already created in a save are not rewritten just because they remain unopened.

## Testing

- Create a brand-new single-player save and confirm the farmhouse gift contains one Cookie.
- Create a brand-new multiplayer save and confirm the host and each farmhand receive their own vanilla gift box containing one Cookie.
- Confirm the old Starter Gift Letter controls and letter actions are absent from Control Center.

The SMAPI mod is built and packaged through GitHub Actions. A successful build does not replace in-game and multiplayer validation.
