using StardewModdingAPI;
using StardewModdingAPI.Events;

namespace SpectrumStardewMod.Gameplay;

/// <summary>
/// Replaces the vanilla starting gift contents with one Cookie.
/// Stardew reads this property from the farm map when it creates each
/// farmer's protected farmhouse gift box.
/// </summary>
internal sealed class StarterGiftMapPatch
{
    private const string GiftProperty = "FarmHouseStarterGift";
    private const string GiftPropertyValue = "(O)223 1";

    internal StarterGiftMapPatch(IModHelper helper)
    {
        helper.Events.Content.AssetRequested += this.OnAssetRequested;
    }

    private void OnAssetRequested(object? sender, AssetRequestedEventArgs e)
    {
        string assetName = e.NameWithoutLocale.Name;
        bool isFarmMap =
            e.NameWithoutLocale.IsEquivalentTo("Maps/Farm") ||
            assetName.StartsWith("Maps/Farm_", StringComparison.OrdinalIgnoreCase);

        // Stardew reads FarmHouseStarterGift from the farm map, not the
        // farmhouse interior map. Farm_* also covers vanilla farm variants.
        if (!isFarmMap)
            return;

        e.Edit(asset =>
        {
            asset.AsMap().Data.Properties[GiftProperty] = GiftPropertyValue;
        });
    }
}
