using System;
using Microsoft.Xna.Framework;
using Pathoschild.Stardew.Automate;
using SolidFoundations.Framework.Models.ContentPack;
using SolidFoundationsAutomate.Framework.Integrations;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Buildings;
using StardewValley.TerrainFeatures;
using SObject = StardewValley.Object;

namespace SolidFoundationsAutomate.Framework.Automate;

/// <summary>Creates Automate machines for Solid Foundations buildings.</summary>
internal sealed class SolidFoundationsBuildingFactory : IAutomationFactory
{
    private readonly SolidFoundationsModelProvider Models;
    private readonly InputFilterEvaluator Filters;
    private readonly IMonitor Monitor;

    /// <summary>Construct an instance.</summary>
    /// <param name="models">Provides SF building models.</param>
    /// <param name="monitor">The SMAPI log.</param>
    public SolidFoundationsBuildingFactory(SolidFoundationsModelProvider models, IMonitor monitor)
    {
        this.Models = models ?? throw new ArgumentNullException(nameof(models));
        this.Monitor = monitor ?? throw new ArgumentNullException(nameof(monitor));
        this.Filters = new InputFilterEvaluator(models);
    }

    /// <inheritdoc />
    public IAutomatable? GetFor(SObject obj, GameLocation location, in Vector2 tile)
    {
        return null;
    }

    /// <inheritdoc />
    public IAutomatable? GetFor(TerrainFeature feature, GameLocation location, in Vector2 tile)
    {
        return null;
    }

    /// <inheritdoc />
    public IAutomatable? GetFor(Building building, GameLocation location, in Vector2 tile)
    {
        if (!this.Models.TryGetModel(building, out ExtendedBuildingModel? model))
            return null;

        // Pack authors opt out per building. Returning null here (with the built-in machine suppressed by
        // AutomatePatches) means Automate leaves the building completely alone, which is what the flag promises.
        if (model.DisableAutomate)
            return null;

        try
        {
            return SolidFoundationsBuildingMachine.TryCreate(building, location, model, this.Filters, this.Monitor);
        }
        catch (Exception ex)
        {
            this.Monitor.LogOnce($"Failed to create an Automate machine for Solid Foundations building '{building.buildingType.Value}':\n{ex}", LogLevel.Error);
            return null;
        }
    }

    /// <inheritdoc />
    public IAutomatable? GetForTile(GameLocation location, in Vector2 tile)
    {
        return null;
    }
}
