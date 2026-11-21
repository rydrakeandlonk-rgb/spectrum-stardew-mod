# Starting Gift

Spectrum Stardew replaces the vanilla farmhouse starting gift with **one Cookie**.

The mod edits the vanilla `FarmHouseStarterGift` map property to `(O)223 1` when Stardew requests the farmhouse map. Stardew creates and owns the starting gift box for each farmer, including the farmer's own cabin in multiplayer.

There is no letter, mail queue, item identifier setting, test-send action, save-data entitlement, custom delivery state, or item eligibility validator. The item is a fixed constant in the mod, so the feature does not accept arbitrary item identifiers or perform item registry validation.

The property is applied when the game creates the farmhouse/cabin gift for a new save. Existing gift boxes already created in an existing save are not rewritten.

## Testing

- Create a new single-player save and confirm the farmhouse gift contains one Cookie.
- Create a new multiplayer save and confirm the host and each farmhand receive their own vanilla gift box containing one Cookie.
- Confirm the old Starter Gift Letter controls and letter actions are absent from Control Center.

The SMAPI mod is built and packaged through GitHub Actions. A successful build does not replace in-game and multiplayer validation.
