# GymMaps 1.0.0

Requires MelonLoader, UIFramework, and RumbleModdingAPI. 


## Included default maps

Five default maps are embedded in the mods DLL: `frogian.txt`, `rings.txt`, `towers.txt`, `slipnslide.txt`, and `gridtest.txt`. 

Maps are local files located directly in `UserData/GymMaps`; subdirectories are not supported.

## All primitive modifier tags

| Short tag | Full alias | Effect |
| --- | --- | --- |
| `(I)` | `(Invisible)` | Hide the rendered primitive; collision remains unless removed by another tag. |
| `(W)` | `(Wall)` | Wall collision; adds a BoxCollider alongside the normal mesh collision. |
| `(E)` | `(Environment)` | Layer 10 Environment: player support/slide surface, breaks structures on impact, and is excluded from summonable ground. |
| `(V)` | `(Visual)` | Visible geometry without solid ground collision. |
| `(D)` | `(Death)` | Add a death barrier. Contacts with the local player request its native reset, using the current course start if present. |

Supported primitive shape prefixes are `Cube`, `Sphere`, `Capsule`, `Cylinder`, `Plane` and `Quad`. Exported maps keep the existing five-line records (name, RGB color, XYZ position, XYZ Euler rotation, XYZ scale), preceded by an object count. Dimensions must be nonzero and numbers finite. No Blender exporter changes are required.

Check out SDRAWKCABMIAY's video who pioneered this tool to learn how to make maps!
https://www.youtube.com/watch?v=eqrZrQE3uKA&list=WL&index=4&t=13s


Also join the Rumble mapping community to your maps
https://discord.gg/4GEDyjMmY


## Menus and tools

F9 > GymMaps > Maps provides 'Load', 'Refresh' and 'Unload'. 'Load' validates and builds incoming geometry before removing the existing course. Maps are only available in the Gym. Loading and unloading also clears all previous maps unless appended. Refresh scans valid `.txt` maps directly in `UserData/GymMaps`.

F9 > GymMaps > Tools contains:

- **Enable trigger commands:** off on each launch. Description: "Allows commands that change game and player behavior. Use at your own risk." Gates all OnMapLoad/OnTriggerEnter commands and spawned-button actions. Enabling later does not replay skipped OnMapLoad commands; reload the map to run them.

- **Safety Platform:** off on each launch. When enabled, check support once after each successful replace/append. Supporting ground within 0.5 units below the feet (including Environment, excluding death surfaces) prevents a spawn. Otherwise create a fixed light-blue 4 x 0.25 x 4 platform with its top 0.05 below the feet. If the body is not ready, wait up to five seconds before skipping that load. Enabling does not immediately spawn or start continuous monitoring. Disable, replace, unload or scene exit removes it.
- **Kill floor raise amount:** default 1, range 0.25 to 25 Unity units per click.
- **Raise Kill Floor:** select the largest broad horizontal death collider (at least 10 x 10, thin relative to its width/depth) and raise it by the chosen amount. Vertical walls are excluded. If none exists, first create a floor 5 units below the lowest course geometry, or 10 below the player without a course. Created floors cover the course plus margins and are at least 200 x 200. Further clicks raise them.

## Naming and event datatypes

Name an authoring Cube `Cube<identifier>.<event>.<command>`; for example `CubeFinish.OnTriggerEnter.play.sound.firework` will play a firework sound upon player contact

| Event datatype | Runs when | Example |
| --- | --- | --- |
| `OnMapLoad` | Once after this map's geometry activates and physics transforms synchronize. Runs again when the map reloads. | `CubeSpawn.OnMapLoad.structure.summon.wall` |
| `OnTriggerEnter` | The local player enters or passes through the marker volume. | `CubeFinish.OnTriggerEnter.play.sound.firework` |

## All command templates

The following table lists every built-in command/template and its recommended event. Standard registered commands can also be called from `(OnEnter=COMMAND arguments)` tags. Load commands run after map activation; entering commands use cooldown/once rules. Commands call mod functions, not operating-system shell commands.

| Template after the event | Recommended event | Action |
| --- | --- | --- |
| `structure.summon.cube` | `OnMapLoad` | Summon a native Cube on valid ground below the marker. |
| `structure.summon.wall` | `OnMapLoad` | Summon a native Wall. |
| `structure.summon.pillar` | `OnMapLoad` | Summon a native Pillar. |
| `structure.summon.ball` | `OnMapLoad` | Summon a native Ball. |
| `structure.summon.disk` | `OnMapLoad` | Summon a native Disc. |
| `structure.summon.disc` | `OnMapLoad` | Alias for `structure.summon.disk`. |
| `interface.respawn.start` | `OnMapLoad` | Set the local player's respawn position and facing to this marker. |
| `interface.killfloor.height` | `OnMapLoad` | Set/create the kill floor using a required `(Height=NUMBER)` tag. |
| `summon.asset.button.interface.teleport.gym` | `OnMapLoad` | Spawn a native button that unloads the custom course and teleports to the Gym start when pressed. |
| `summon.asset.button.interface.teleport.map.<mapname>` | `OnMapLoad` | Spawn a native button that replaces the course with the named map and teleports to its start when pressed. |
| `map.load.<mapname>.replace` | `OnTriggerEnter` | Replace the current course with the named map when entered. Does not automatically teleport. |
| `map.load.<mapname>.append` | `OnTriggerEnter` | Add the named map alongside the current course when entered. Does not automatically teleport. |
| `play.sound.telephone` | `OnTriggerEnter` | Play the existing Gym telephone notification sound at the marker. |
| `play.sound.firework` | `OnTriggerEnter` | Play the shop purchase sound bank (`GearMarket.unlockPurchaseSFX`) at the marker. |
| `play.sound.clip.id-<UTF8 hex asset name>` | `OnTriggerEnter` or `OnMapLoad` | Play any loaded audio clip by its exported dot-safe ID. |
| `timer.start` | `OnTriggerEnter` | Restart and start the race timer. |
| `timer.stop` | `OnTriggerEnter` | Stop the timer and show elapsed seconds. |
| `timer.reset` | `OnTriggerEnter` | Reset the timer to zero. |
| `log TEXT` | `(OnEnter=log TEXT)` | Write TEXT to the mod log. |
| `status TEXT` | `(OnEnter=status TEXT)` | Show TEXT in the menu Status field and log it. |

`log` and `status` also accept no text in dot syntax. Use parenthesized OnEnter tags to supply spaces in command arguments. Unknown commands and missing map targets reject a load before the current course is removed. Other mods can register handlers before loading maps with `MapCommands.Register("name", (context, arguments) => { ... });`; the context supplies map name, primitive name and GameObject.

### Structure spawning

Examples:

```text
CubeSpawn.OnMapLoad.structure.summon.cube
CubeWall.OnMapLoad.structure.summon.wall
CubePillar.OnMapLoad.structure.summon.pillar
CubeBall.OnMapLoad.structure.summon.ball
CubeDisk.OnMapLoad.structure.summon.disk
```

### Respawn start

```text
CubeStart.OnMapLoad.interface.respawn.start
```
### Exact kill-floor height

```text
CubeFloor.OnMapLoad.interface.killfloor.height(Height=-10.5)
```

`Height` specifies the kill floor's **top surface in Unity world Y**, independently of the marker's position. Negative heights, decimals and zero are supported. The value must be finite. The tag is required, and supplying it more than once is rejected. Existing floors move to that exact top height; if none is found, an invisible death-trigger floor is created there. Example: `(Height=3.25)` places its top at Y=3.25.

This shares floor selection, creation and cleanup with Tools > Raise Kill Floor. An entering variant, `CubeFloorZone.OnTriggerEnter.interface.killfloor.height(Height=-5)`, sets the height when entered.

### Spawned teleport buttons

```text
CubeGymButton.OnMapLoad.summon.asset.button.interface.teleport.gym
CubeMapButton.OnMapLoad.summon.asset.button.interface.teleport.map.RisingLeftFlow
```

### Map entry transitions

```text
CubePortal.OnTriggerEnter.map.load.<mapname>.replace
CubePortal.OnTriggerEnter.map.load.towers.append
```

Replace clears the complete current map set, triggers, race timer, structures, safety platform, respawn marker, spawned buttons and floor-tool changes before activating the new map. Append adds incoming geometry at its exported world coordinates, preserves existing triggers/state, and runs only the incoming map's load commands. An already-present map is not appended again at identical coordinates. Unload removes the whole set. Map transitions keep the player's current position; use the spawned map teleport button for a transition that also moves the player.

### Audio and race examples

Each command uses `play.sound.clip.id-<UTF8 hex asset name>`; copy the generated ID exactly. IDs preserve spaces and punctuation without breaking dot tags and are resolved dynamically for future markers. Enable trigger commands must be on for commands to run. Missing or unloaded assets log an error instead of blocking map loading. Generic clips play once at the marker, reuse a matching AudioSource mixer and volume when available, and clear on replace/unload. This enumerates loaded Unity assets, not every file in an unloaded game bundle.

```text
CubeTelephone.OnTriggerEnter.play.sound.telephone(Cooldown=2)
CubeFinish.OnTriggerEnter.play.sound.firework(Once)
CubeStart.OnTriggerEnter.timer.start
CubeFinish.OnTriggerEnter.timer.stop(Once)
CubeReset.OnTriggerEnter.timer.reset
CubeMessage(Trigger)(OnEnter=log Checkpoint reached)(OnEnter=status Checkpoint reached)
```

Telephone playback uses the loaded Gym PhoneHandler's notification clip. Firework playback uses the shop GearMarket's purchase SFX bank with its native weighted clip choice, mixer group, pitch and volume settings. Both play at the marker on separate one-shot audio emitters. Sound emitters clear on replace/unload and persist across append until playback finishes.

## All additional tags

| Tag/datatype | Meaning | Example |
| --- | --- | --- |
| `(OnEnter=COMMAND arguments)` | Add an entry callback; repeat the tag for multiple commands, in name order. | `CubeZone(Trigger)(OnEnter=timer.start)` |
| `(Trigger)` | Remove solid collision; retain the visible primitive unless hidden. | `CubeZone(I)(Trigger)(OnEnter=log hello)` |
| `(Once)` | Entry callbacks fire only once per map load, including across player respawns. | `CubeFinish.OnTriggerEnter.timer.stop(Once)` |
| `(Cooldown=SECONDS)` | Minimum interval between entry callbacks, default 0.5; zero is allowed. | `CubeBell.OnTriggerEnter.play.sound.telephone(Cooldown=2)` |
| `(Placeholder)` | Omit the authoring object entirely. Cannot be combined with an event command. | `CubeGuide(Placeholder)` |
| `(Height=NUMBER)` | Finite world Y height for `interface.killfloor.height`; one value per object. | `CubeFloor.OnMapLoad.interface.killfloor.height(Height=-10.5)` |

A dot-event Cube removes its physical/visible authoring primitive automatically; it does not need `(I)` or `(Trigger)`. A separate `(Placeholder)` omits the whole entry, so it cannot run commands. Ordinary solid primitives with OnEnter callbacks still collide; use a separate Cube volume above a standing platform for body-center entry detection. `(Once)` and `(Cooldown)` affect entry behavior, not load-time callbacks.

## Scene restrictions

GymMaps gameplay features only run in the Gym. Matches, the Park, and other scenes block map loading, commands, spawned-button actions, structure summons, safety-platform spawning, and kill-floor checks. Leaving the Gym cancels pending map work and clears owned objects and effects. Returning to the Gym re-enables these features; load the desired map again.
