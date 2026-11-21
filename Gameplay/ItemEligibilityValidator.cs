using StardewValley;

namespace SpectrumStardewMod.Gameplay;

/// <summary>
/// Central policy gate for item identifiers submitted to the starter-gift feature.
/// Keep all eligibility exclusions here so game-version changes can be reviewed
/// in one place. This class intentionally does not deliver or grant items.
/// </summary>
internal static class ItemEligibilityValidator
{
    /// <summary>
    /// Increment when the policy rules change, so diagnostics can identify which
    /// rule set made an eligibility decision.
    /// </summary>
    internal const int RulesVersion = 3;

    internal sealed record ValidationResult(
        bool IsEligible,
        string RuleId,
        string Reason,
        Item? ResolvedItem
    );

    private sealed record ValidationRule(
        string Id,
        Func<Item, bool> IsAllowed,
        string RejectionReason
    );

    // Native Stardew mail attachments use the standard object item type.
    // Accept every registered object, including non-food and mod-provided objects;
    // classify the resolved registry item rather than trusting the submitted ID.
    private static readonly ValidationRule[] Rules =
    {
        new(
            "ALLOW_OBJECT_TYPE_ONLY",
            item => item.TypeDefinitionId == ItemRegistry.type_object,
            "Only registered standard object items can be attached to a starter letter."
        )
    };

    /// <summary>
    /// Resolve an identifier through Stardew's registry, then apply the central
    /// eligibility rules to the actual resolved item rather than trusting its text.
    /// </summary>
    internal static ValidationResult Validate(string? itemIdentifier)
    {
        string identifier = (itemIdentifier ?? string.Empty).Trim();
        if (identifier.Length == 0)
            return Reject("REJECT_EMPTY_IDENTIFIER", "No item identifier was provided.");

        Item? item;
        try
        {
            item = ItemRegistry.Create(identifier, allowNull: true);
        }
        catch (Exception)
        {
            return Reject("REJECT_ITEM_RESOLUTION_ERROR", "Stardew could not resolve the item identifier.");
        }

        if (item is null)
            return Reject("REJECT_UNRESOLVED_ITEM", "The item identifier does not resolve to a registered item.");

        foreach (ValidationRule rule in Rules)
        {
            bool allowed;
            try
            {
                allowed = rule.IsAllowed(item);
            }
            catch (Exception)
            {
                return Reject("REJECT_ITEM_METADATA_ERROR", "The resolved item could not be safely classified.");
            }

            if (!allowed)
                return new ValidationResult(false, rule.Id, rule.RejectionReason, null);
        }

        return new ValidationResult(
            true,
            "ACCEPT_ELIGIBLE_ITEM",
            $"Item passed eligibility rules version {RulesVersion}.",
            item
        );
    }

    private static ValidationResult Reject(string ruleId, string reason) =>
        new(false, ruleId, reason, null);
}
