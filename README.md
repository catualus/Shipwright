# Shipwright

**Publishes your compiled map to the Garry's Mod Workshop, straight from Compile Pal.**

[![CI](https://github.com/catualus/Shipwright/actions/workflows/ci.yml/badge.svg)](https://github.com/catualus/Shipwright/actions/workflows/ci.yml)

Shipwright runs as a compile step. When a map finishes compiling it packs the `.bsp` into a `.gma` and
updates the Workshop item that map is bound to — or creates one — without leaving Compile Pal, opening
a browser or touching a command line.

Each map is set up in its own window: which item it publishes to, what goes in it, and whether it
publishes at all. Nothing is uploaded until you turn it on for that map.

---

## Contents

- [What it does](#what-it-does)
- [Installation](#installation)
- [Setting up a map](#setting-up-a-map)
- [What gets published](#what-gets-published)
- [The entity lump](#the-entity-lump)
- [Requirements](#requirements)
- [Limitations](#limitations)
- [Building from source](#building-from-source)
- [Licence](#licence)

---

## What it does

- **Publishes on compile.** The step runs after packing and repacking, so what goes up is the map you
  just built.
- **Binds a map to an item, once.** Pick from what your account has published, or paste an item's
  address. The choice is stored beside the map rather than in a preset — so a queue of eight maps
  publishes to eight items, not one.
- **Leaves the entity lump out.** A map published this way has no entities in it to decompile. See
  [The entity lump](#the-entity-lump).
- **Says what will happen first.** Each queued map's card shows what it will do, and a compile that
  would publish asks first, naming the item it is about to replace.
- **Refuses to waste a compile.** A map set to publish with nothing to publish to stops the run before
  VBSP starts, rather than an hour later.
- **Skips uploads nobody needs.** An addon identical to the one published last time is not uploaded
  again, and the same item is not updated twice within a few minutes.

---

## Installation

1. Download the latest release and unzip it.
2. Copy the `Shipwright` folder into your Compile Pal `Plugins` directory.
3. Restart Compile Pal, press **+** on the step list, and add **Shipwright**.

The step appears because the folder is there, and disappears if you delete it. It only shows up for
Garry's Mod.

It needs a Compile Pal that supports plugin settings windows — [this
fork](https://github.com/catualus/CompilePal), after 1.0.2.

---

## Setting up a map

Select a map in the queue, expand the **Shipwright** step and press **Workshop**. The window opens on
that map and shows what it is: size, map revision, whether the entities have been moved out, and
whether a nav mesh is beside it.

| Tab | For |
|---|---|
| **Publishing** | Whether this map publishes, whether an item may be created for it, the change note, what ships beside the map, and how often the item may be updated. |
| **Your maps** | Everything your Steam account has published, filtered to maps. |
| **Paste a link** | Bind by pasting an item's address. Works with Steam closed. |
| **New item** | The title, tags and icon a new item is created with. |

Nothing in the window uploads anything. It records what the next compile should do.

---

## What gets published

A `.gma` containing exactly this:

```
addon.json              generated: title, type "map", your tags
maps/<name>.bsp
maps/<name>_l_0.lmp     only if you ask for it
maps/<name>.nav         only if you ask for it
maps/thumb/<name>.png   if you have one
```

Everything is copied into a temporary folder and packed from there, so nothing else in your maps
directory can end up in the addon. Your `.vmf` never goes near it.

An item's description, images and visibility are set on its Workshop page. gmpublish cannot change
them, so neither can this.

---

## The entity lump

Compile Pal's `ENTLUMP` step moves a map's entities into a `<name>_l_0.lmp` file beside the `.bsp`.
Shipwright leaves that file out of the addon by default, which is the point:

- Subscribers download a map with no entities in it, which a decompiler can do little with.
- They do not need them — the server they join sends the entities.
- **Your servers do.** Copy the `.lmp` into each server's `garrysmod/maps` folder.

The engine only accepts a `.lmp` whose **map revision** matches the `.bsp`. Every recompile changes
that number, so every publish means every server needs the new `.lmp` too — until it has one, the map
loads empty with no error. Shipwright prints both revisions on every run and refuses to ship a `.lmp`
that does not match.

Turn on **Entity lump** in the window to publish a map that plays on its own.

---

## Requirements

- Garry's Mod installed. `gmad.exe` and `gmpublish.exe` come from its `bin` folder.
- Steam running, signed in as the account that owns the item.
- For a new item, a 512×512 baseline JPEG icon with 4:2:0 chroma. The window checks yours as soon as
  you pick it.

Shipwright never asks for, stores or sends a Steam password. Uploads go through the Steam client you
are already signed in to, which is also what stops it publishing to an item you do not own.

---

## Limitations

- **Garry's Mod only.** It is the only Source game whose Workshop can be published to from a command
  line. CS2 uses its Workshop Manager, and TF2 and CS:GO publish from inside the game.
- **One item per map.** Publishing several maps to a single item is not something this does.
- **Title, tags and icon apply to new items.** Changing them on an item that already exists is done on
  its Workshop page.
- **A new item stays hidden** until the Steam Workshop legal agreement is accepted on its page.

---

## Building from source

Needs the .NET 10 SDK. Windows only.

```
dotnet test Shipwright.slnx
./build-plugin.ps1
```

`build-plugin.ps1` writes `artifacts/Shipwright`, the folder to copy into `Plugins`. Add `-Zip` for
the archive attached to a release.

The command line half works on its own — run `shipwright.exe` with no arguments for the list.
`inspect` says what a publish would ship without touching anything.

---

## Licence

GPL-3.0. See [LICENSE](LICENSE).
