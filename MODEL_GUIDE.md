# Simple Fishing Trap model files

You don't need Unity. The mod loads the trap model straight from plain files placed next to `SimpleFishingTrap.dll`:

| File | Required | What it is |
|---|---|---|
| `SimpleFishingTrap.obj` | yes | The model, in metres, with its pivot at the bottom centre and the front facing forward |
| `SimpleFishingTrap_Albedo.png` | yes | Colour texture |
| `SimpleFishingTrap_Normal.png` | no | Normal map |
| `SimpleFishingTrap_Icon.png` | no | Build-menu icon, about 128×128 with a transparent background |

If no model files are found, the mod uses the vanilla wooden chest as a placeholder.

To set it up in the project, put the files in the `Assets/` folder and build. They're copied next to the DLL automatically. For Thunderstore, put them in the zip next to the DLL.

## Named parts inside the OBJ

Each named object in the OBJ becomes its own part:

- **`Collider`** (or `Collider1`, `Collider2`, …): a hidden box that sets the trap's collision. If there isn't one, a box is fitted around the whole model.
- **`FishVisual1`, `FishVisual2`, …**: fish that appear one by one as the trap fills, and disappear when it's emptied.
- **Anything else**: the visible model.

The trap uses Valheim's own piece shader, so it gets wet in the rain and snow-covered like vanilla builds.

## Swapping the model later

Replace the files and restart the game. Claude can prep any FBX, OBJ, glTF or .blend file into this format.

## Advanced: Unity AssetBundle (optional)

An AssetBundle named `simplefishingtrap` containing a prefab `SimpleFishingTrap_Model` is still supported, and takes priority over the OBJ. `UnityEditor/BuildSimpleFishingTrapBundle.cs` builds one.
