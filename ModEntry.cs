using System;
using Pathoschild.Stardew.Automate;
using SolidFoundations.Framework.Interfaces.Internal;
using SolidFoundationsAutomate.Framework.Automate;
using SolidFoundationsAutomate.Framework.Integrations;
using SolidFoundationsAutomate.Framework.Patches;
using StardewModdingAPI;
using StardewModdingAPI.Events;

namespace SolidFoundationsAutomate;

/// <summary>
/// Entry point for the unofficial Stardew Valley 1.6 port of "Solid Foundations - Automate Integration".
/// </summary>
/// <remarks>
/// <para>
/// In SDV 1.6, Solid Foundations 3.x registers every custom building in the vanilla <c>Data/Buildings</c> asset,
/// and Automate 2.1+ automatically wraps any building with <c>ItemConversions</c> in its built-in
/// <c>DataBasedBuildingMachine</c>. That built-in machine knows nothing about SF-only fields, so on its own it:
/// </para>
/// <list type="bullet">
///   <item>ignores <c>DisableAutomate</c> (packs that opt out still get automated);</item>
///   <item>ignores SF <c>InputFilters</c> (MaxAllowed, RejectWhileProcessing, Condition, ModDataFlags);</item>
///   <item>never touches SF buildings that only define chests (no conversions), which the original addon supported.</item>
/// </list>
/// <para>
/// This mod hands every SF building to its own SF-aware machine instead. Automate always asks its built-in factory
/// first, so a narrow Harmony postfix makes <c>DataBasedBuildingMachine.CanAutomate</c> return false for SF buildings,
/// which lets the factory registered here pick them up.
/// </para>
/// </remarks>
internal sealed class ModEntry : Mod
{
    private const string SolidFoundationsId = "PeacefulEnd.SolidFoundations";
    private const string AutomateId = "Pathoschild.Automate";

    /// <inheritdoc />
    public override void Entry(IModHelper helper)
    {
        helper.Events.GameLoop.GameLaunched += this.OnGameLaunched;
    }

    /// <summary>Hook into both APIs once every mod has finished its own Entry.</summary>
    private void OnGameLaunched(object? sender, GameLaunchedEventArgs e)
    {
        IApi? solidFoundationsApi = this.TryGetApi<IApi>(SolidFoundationsId, "Solid Foundations");
        IAutomateAPI? automateApi = this.TryGetApi<IAutomateAPI>(AutomateId, "Automate");
        if (solidFoundationsApi is null || automateApi is null)
        {
            this.Monitor.Log("Automate integration is disabled for this session because a required API is unavailable.", LogLevel.Error);
            return;
        }

        var models = new SolidFoundationsModelProvider(solidFoundationsApi, this.Monitor);

        // Patch before registering the factory so the very first machine scan already routes SF buildings to us.
        bool patched = AutomatePatches.Apply(this.ModManifest.UniqueID, models, this.Monitor);
        if (!patched)
        {
            this.Monitor.Log(
                "Couldn't redirect Automate's built-in building handler. SF buildings that define ItemConversions will still be "
                + "automated by Automate itself, but DisableAutomate and InputFilters won't be applied to them. "
                + "Chest-only SF buildings are unaffected and still handled by this mod.",
                LogLevel.Warn
            );
        }

        try
        {
            automateApi.AddFactory(new SolidFoundationsBuildingFactory(models, this.Monitor));
            this.Monitor.Log("Registered the Solid Foundations building factory with Automate.", LogLevel.Trace);
        }
        catch (Exception ex)
        {
            this.Monitor.Log($"Failed to register the Solid Foundations factory with Automate:\n{ex}", LogLevel.Error);
        }
    }

    /// <summary>Get a mod API, logging a clear error instead of throwing when it's missing or incompatible.</summary>
    /// <typeparam name="TApi">The API interface, referenced directly from the provider's own assembly.</typeparam>
    /// <param name="modId">The provider's unique ID.</param>
    /// <param name="displayName">A human-readable mod name for log messages.</param>
    private TApi? TryGetApi<TApi>(string modId, string displayName)
        where TApi : class
    {
        if (!this.Helper.ModRegistry.IsLoaded(modId))
        {
            this.Monitor.Log($"{displayName} ({modId}) isn't loaded.", LogLevel.Error);
            return null;
        }

        try
        {
            TApi? api = this.Helper.ModRegistry.GetApi<TApi>(modId);
            if (api is null)
                this.Monitor.Log($"{displayName} ({modId}) is loaded but didn't provide a compatible API. Check that it's up to date.", LogLevel.Error);
            return api;
        }
        catch (Exception ex)
        {
            this.Monitor.Log($"Failed to get the {displayName} API. This usually means the installed version is incompatible with this port.\n{ex}", LogLevel.Error);
            return null;
        }
    }
}
