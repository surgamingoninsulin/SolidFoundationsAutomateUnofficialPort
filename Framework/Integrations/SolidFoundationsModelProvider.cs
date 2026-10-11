using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using SolidFoundations.Framework.Interfaces.Internal;
using SolidFoundations.Framework.Models.ContentPack;
using StardewModdingAPI;
using StardewValley.Buildings;

namespace SolidFoundationsAutomate.Framework.Integrations;

/// <summary>Safe access to Solid Foundations building models and flags through SF's public API.</summary>
/// <remarks>
/// Every call is wrapped so a misbehaving content pack or an SF API change can't break Automate's machine scan,
/// which runs for every building in every automated location.
/// </remarks>
internal sealed class SolidFoundationsModelProvider
{
    private readonly IApi Api;
    private readonly IMonitor Monitor;

    /// <summary>Construct an instance.</summary>
    /// <param name="api">The Solid Foundations API.</param>
    /// <param name="monitor">The SMAPI log.</param>
    public SolidFoundationsModelProvider(IApi api, IMonitor monitor)
    {
        this.Api = api ?? throw new ArgumentNullException(nameof(api));
        this.Monitor = monitor ?? throw new ArgumentNullException(nameof(monitor));
    }

    /// <summary>Get the SF model for a building, if it's a Solid Foundations building.</summary>
    /// <param name="building">The building to check.</param>
    /// <param name="model">The SF model, if found.</param>
    public bool TryGetModel(Building? building, [NotNullWhen(true)] out ExtendedBuildingModel? model)
    {
        model = null;
        if (building is null)
            return false;

        try
        {
            KeyValuePair<bool, ExtendedBuildingModel> result = this.Api.GetBuildingModel(building);
            if (result.Key && result.Value is not null)
            {
                model = result.Value;
                return true;
            }
        }
        catch (Exception ex)
        {
            this.Monitor.LogOnce($"Failed to read the Solid Foundations model for building type '{building.buildingType.Value}':\n{ex}", LogLevel.Error);
        }

        return false;
    }

    /// <summary>Get whether a building is managed by Solid Foundations.</summary>
    /// <param name="building">The building to check.</param>
    public bool IsSolidFoundationsBuilding(Building? building)
    {
        return this.TryGetModel(building, out _);
    }

    /// <summary>Get whether a building has an SF mod-data flag, using SF's own key format and matching rules.</summary>
    /// <param name="building">The building to check.</param>
    /// <param name="flag">The flag name, without the <c>!</c> negation prefix.</param>
    public bool HasFlag(Building building, string flag)
    {
        try
        {
            return this.Api.DoesBuildingHaveFlag(building, flag);
        }
        catch (Exception ex)
        {
            this.Monitor.LogOnce($"Failed to check Solid Foundations flag '{flag}' on building type '{building.buildingType.Value}':\n{ex}", LogLevel.Error);
            return false;
        }
    }
}
