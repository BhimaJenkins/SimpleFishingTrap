# Changelog

## 0.1.0
- Initial release: Simple Fishing Trap piece (Workbench, 6 Wood + 10 Leather Scraps).
- Must be placed within 0.25 m of water; ghost turns red otherwise.
- Catches 1 Raw Fish every 3 minutes, holds 10, stops when full. Press E to collect into inventory.
- Keeps catching while away (bee hive style). Ward-protected.
- Custom model loads from plain files (OBJ + PNG textures + PNG icon) next to the DLL — no Unity needed. Unity AssetBundle still supported.
- Trap model: two leaning wooden stakes, sagging nylon net cork floats, stone weights, rope lashings; 4 fish appear as it fills. 256px atlas, ~2.5k tris.
- Placeholder chest model when no model files are present; optional "FishVisual" fill-up meshes.
- Everything configurable; server-enforced settings sync.
