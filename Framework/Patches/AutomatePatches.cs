using System;
using System.Reflection;
using HarmonyLib;
using SolidFoundationsAutomate.Framework.Integrations;
using StardewModdingAPI;
using StardewValley.Buildings;

namespace SolidFoundationsAutomate.Framework.Patches;

/// <summary>Redirects Solid Foundations buildings away from Automate's built-in data-based building machine.</summary>
/// <remarks>
/// Automate resolves a building by asking each registered factory in order and keeping the first non-null result.
/// Its own factory is always first and returns a <c>DataBasedBuildingMachine</c> for any building whose
/// <c>Data/Buildings</c> entry has item conversions, which includes every SF processing building on 1.6.
/// Postfixing the static <c>DataBasedBuildingMachine.CanAutomate(Building)</c> check is the narrowest hook that lets
/// the factory from this mod receive those buildings, without touching any other Automate behaviour.
/// </remarks>
internal static class AutomatePatches
{
    private const string DataBasedBuildingMachineType = "Pathoschild.Stardew.Automate.Framework.Machines.DataBasedBuildingMachine";
    private const string CanAutomateMethod = "CanAutomate";

    private static SolidFoundationsModelProvider? Models;
    private static IMonitor? Monitor;

    /// <summary>Apply the patch.</summary>
    /// <param name="harmonyId">The Harmony instance ID (this mod's unique ID).</param>
    /// <param name="models">Provides SF building models.</param>
    /// <param name="monitor">The SMAPI log.</param>
    /// <returns>Whether the patch was applied.</returns>
    public static bool Apply(string harmonyId, SolidFoundationsModelProvider models, IMonitor monitor)
    {
        Models = models;
        Monitor = monitor;

        try
        {
            Type? machineType = AccessTools.TypeByName(DataBasedBuildingMachineType);
            MethodInfo? canAutomate = machineType is null
                ? null
                : AccessTools.Method(machineType, CanAutomateMethod, new[] { typeof(Building) });

            if (canAutomate is null)
            {
                monitor.Log($"Couldn't find {DataBasedBuildingMachineType}.{CanAutomateMethod}(Building) in the installed Automate version.", LogLevel.Warn);
                return false;
            }

            var harmony = new Harmony(harmonyId);
            harmony.Patch(
                original: canAutomate,
                postfix: new HarmonyMethod(typeof(AutomatePatches), nameof(After_CanAutomate))
            );

            monitor.Log($"Patched {DataBasedBuildingMachineType}.{CanAutomateMethod} so Solid Foundations buildings use this mod's machine.", LogLevel.Trace);
            return true;
        }
        catch (Exception ex)
        {
            monitor.Log($"Failed to patch Automate:\n{ex}", LogLevel.Error);
            return false;
        }
    }

    /// <summary>After Automate decides whether its built-in machine handles a building, reject SF buildings.</summary>
    /// <param name="__0">The building being checked (by index, so a parameter rename in Automate can't break the patch).</param>
    /// <param name="__result">Whether Automate's built-in machine will handle the building.</param>
    private static void After_CanAutomate(Building __0, ref bool __result)
    {
        if (!__result || Models is null)
            return;

        try
        {
            if (Models.IsSolidFoundationsBuilding(__0))
                __result = false;
        }
        catch (Exception ex)
        {
            // Leave Automate's decision untouched; a failure here must never stop the machine scan.
            Monitor?.LogOnce($"Failed to check whether a building belongs to Solid Foundations:\n{ex}", LogLevel.Error);
        }
    }
}
