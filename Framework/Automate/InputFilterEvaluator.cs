using System;
using System.Collections.Generic;
using SolidFoundations.Framework.Models.ContentPack;
using SolidFoundationsAutomate.Framework.Integrations;
using StardewValley;
using StardewValley.Buildings;
using StardewValley.Objects;

namespace SolidFoundationsAutomate.Framework.Automate;

/// <summary>Applies a Solid Foundations building's <c>InputFilters</c> to items Automate wants to insert.</summary>
/// <remarks>
/// <para>
/// Mirrors SF's own <c>BuildingExtensions.IsObjectFilteredForChest</c> (internal to SF, so it can't be called
/// directly), with two Automate-specific refinements:
/// </para>
/// <list type="bullet">
///   <item>
///     <c>MaxAllowed</c> is returned as a remaining allowance, so Automate inserts exactly up to the limit. SF only
///     blocks a manual insert once the chest is already over the limit, which is fine for one click but lets a bulk
///     insert overshoot.
///   </item>
///   <item>
///     <c>RejectWhileProcessing</c> treats the building as processing while any timed conversion
///     (<c>MinutesPerConversion &gt;= 0</c>) still has items waiting in its source chest. SF keeps the countdown in
///     an internal field on the shared model, so that is the closest per-building signal it exposes.
///   </item>
/// </list>
/// </remarks>
internal sealed class InputFilterEvaluator
{
    /// <summary>No filter limits the amount.</summary>
    public const int Unlimited = int.MaxValue;

    private readonly SolidFoundationsModelProvider Models;

    /// <summary>Construct an instance.</summary>
    /// <param name="models">Provides SF building flags.</param>
    public InputFilterEvaluator(SolidFoundationsModelProvider models)
    {
        this.Models = models ?? throw new ArgumentNullException(nameof(models));
    }

    /// <summary>Get how many of an item may still be added to an input chest under the building's filters.</summary>
    /// <param name="building">The building that owns the chest.</param>
    /// <param name="model">The building's SF model.</param>
    /// <param name="chestId">The ID the chest was resolved by (the SF chest <c>Name</c>).</param>
    /// <param name="chest">The input chest.</param>
    /// <param name="item">A sample of the item to insert.</param>
    /// <returns><c>0</c> if the item is rejected, <see cref="Unlimited"/> if no filter caps it, else the remaining allowance.</returns>
    public int GetRemainingAllowance(Building building, ExtendedBuildingModel model, string chestId, Chest chest, Item item)
    {
        List<InputFilter>? filters = model.InputFilters;
        if (filters is null || filters.Count == 0)
            return Unlimited;

        int allowance = Unlimited;
        foreach (InputFilter? filter in filters)
        {
            if (filter?.RestrictedItems is null || !IsFilterForChest(filter, chestId, chest))
                continue;

            foreach (InputFilter.RestrictedItem? restriction in filter.RestrictedItems)
            {
                if (restriction is null || !MatchesAllTags(item, restriction.RequiredTags))
                    continue;

                if (restriction.RejectWhileProcessing && this.IsProcessing(building, model))
                    return 0;

                // Same precedence as SF: an explicit condition wins; mod-data flags alone are checked otherwise.
                if (!string.IsNullOrEmpty(restriction.Condition))
                {
                    if (this.ValidateConditions(building, restriction.Condition, restriction.ModDataFlags))
                        return 0;
                }
                else if (restriction.ModDataFlags is not null && this.ValidateConditions(building, null, restriction.ModDataFlags))
                {
                    return 0;
                }

                if (restriction.MaxAllowed >= 0)
                {
                    int current = CountMatching(chest, restriction.RequiredTags);
                    allowance = Math.Min(allowance, Math.Max(0, restriction.MaxAllowed - current));
                    if (allowance == 0)
                        return 0;
                }
            }
        }

        return allowance;
    }

    /// <summary>Get whether any timed conversion currently has input waiting in its source chest.</summary>
    /// <param name="building">The building to check.</param>
    /// <param name="model">The building's SF model.</param>
    public bool IsProcessing(Building building, ExtendedBuildingModel model)
    {
        List<ExtendedBuildingItemConversion>? conversions = model.ItemConversions;
        if (conversions is null)
            return false;

        foreach (ExtendedBuildingItemConversion? conversion in conversions)
        {
            if (conversion is null || conversion.MinutesPerConversion < 0 || string.IsNullOrWhiteSpace(conversion.SourceChest))
                continue;

            Chest? source = building.GetBuildingChest(conversion.SourceChest);
            if (source is null)
                continue;

            foreach (Item? slot in source.Items)
            {
                if (slot is not null && slot.Stack > 0)
                    return true;
            }
        }

        return false;
    }

    /// <summary>Port of SF's <c>BuildingExtensions.ValidateConditions</c>, evaluated against the building's own location.</summary>
    /// <param name="building">The building whose flags to check.</param>
    /// <param name="condition">A game state query, or null/empty to skip it (vanilla treats that as true).</param>
    /// <param name="modDataFlags">SF flags that must exist, or must not exist when prefixed with <c>!</c>.</param>
    private bool ValidateConditions(Building building, string? condition, string[]? modDataFlags)
    {
        // Automate runs while the player may be elsewhere, so evaluate location queries where the building is.
        if (!GameStateQuery.CheckConditions(condition, building.GetParentLocation()))
            return false;

        if (modDataFlags is null)
            return true;

        foreach (string? rawFlag in modDataFlags)
        {
            string flag = (rawFlag ?? string.Empty).Replace(" ", string.Empty);
            bool mustBeAbsent = flag.Length > 0 && flag[0] == '!';
            if (mustBeAbsent)
                flag = flag.Substring(1);

            bool exists = flag.Length > 0 && this.Models.HasFlag(building, flag);
            if (exists == mustBeAbsent)
                return false;
        }

        return true;
    }

    /// <summary>Get whether an input filter targets the given chest.</summary>
    private static bool IsFilterForChest(InputFilter filter, string chestId, Chest chest)
    {
        // SF compares against Chest.Name; the chest ID is checked too in case a building chest's Name was never set.
        return string.Equals(filter.InputChest, chest.Name, StringComparison.Ordinal)
            || string.Equals(filter.InputChest, chestId, StringComparison.Ordinal);
    }

    /// <summary>Get whether an item has every required context tag (an empty list matches everything, like SF).</summary>
    private static bool MatchesAllTags(Item item, List<string>? requiredTags)
    {
        if (requiredTags is null)
            return true;

        foreach (string tag in requiredTags)
        {
            if (!item.HasContextTag(tag))
                return false;
        }

        return true;
    }

    /// <summary>Count the items in a chest that match a restriction's tags.</summary>
    private static int CountMatching(Chest chest, List<string>? requiredTags)
    {
        int count = 0;
        foreach (Item? slot in chest.Items)
        {
            if (slot is not null && MatchesAllTags(slot, requiredTags))
                count += slot.Stack;
        }

        return count;
    }
}
