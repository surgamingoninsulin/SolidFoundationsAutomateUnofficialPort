using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Pathoschild.Stardew.Automate;
using SolidFoundations.Framework.Models.ContentPack;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Buildings;
using StardewValley.GameData.Buildings;
using StardewValley.Objects;

namespace SolidFoundationsAutomate.Framework.Automate;

/// <summary>An Automate machine for a Solid Foundations building's input and output chests.</summary>
/// <remarks>
/// <para>Chest roles are resolved once per machine scan:</para>
/// <list type="number">
///   <item>
///     From <c>ItemConversions</c> (same source of truth as Automate's built-in handler): every <c>SourceChest</c> is
///     an input and every <c>DestinationChest</c> is an output. A chest that is both (an intermediate step in a
///     chained conversion) is left to the building, so Automate never feeds or drains half-finished goods.
///   </item>
///   <item>
///     If that yields nothing, the original 1.5 addon's rules: the first <c>Load</c> chest (else first <c>Chest</c>)
///     is the input, the first <c>Collect</c> chest (else first <c>Chest</c>) is the output.
///   </item>
/// </list>
/// <para>
/// The chests themselves are looked up on demand, because SF and the game can replace building chest instances
/// (e.g. on upgrade or model refresh) between Automate's rescans.
/// </para>
/// </remarks>
internal sealed class SolidFoundationsBuildingMachine : IMachine
{
    private readonly Building Building;
    private readonly ExtendedBuildingModel Model;
    private readonly InputFilterEvaluator Filters;
    private readonly IMonitor Monitor;
    private readonly string[] InputChestIds;
    private readonly string[] OutputChestIds;

    /// <inheritdoc />
    public GameLocation Location { get; }

    /// <inheritdoc />
    public Rectangle TileArea { get; }

    /// <inheritdoc />
    /// <remarks>Same format as Automate's built-in building IDs, so existing per-machine settings keep working.</remarks>
    public string MachineTypeID { get; }

    private SolidFoundationsBuildingMachine(Building building, GameLocation location, ExtendedBuildingModel model, InputFilterEvaluator filters, IMonitor monitor, string[] inputChestIds, string[] outputChestIds)
    {
        this.Building = building;
        this.Model = model;
        this.Filters = filters;
        this.Monitor = monitor;
        this.InputChestIds = inputChestIds;
        this.OutputChestIds = outputChestIds;

        this.Location = location;
        this.TileArea = new Rectangle(building.tileX.Value, building.tileY.Value, building.tilesWide.Value, building.tilesHigh.Value);
        this.MachineTypeID = new string((building.buildingType.Value ?? model.ID ?? "SolidFoundationsBuilding").Where(char.IsLetterOrDigit).ToArray());
    }

    /// <summary>Create a machine for a building, or null if it has no chests Automate can use.</summary>
    /// <param name="building">The SF building.</param>
    /// <param name="location">The location containing the building.</param>
    /// <param name="model">The building's SF model.</param>
    /// <param name="filters">Applies the model's input filters.</param>
    /// <param name="monitor">The SMAPI log.</param>
    public static SolidFoundationsBuildingMachine? TryCreate(Building building, GameLocation location, ExtendedBuildingModel model, InputFilterEvaluator filters, IMonitor monitor)
    {
        ResolveChestIds(model, out string[] inputs, out string[] outputs);
        if (inputs.Length == 0 && outputs.Length == 0)
            return null;

        return new SolidFoundationsBuildingMachine(building, location, model, filters, monitor, inputs, outputs);
    }

    /// <inheritdoc />
    public MachineState GetState()
    {
        if (this.Building.isUnderConstruction())
            return MachineState.Disabled;

        bool hasAnyChest = false;

        foreach (string id in this.OutputChestIds)
        {
            Chest? chest = this.Building.GetBuildingChest(id);
            if (chest is null)
                continue;

            hasAnyChest = true;
            if (chest.Items.Any(item => item is not null))
                return MachineState.Done;
        }

        foreach (string id in this.InputChestIds)
        {
            Chest? chest = this.Building.GetBuildingChest(id);
            if (chest is null)
                continue;

            hasAnyChest = true;
            if (!IsFull(chest))
                return MachineState.Empty;
        }

        // No chest instances exist yet (e.g. a freshly placed building before its first load): nothing to automate.
        return hasAnyChest ? MachineState.Processing : MachineState.Disabled;
    }

    /// <inheritdoc />
    public ITrackedStack? GetOutput()
    {
        foreach (string id in this.OutputChestIds)
        {
            Chest? chest = this.Building.GetBuildingChest(id);
            if (chest is null)
                continue;

            foreach (Item? item in chest.Items)
            {
                if (item is null)
                    continue;

#pragma warning disable CS0618 // The callback constructor is kept for compatibility with every Automate 2.x release; newer fluent OnEmpty() only exists in recent versions.
                return new TrackedItem(item, onEmpty: taken =>
                {
                    chest.Items.Remove(taken);
                    chest.clearNulls();
                });
#pragma warning restore CS0618
            }
        }

        return null;
    }

    /// <inheritdoc />
    public bool SetInput(IStorage input)
    {
        bool addedAny = false;

        foreach (string id in this.InputChestIds)
        {
            Chest? chest = this.Building.GetBuildingChest(id);
            if (chest is null || IsFull(chest))
                continue;

            chest.clearNulls();

            // Snapshot: GetItems() enumerates live connected storage, and Reduce() below can remove empty stacks.
            foreach (ITrackedStack stack in input.GetItems().ToArray())
            {
                if (stack.Count <= 0)
                    continue;

                try
                {
                    addedAny |= this.TryAddStack(id, chest, stack);
                }
                catch (Exception ex)
                {
                    this.Monitor.LogOnce($"Failed to insert '{stack.Sample?.QualifiedItemId}' into chest '{id}' of Solid Foundations building '{this.Building.buildingType.Value}':\n{ex}", LogLevel.Error);
                }

                if (IsFull(chest))
                    break;
            }
        }

        return addedAny;
    }

    /// <summary>Move as much of a stack into an input chest as conversions, filters and free space allow.</summary>
    /// <param name="chestId">The input chest ID.</param>
    /// <param name="chest">The input chest.</param>
    /// <param name="stack">The connected-storage stack to take from.</param>
    /// <returns>Whether any items were moved.</returns>
    private bool TryAddStack(string chestId, Chest chest, ITrackedStack stack)
    {
        Item sample = stack.Sample;

        // Only accept what the building can actually convert from this chest.
        BuildingItemConversion? rule = this.Building.GetItemConversionForItem(sample, chest);
        if (rule is null && this.Model.ItemConversions?.Count > 0)
            return false;

        int batchSize = Math.Max(1, rule?.RequiredCount ?? 1);
        if (stack.Count < batchSize)
            return false;

        int allowance = this.Filters.GetRemainingAllowance(this.Building, this.Model, chestId, chest, sample);
        int space = GetFreeSpaceFor(chest, sample);

        int toAdd = Math.Min(stack.Count, Math.Min(allowance, space));
        toAdd -= toAdd % batchSize; // only whole batches, so nothing sits in the chest that can never convert
        if (toAdd <= 0)
            return false;

        int added = AddToChest(chest, sample, toAdd);
        if (added <= 0)
            return false;

        stack.Reduce(added);
        return true;
    }

    /// <summary>Resolve which chest IDs Automate should feed and empty.</summary>
    private static void ResolveChestIds(ExtendedBuildingModel model, out string[] inputs, out string[] outputs)
    {
        var sources = new HashSet<string>(StringComparer.Ordinal);
        var destinations = new HashSet<string>(StringComparer.Ordinal);

        if (model.ItemConversions is not null)
        {
            foreach (ExtendedBuildingItemConversion? conversion in model.ItemConversions)
            {
                if (conversion is null)
                    continue;
                if (!string.IsNullOrWhiteSpace(conversion.SourceChest))
                    sources.Add(conversion.SourceChest);
                if (!string.IsNullOrWhiteSpace(conversion.DestinationChest))
                    destinations.Add(conversion.DestinationChest);
            }
        }

        inputs = sources.Where(id => !destinations.Contains(id)).ToArray();
        outputs = destinations.Where(id => !sources.Contains(id)).ToArray();
        if (inputs.Length > 0 || outputs.Length > 0)
            return;

        // Fallback: the original addon's chest-type rules, for chest-only buildings.
        List<ExtendedBuildingChest>? chests = model.Chests;
        if (chests is null || chests.Count == 0)
            return;

        string? input = FirstChestName(chests, BuildingChestType.Load) ?? FirstChestName(chests, BuildingChestType.Chest);
        string? output = FirstChestName(chests, BuildingChestType.Collect) ?? FirstChestName(chests, BuildingChestType.Chest);

        // A single shared 'Chest' would be both: feeding and draining it would just loop items, so skip it.
        if (input is not null && input == output)
            return;

        inputs = input is null ? Array.Empty<string>() : new[] { input };
        outputs = output is null ? Array.Empty<string>() : new[] { output };
    }

    /// <summary>Get the name of the first chest of a type.</summary>
    private static string? FirstChestName(List<ExtendedBuildingChest> chests, BuildingChestType type)
    {
        foreach (ExtendedBuildingChest? chest in chests)
        {
            if (chest is not null && chest.Type == type && !string.IsNullOrWhiteSpace(chest.Name))
                return chest.Name;
        }

        return null;
    }

    /// <summary>Get whether a chest has no free slot and no partial stack left.</summary>
    /// <remarks>Uses <see cref="Chest.GetActualCapacity"/>, which SF patches to honour each chest's <c>Capacity</c>.</remarks>
    private static bool IsFull(Chest chest)
    {
        int used = 0;
        foreach (Item? slot in chest.Items)
        {
            if (slot is null)
                continue;

            used++;
            if (slot.Stack < slot.maximumStackSize())
                return false;
        }

        return used >= chest.GetActualCapacity();
    }

    /// <summary>Get how many of an item fit in a chest, counting partial matching stacks and empty slots.</summary>
    private static int GetFreeSpaceFor(Chest chest, Item sample)
    {
        int maxStack = Math.Max(1, sample.maximumStackSize());
        long space = 0;
        int used = 0;

        foreach (Item? slot in chest.Items)
        {
            if (slot is null)
                continue;

            used++;
            if (slot.canStackWith(sample))
                space += Math.Max(0, slot.maximumStackSize() - slot.Stack);
        }

        space += (long)Math.Max(0, chest.GetActualCapacity() - used) * maxStack;
        return (int)Math.Min(int.MaxValue, space);
    }

    /// <summary>Add items to a chest in stack-sized chunks, so no slot ever exceeds the item's max stack.</summary>
    /// <returns>The number actually added.</returns>
    private static int AddToChest(Chest chest, Item sample, int count)
    {
        int maxStack = Math.Max(1, sample.maximumStackSize());
        int added = 0;

        while (count > 0)
        {
            int chunkSize = Math.Min(count, maxStack);
            Item chunk = sample.getOne();
            chunk.Stack = chunkSize;

            Item? leftover = chest.addItem(chunk);
            int chunkAdded = chunkSize - (leftover?.Stack ?? 0);
            added += chunkAdded;
            count -= chunkAdded;

            if (leftover is not null && leftover.Stack > 0)
                break; // chest is full
        }

        return added;
    }
}
