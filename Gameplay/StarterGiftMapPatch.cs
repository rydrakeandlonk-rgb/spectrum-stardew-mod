using StardewModdingAPI;
using StardewModdingAPI.Events;

namespace SpectrumStardewMod.Gameplay;

/// <summary>
/// Replaces the vanilla starting gift contents with one Cookie.
/// Stardew creates a separate protected gift box for each farmer.
/// </summary>
internal sealed class StarterGiftMapPatch
{
    // Stardew reads this map property when creating a new save.\n    private const string GiftProperty = "FarmHouseStarterGift";
    private const string GiftPropertyValue = "(O)223 1";

    internal StarterGiftMapPatch(IModHelper helper)
    {
        helper.Events.Content.AssetRequested += this.OnAssetRequested;
    }

    private void OnAssetRequested(object? sender, AssetRequestedEventArgs e)
    {
        if (!e.NameWithoutLocale.IsEquivalentTo("Maps/FarmHouse"))
            return;

        e.Edit(asset =>
        {
            asset.AsMap().Data.Properties[GiftProperty] = GiftPropertyValue;
        });
    }
}
