# Shipwright — Compile Pal plugin

Publishes the map you just compiled to the Garry's Mod Workshop, as a compile step. It updates the
item the map is already bound to, or creates one, and by default it ships the `.bsp` **without** the
entity lump beside it.

## Install

1. Copy this whole `Shipwright` folder into your Compile Pal `Plugins` directory.
2. Restart Compile Pal.
3. Press **+** on the process list and add **Shipwright**.

The step runs at order 12.5 — after `BSPZIP` (12.1), which is the last step that changes the BSP, and
before `GAME` (13). It only appears for Garry's Mod, because gmad and gmpublish are the only
Workshop publishing tools any Source game gives you.

## It will not publish anything until you say so twice

Out of the box the step is a **dry run**: it packs the addon, lists every file inside it, names the
Workshop item it would update, and stops. Nothing reaches Steam.

- **Actually publish** is what turns it into an upload.
- **Allow creating a new item** is separate, and only matters when no item is bound to the map yet.

Without the second one, a map with no item recorded is skipped with a message rather than published
somewhere new. That is deliberate: "create a new item" is not a sensible thing to do by accident on
every compile.

## Which item it publishes to

Select a map in the queue, expand the **Shipwright** step, and press **Workshop**. That window is
where a map is bound to an item, three ways:

- **Your maps** — everything this Steam account has published, listed by asking `gmpublish list`.
  Needs Steam running and signed in.
- **Paste a link** — the item's page address from your browser, resolved to a title and a picture
  before it is bound. Works with Steam closed.
- **New item** — the title, tags and icon a new item should be created with, checked as you pick them.

Nothing in that window uploads anything.

The answer is stored in `<mapname>.workshop.json` next to your `.vmf`, along with the title and tags
a new item should be created with, when it was last published, the map revision, and a hash of what
was uploaded.

**It is per map, not per preset.** That is why it is not a parameter: a preset applies to every map in
the queue, so an ID typed into one would send every queued map to the same item, each overwriting the
last.

Nothing is ever matched by title. Before overwriting anything, the step asks Steam's public API what
the bound ID actually is and prints the title, so the name of the map you are about to replace is in
the log whether or not you opened the window.

## What the queue tells you

Each queued map's card shows what Shipwright will do to it, before anything is compiled:

| Chip | Means |
|---|---|
| *the item's title* | Bound. A publishing run replaces that item. |
| **will create a new item** | Not bound, and creating one is allowed. |
| **not bound** (red) | Not bound, creating is not allowed, and the step is set to publish. **The compile will not start.** |
| **not bound** (grey) | Not bound, but nothing would be published anyway. |

Pressing Compile with anything to confirm shows one dialog listing exactly which items will be
replaced. If a map is blocking, the dialog says so and the run does not start - which is the point:
the alternative is compiling for an hour and then being told a text file was missing a number.

## The entity lump

If you run the `ENTLUMP` step, your entities are in `mapname_l_0.lmp` beside the BSP and not in the
map. Shipwright leaves that file out of the addon on purpose:

- Clients get a map with no entities, which is not something a decompiler can do much with.
- They do not need them — they are playing on a server, and the server sends them.
- **The server does need them.** Copy the `.lmp` into the server's `garrysmod/maps` folder yourself.

The catch worth knowing before you use this: the engine only accepts a `.lmp` whose map revision
matches the BSP. Every recompile changes the revision, so **every publish means every server needs
the new `.lmp` too**. Until they have it, they load your map with nothing in it. The step prints the
revision on every run for exactly this reason.

Turn on **Include the entity lump** if you would rather publish a map that plays on its own.

## What ends up in the addon

Only what was chosen, copied into a temporary folder that is packed and then deleted:

```
addon.json          generated: title, type "map", your tags
maps/<name>.bsp
maps/<name>.lmp     only with "Include the entity lump"
maps/<name>.nav     only with "Include the nav mesh"
maps/thumb/<name>.png   if you have one
```

Your `.vmf` is never in there, and neither is any other map in your maps folder.

## Requirements

- Steam running, signed in as the account that owns the item. If Steam is running and signed in and
  publishing still fails, Shipwright says whether the problem is Steam's own registration going stale
  — restarting Steam fixes that one, and gmpublish's own message ("Couldn't initialize Steam!") does
  not tell you so.
- Garry's Mod installed — `gmad.exe` and `gmpublish.exe` come from its `bin` folder.
- For a new item, a 512x512 baseline JPEG icon with 4:2:0 chroma. The **Workshop** window checks one
  as soon as you pick it; `shipwright check-icon your.jpg` says the same thing from a terminal.
- A Compile Pal that supports plugin settings windows, for the **Workshop** button — this fork, after
  1.0.2. On anything older every other part still works, and the binding is a text file you edit by
  hand.

Shipwright never asks for, stores or sends a Steam password. Uploads go through the Steam client that
is already signed in, which is also what stops it publishing to an item you do not own.

## Files

| | |
|---|---|
| `shipwright.exe` | The tool. Run it with no arguments for the command list. `inspect` tells you what a publish would ship without touching anything. |
| `shipwright-ui.exe` | The **Workshop** window. Binds a map to an item, or records what a new one should be called. Never uploads. |
| `meta.json` | Tells Compile Pal the step exists, and when to run it. |
| `parameters.json` | The options in the parameter picker. |
