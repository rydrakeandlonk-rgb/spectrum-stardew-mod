using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Objects;
using SpectrumStardewMod.Gameplay;

namespace SpectrumStardewMod.Gameplay;

/// <summary>
/// Queues host-only, save-scoped starter letters through Stardew's native mail system.
/// Letter content is snapshotted in save data before it is queued, so later Control
/// Center edits cannot change mail already waiting to be delivered.
/// </summary>
internal sealed class StarterGiftService
{
    private const string SaveDataKey = "spectrum-starter-gift-v1";
    private const string PermanentMailId = "Spectrum_StarterGift";
    private const string FallbackItemId = "(O)223"; // Cookie
    private const int MaxLetterHistory = 20;
    private readonly IModHelper helper;
    private readonly IMonitor monitor;
    private StarterGiftSaveData saveData = new();

    internal StarterGiftService(IModHelper helper, IMonitor monitor)
    {
        this.helper = helper;
        this.monitor = monitor;
        helper.Events.Content.AssetRequested += this.OnAssetRequested;
    }

    internal void LoadSaveData()
    {
        this.saveData = this.helper.Data.ReadSaveData<StarterGiftSaveData>(SaveDataKey) ?? new StarterGiftSaveData();
        this.PruneDeliveredTestLetters();
        this.helper.GameContent.InvalidateCache("Data/mail");
    }

    internal void ClearSaveData()
    {
        this.saveData = new StarterGiftSaveData();
        this.helper.GameContent.InvalidateCache("Data/mail");
    }

    internal bool QueueLetter(string itemIdentifier, string title, string message, bool test, out string detail)
    {
        detail = "";
        if (!Context.IsWorldReady || !Context.IsMainPlayer || Game1.player is null)
        {
            detail = "Only the loaded save's host can queue starter mail.";
            return false;
        }

        if (!test && (this.saveData.PermanentGiftQueued || Game1.player.hasOrWillReceiveMail(PermanentMailId)))
        {
            detail = "The permanent starter gift has already been queued or received for this save.";
            return false;
        }

        if (test && DateTime.UtcNow.Ticks - this.saveData.LastTestAtUtcTicks < TimeSpan.FromSeconds(20).Ticks)
        {
            detail = "Please wait before sending another test letter.";
            return false;
        }

        ItemEligibilityValidator.ValidationResult validation = ItemEligibilityValidator.Validate(itemIdentifier);
        StardewValley.Object? eligibleObject = validation.ResolvedItem as StardewValley.Object;
        if (!validation.IsEligible || eligibleObject is null)
        {
            string rejectedRule = validation.RuleId;
            ItemEligibilityValidator.ValidationResult fallback = ItemEligibilityValidator.Validate(FallbackItemId);
            eligibleObject = fallback.ResolvedItem as StardewValley.Object;
            if (!fallback.IsEligible || eligibleObject is null)
            {
                detail = "The configured item was ineligible and the fallback cookie could not be validated.";
                return false;
            }
            this.monitor.Log($"Starter gift item rejected ({rejectedRule}); using the fallback cookie.", LogLevel.Warn);
        }

        string mailId = test ? $"Spectrum_StarterGift_Test_{Guid.NewGuid():N}" : PermanentMailId;
        string safeTitle = Sanitize(title, 80, "A gift for you");
        string safeMessage = Sanitize(message, 500, "A little something to get you started.");
        string itemToken = $" %item object {eligibleObject.ItemId} 1 %%";
        string mailText = $"{safeMessage}^{itemToken}[#]{safeTitle}";

        this.saveData.Letters[mailId] = mailText;
        if (test)
            this.saveData.LastTestAtUtcTicks = DateTime.UtcNow.Ticks;
        else
            this.saveData.PermanentGiftQueued = true;

        this.TrimLetterHistory();
        this.helper.Data.WriteSaveData(SaveDataKey, this.saveData);
        this.helper.GameContent.InvalidateCache("Data/mail");
        Game1.addMailForTomorrow(mailId);

        detail = test
            ? $"Test letter queued with {eligibleObject.DisplayName}."
            : $"Permanent starter gift queued with {eligibleObject.DisplayName}.";
        return true;
    }

    private void OnAssetRequested(object? sender, AssetRequestedEventArgs e)
    {
        if (!e.NameWithoutLocale.IsEquivalentTo("Data/mail"))
            return;

        e.Edit(asset =>
        {
            var data = asset.AsDictionary<string, string>().Data;
            foreach (var entry in this.saveData.Letters)
                data[entry.Key] = entry.Value;
        });
    }

    private void PruneDeliveredTestLetters()
    {
        // Keep queued/unread test letters available in Data/mail across reloads.
        // Only remove old test entries which are already in the save's received flags.
        if (!Context.IsWorldReady || Game1.player is null || this.saveData.Letters.Count <= 20)
            return;
        foreach (string id in this.saveData.Letters.Keys
            .Where(id => id.StartsWith("Spectrum_StarterGift_Test_", StringComparison.Ordinal))
            .Where(id => Game1.player.mailReceived.Contains(id))
            .Take(Math.Max(0, this.saveData.Letters.Count - 20))
            .ToArray())
        {
            this.saveData.Letters.Remove(id);
        }
        this.helper.Data.WriteSaveData(SaveDataKey, this.saveData);
    }

    private void TrimLetterHistory()
    {
        while (this.saveData.Letters.Count > MaxLetterHistory)
        {
            string? oldestTest = this.saveData.Letters.Keys.FirstOrDefault(id =>
                id.StartsWith("Spectrum_StarterGift_Test_", StringComparison.Ordinal));
            if (oldestTest is null)
                break;
            this.saveData.Letters.Remove(oldestTest);
        }
    }

    private static string Sanitize(string? value, int maxLength, string fallback)
    {
        string cleaned = new string((value ?? string.Empty)
            .Where(ch => !char.IsControl(ch) || ch is '\n' or '\r')
            .ToArray())
            .Replace("%", string.Empty, StringComparison.Ordinal)
            .Replace("#", "-", StringComparison.Ordinal)
            .Replace("\r\n", "^", StringComparison.Ordinal)
            .Replace("\n", "^", StringComparison.Ordinal)
            .Replace("\r", "^", StringComparison.Ordinal)
            .Trim();
        return (cleaned.Length == 0 ? fallback : cleaned)[..Math.Min(cleaned.Length == 0 ? fallback.Length : cleaned.Length, maxLength)];
    }

    private sealed class StarterGiftSaveData
    {
        public Dictionary<string, string> Letters { get; set; } = new(StringComparer.Ordinal);
        public bool PermanentGiftQueued { get; set; }
        public long LastTestAtUtcTicks { get; set; }
    }
}
