using StardewModdingAPI;
using StardewValley;

namespace SpectrumStardewMod.Gameplay;

/// <summary>
/// In-game integration checks using the live Stardew item registry. This suite
/// never adds items to inventory, mail, or the world.
/// </summary>
internal static class ItemEligibilityValidatorTests
{
    internal static void Run(IMonitor monitor)
    {
        int passed = 0;
        int failed = 0;
        int skipped = 0;

        Check("known edible vanilla object is accepted", ItemEligibilityValidator.Validate("(O)24"), true, "ACCEPT_ELIGIBLE_ITEM");
        Check("known non-edible vanilla object is rejected", ItemEligibilityValidator.Validate("(O)388"), false, "ALLOW_EDIBLE_OBJECT_ONLY");
        Check("empty identifier is rejected", ItemEligibilityValidator.Validate(""), false, "REJECT_EMPTY_IDENTIFIER");
        Check("unresolved identifier is rejected", ItemEligibilityValidator.Validate("(O)SpectrumStardewMod_Test_Missing_6F34A2"), false, "REJECT_UNRESOLVED_ITEM");

        Item? nonObjectFixture = null;
        try { nonObjectFixture = ItemRegistry.Create("(BC)130", allowNull: true); }
        catch (Exception) { /* Optional fixture; report a skip below. */ }

        if (nonObjectFixture is null)
        {
            skipped++;
            monitor.Log("[Spectrum] ITEM VALIDATOR TEST SKIPPED | optional non-object fixture (BC)130 is unavailable.", LogLevel.Warn);
        }
        else
        {
            Check("registered non-object item type is rejected", ItemEligibilityValidator.Validate("(BC)130"), false, "ALLOW_OBJECT_TYPE_ONLY");
        }

        void Check(string name, ItemEligibilityValidator.ValidationResult result, bool expectedEligible, string expectedRuleId)
        {
            bool success = result.IsEligible == expectedEligible && string.Equals(result.RuleId, expectedRuleId, StringComparison.Ordinal);
            if (success)
            {
                passed++;
                monitor.Log($"[Spectrum] ITEM VALIDATOR TEST PASSED | {name} | {result.RuleId}", LogLevel.Info);
            }
            else
            {
                failed++;
                monitor.Log($"[Spectrum] ITEM VALIDATOR TEST FAILED | {name} | expected {expectedRuleId}/{expectedEligible}, got {result.RuleId}/{result.IsEligible} | {result.Reason}", LogLevel.Error);
            }
        }

        monitor.Log($"[Spectrum] ITEM VALIDATOR TESTS COMPLETE | {passed} passed | {failed} failed | {skipped} skipped | rules v{ItemEligibilityValidator.RulesVersion}. No items were delivered or added.", failed == 0 ? LogLevel.Info : LogLevel.Error);
    }
}
