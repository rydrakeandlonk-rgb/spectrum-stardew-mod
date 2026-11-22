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
    private readonly IMonitor monitor;

    internal StarterGiftMapPatch(IModHelper helper, IMonitor monitor)
    {
        this.monitor = monitor;
        helper.Events.Content.AssetRequested += this.OnAssetRequested;
        this.monitor.Log(
            $"[Starter Gift] Patch registered. Target property: {GiftProperty}; replacement: {GiftPropertyValue}.",
            LogLevel.Info
        );
    }

    private void OnAssetRequested(object? sender, AssetRequestedEventArgs e)
    {
        string assetName = e.NameWithoutLocale.Name;
        bool isFarmMap =
            e.NameWithoutLocale.IsEquivalentTo("Maps/Farm") ||
            assetName.StartsWith("Maps/Farm_", StringComparison.OrdinalIgnoreCase);

        if (!isFarmMap)
            return;

        e.Edit(asset =>
        {
            var properties = asset.AsMap().Data.Properties;
            bool propertyExisted = properties.ContainsKey(GiftProperty);
            string originalValue = propertyExisted
                ? properties[GiftProperty]?.ToString() ?? "<null>"
                : "<missing>";

            properties[GiftProperty] = GiftPropertyValue;

            this.monitor.Log(
                $"[Starter Gift] Replaced {GiftProperty} on '{assetName}'. " +
                $"Original: '{originalValue}' (present: {propertyExisted}); " +
                $"new: '{GiftPropertyValue}'. " +
                (originalValue == GiftPropertyValue
                    ? "The map already contained the configured Cookie gift."
                    : "Farm map gift property successfully overridden."),
                LogLevel.Info
            );
        });
    }
}
