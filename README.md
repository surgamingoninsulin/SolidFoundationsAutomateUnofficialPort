# Solid Foundations – Automate Integration (Unofficial 1.6 Port)

An unofficial Stardew Valley **1.6** port of PeacefulEnd's *Solid Foundations – Automate Integration*
(`1.0.0-beta.1`, Nexus 12311), rebuilt from a decompile of the original DLL against the current
[Solid Foundations](https://github.com/Floogen/SolidFoundations) 3.x and [Automate](https://github.com/Pathoschild/StardewMods/tree/develop/Automate) 2.x code.

It uses its own `UniqueID` (`PeacefulEnd.SolidFoundationsAutomate.UnofficialPort`), distinct from the old beta's
`PeacefulEnd.SolidFoundationsAutomate` ID. SMAPI's update-check database maps that original ID to Nexus 12311's
merged-and-broken release, so keeping it would permanently flag this working port as needing an update to a broken
version. Delete the old beta's mod folder — this isn't a drop-in replacement, it's a separate mod with the same
functionality.

## Why it's still needed on 1.6

On 1.6, Solid Foundations registers its buildings in the vanilla `Data/Buildings` asset, and Automate 2.1+
automates any building with `ItemConversions` on its own. But Automate's built-in handler doesn't know about SF-only fields:

| SF feature | Automate alone | With this mod |
|---|---|---|
| `DisableAutomate: true` | ignored, the building is automated anyway | respected, Automate leaves it alone |
| `InputFilters` → `MaxAllowed` | ignored | inserts exactly up to the limit |
| `InputFilters` → `RejectWhileProcessing` | ignored | no input while a timed conversion has items waiting |
| `InputFilters` → `Condition` / `ModDataFlags` | ignored | evaluated like SF does |
| Chest-only buildings (no conversions) | not automated | automated with the original addon's Load/Collect rules |
| Item conversion batches | one `RequiredCount` per cycle | fills the chest in whole batches |

## What changed from the original beta

| Original (SDV 1.5.6 / SMAPI 3) | Port (SDV 1.6 / SMAPI 4) |
|---|---|
| `GenericBuilding` subclass | vanilla `Building` + SF model looked up through SF's public API |
| `BuildableGameLocation` | `GameLocation` (Automate 2.x factory signature) |
| Single input/output chest picked by chest type | Chest roles taken from conversions (`SourceChest` → input, `DestinationChest` → output); chest-type rules kept as a fallback |
| `MaxConcurrentConversions` gating | dropped, because SF 3.x no longer reads the field |
| `Utility.addItemToThisInventoryList` | stack-safe chunked `Chest.addItem`, never over-stacks a slot |
| .NET 5 | .NET 6 |

**How it hooks in:** Automate asks its own factory first, and that factory claims every building with conversions.
A one-method Harmony postfix on `DataBasedBuildingMachine.CanAutomate` returns `false` for SF buildings only, so they
fall through to this mod's factory. Vanilla and other modded buildings are untouched. If a future Automate update
renames that method, the mod logs a warning and SF buildings fall back to plain Automate behaviour. Nothing breaks.

## Requirements

- Stardew Valley 1.6.9+
- SMAPI 4.1.10+
- Solid Foundations 3.0.0+
- Automate 2.1.0+

## Building (on your gaming PC)

The game's DLLs can't be redistributed, so you compile it on the machine where Stardew is installed.

1. Install the **.NET 6 SDK or newer**: <https://dotnet.microsoft.com/download>
2. Make sure **Automate** and **Solid Foundations** are installed in `Stardew Valley\Mods\`
   (the project compiles directly against `Mods\Automate\Automate.dll` and `Mods\SolidFoundations\SolidFoundations.dll`).
3. Open a terminal in this folder and run:

   ```powershell
   dotnet build -c Release
   ```

That's it. `Pathoschild.Stardew.ModBuildConfig` finds your game folder, copies the built mod into
`Mods\SolidFoundationsAutomate\`, and writes a release zip to `bin\Release\net6.0\`.

**Custom paths** (Steam library on another drive, or a mod manager profile):

```powershell
dotnet build -c Release -p:GamePath="E:\SteamLibrary\steamapps\common\Stardew Valley"
dotnet build -c Release -p:GameModsPath="C:\Users\you\Stardrop\Mods"
```

## Installing

1. Delete the old `SolidFoundationsAutomate` folder from `Mods\` (the 1.0.0-beta.1 DLL crashes on 1.6).
2. Build as above (the folder is deployed automatically), or unzip the release zip into `Mods\`.
3. Launch through SMAPI. With `verbose` logging you should see:
   `Patched ...DataBasedBuildingMachine.CanAutomate so Solid Foundations buildings use this mod's machine.`

## Notes for pack authors

- Machine IDs match Automate's built-in format (building type with non-alphanumerics stripped, e.g.
  `StardewBrewery_Brewery` → `StardewBreweryBrewery`), so existing Automate per-machine settings still apply.
- A chest that is both a `SourceChest` and a `DestinationChest` (an intermediate step in a chain) is never fed or
  drained, so Automate can't pull half-finished goods out.
- Filter conditions are evaluated against the building's own location, not the player's, since Automate runs
  wherever the player is.

## Troubleshooting

| Symptom | Cause | Fix |
|---|---|---|
| `Automate.dll not found` at build | Dependency path wrong | Pass `-p:GameModsPath=...` |
| `Couldn't redirect Automate's built-in building handler` in the log | Automate internals changed | SF buildings still work via Automate, but without DisableAutomate/filters. Report it with your Automate version |
| A building isn't automated | `DisableAutomate: true`, no chests, or still under construction | Check the pack's `building.json` |

## License & permissions

Solid Foundations is GPL-3.0 and this mod links against it, so this port's source is GPL-3.0 as well.
The original addon's source was never published. Before uploading a build to Nexus, check PeacefulEnd's
permissions on the original page, or ask them. Keep it private otherwise.
