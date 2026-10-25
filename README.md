# Simple Fishing Trap

A little trap that catches Raw Fish while you're off doing other things.

## How it works

- **Build it** with the Hammer (Crafting tab) near a **Workbench**, for **6 Wood + 10 Leather Scraps**.
- **Place it right next to water**: within **0.25 m** of the water's edge and no more than 0.25 m above the surface. The build ghost turns red if it's too far.
- **It catches 1 Raw Fish every 3 minutes**, the same Raw Fish used in cooking.
- **It holds 10 fish.** A full trap stops catching until you empty it.
- **Press E to collect.** The fish go straight into your inventory, and anything that doesn't fit drops at your feet.
- Like bee hives, traps keep catching while you're away and catch up when you come back. Traps inside someone else's ward can't be emptied.

## Configuration

File: `BepInEx/config/BhimaJenkins.SimpleFishingTrap.cfg`. Changes apply live.

| Section | Setting | Default |
|---|---|---|
| 1 - Catching | Seconds Per Fish | 180 |
| | Trap Capacity | 10 |
| | Catch While Away | true |
| | Item Prefab | FishRaw |
| 2 - Placement | Max Distance From Water | 0.25 |
| | Max Height Above Water | 0.25 (raise to ~1.5 to allow docks) |
| 3 - Building | Recipe | Wood:6, LeatherScraps:10 |
| | Crafting Station | piece_workbench |
| 4 - Server | Enforce Server Settings | true |

## Multiplayer

Install on the server and on every client. The trap's fish count is saved on the trap itself, so every player sees the same number and only one player can collect each fish. The server's settings are sent to everyone who joins.
