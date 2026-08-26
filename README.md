# Shipwright

Publishes a compiled Source map to the Garry's Mod Workshop, as a Compile Pal compile step — updating
the item the map is bound to, or creating one, and leaving the entity lump out of the addon.

It is also a standalone command line tool: `shipwright inspect map.bsp` says what a publish would
ship and which item it would go to, without touching Steam or the network.

## Status

**Scaffold.** The inspection, staging, validation and decision code is written and tested. What has
not happened yet is a real publish against a real Workshop item — see
[Before the first real publish](#before-the-first-real-publish). Until that is done, run it as a dry
run and publish by hand.

## Why this is Garry's Mod only

It is the only Source game whose Workshop can be published to from a command line. `gmad.exe` and
`gmpublish.exe` ship with the game and upload through the Steam client that is already signed in.
CS2 removed console publishing in favour of the Workshop Manager GUI, CS:GO's publish tool was in the
game, and TF2's is a page in the game's UI. `CompatibleGames: [4000]` in `meta.json` is that fact.

## The security posture, in short

Publishing is public, irreversible for everyone who already subscribed, and easy to do by accident on
every compile. So:

- **No credentials, ever.** Uploads go through the running Steam client. There is no login parameter,
  no token file, and no SteamCMD path — `+login user pass` would put a password on a command line
  that Compile Pal writes verbatim into `debug.log` and the compile log, which are the files people
  paste into Discord when a compile fails. Steam, not this tool, decides whether the account owns the
  item.
- **Nothing is published without two separate opt-ins.** The step is a dry run until *Actually
  publish*; creating a new item needs *Allow creating a new item* on top of that.
- **The item is bound explicitly, never matched by name.** A `<mapname>.workshop.json` beside the
  `.vmf` holds the ID. Before an update, the ID is looked up through Steam's keyless public API and
  the item's real title is printed, so overwriting the wrong map is something you see rather than
  something you discover.
- **Only chosen files are packed.** Everything is copied into a fresh temporary directory and gmad is
  pointed at that. Pointing gmad at the game's `maps` folder — the one-line version of this tool —
  would publish every map on the machine, and there is no undo for that.
- **Free text is sanitised and passed as an argument array.** Compile Pal concatenates parameter
  values into a command line without quoting them, so a change note containing a quote can otherwise
  re-split the arguments of the process it reaches.
- **Nothing this tool prints can command the host.** Compile Pal treats a plugin's stdout line
  beginning `COMPILE_PAL_SET` as an instruction to rewrite the game configuration, including the path
  to vbsp. Every line goes out through `Log`, which neutralises that token, and forwarded gmad and
  gmpublish output is additionally prefixed.
- **No personal data is read or printed.** Which Steam account is signed in is Steam's business; this
  tool checks that a process is running and nothing more.

## The entity lump, and the thing that will bite you

With Compile Pal's `ENTLUMP` step, the entities live in `mapname_l_0.lmp` beside the BSP. Shipwright
leaves it out of the addon, which is the point: clients download a map with no entities to decompile,
and get their entities from the server they join.

The engine only accepts a `.lmp` whose **map revision** matches the BSP. A Workshop update pushes a
new BSP — a new revision — to every subscriber in minutes, while the `.lmp` on a server is whatever
someone copied there. So every publish means every server needs the new lump file, and until it has
one the map loads empty with no error. Shipwright prints both revisions on every run and records the
published one in the state file; it will not ship a `.lmp` whose revision does not match.

## Layout

| | |
|---|---|
| `Shipwright/` | The library: everything real. |
| `ShipwrightCli/` | The `shipwright.exe` entry point, four lines of it. |
| `Shipwright.Tests/` | xUnit tests, fixtures synthesised rather than checked in. |
| `CompilePalPlugin/Shipwright/` | `meta.json`, `parameters.json` and the plugin's own README. |
| `build-plugin.ps1` | Publishes the executable and assembles `artifacts/Shipwright/`. |

## Building

```
dotnet test Shipwright.slnx
./build-plugin.ps1
```

Then copy `artifacts/Shipwright` into your Compile Pal `Plugins` folder.

## Before the first real publish

Each of these is answerable in ten minutes against a throwaway Workshop item, and each one changes
code that is currently written to an assumption:

1. **Does `gmpublish create` print the new item's ID, and in what form?** `GmodTools.ParseCreatedId`
   takes the last long number in the output, which is a guess. If the ID cannot be recovered, an item
   exists with no record of it and the next run would create a second one — the code treats that as
   an error and says what to do, but the right answer is to parse the real format.
2. **What visibility does a newly created item get, and does the Workshop legal agreement leave it
   hidden?** If create cannot produce a hidden item, that is another argument for it staying an
   explicit opt-in.
3. **Does `gmpublish update` require the item to be public?** Reported by users, not documented.
4. **Does gmpublish work while Garry's Mod is running?** Currently a warning. If it is unreliable,
   make it a refusal, as `NAV` and `CUBEMAPS` already do for the game being open.
5. **Where does a Garry's Mod addon stop packing?** There is a size ceiling and gmad's compression
   fails below it; the staging report should say when a map is near it rather than after.

## Licence

GPL-3.0, the same as Compile Pal and Meshwright. See [LICENSE](LICENSE).
