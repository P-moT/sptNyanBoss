# Attribution and third-party material

NyanBoss's own code is licensed under the GNU General Public License v3.0 only (GPL-3.0-only); see `LICENSE`.

The material listed below is **not** NyanBoss code. It is not covered by the GPL, and the GPL grants no rights to it. Each item keeps its own licence or copyright, as described in its section.

## Map images

**Files:** `wwwroot/maps/*.svg` (Customs, Factory, Ground Zero, Interchange, Lighthouse, Reserve, Shoreline, Streets of Tarkov, Woods)

- **Source:** the Escape from Tarkov SVG Maps Project, https://github.com/the-hideout/tarkov-dev-svg-maps
- **Authors:** Shebuka and the project's contributors
- **Licence:** Creative Commons Attribution-NonCommercial-ShareAlike 4.0 International (CC BY-NC-SA 4.0). The full text is in `wwwroot/maps/LICENSE.md`, and online at https://creativecommons.org/licenses/by-nc-sa/4.0/
- **Changes:** none. The files are distributed unmodified.
- **Alignment data:** the coordinates used to line the images up with the game come from tarkov.dev, https://github.com/the-hideout/tarkov-dev

**How NyanBoss uses them:** the maps are shown only as backgrounds on the NyanBoss page of SPT's Server Information Center, where a server admin places the areas the boss patrols. They are not used in the game client.

**Conditions that come with them:**

- The licence is non-commercial. Neither the maps nor any copy or derivative of NyanBoss that includes them may be sold, or used for commercial purposes.
- Anything that redistributes the maps must keep this credit and `wwwroot/maps/LICENSE.md` with them. The credit also appears on the page itself.
- The map project forbids using its assets in software that facilitates cheating or gives an unfair advantage in Escape from Tarkov. NyanBoss uses them only in the offline, single-player SPT game.

The same details are in `wwwroot/maps/ATTRIBUTION.md`, beside the files.

## Music

The boss plays the Nyan Cat song while he is alive.

- **Song:** "Nyanyanyanyanyanyanya!" by daniwell (daniwell-P).
- **Rights:** the song is not openly licensed. All rights belong to its owners. It is included in NyanBoss without charge, for a non-commercial fan mod, and is not covered by the GPL.
- **File:** `BepInEx/plugins/NyanBoss/nyan.ogg`

If you are a rights holder and want the song removed, open an issue or contact the author, and it will be taken out of future releases.

The boss works normally without the file, and the client log notes that no music was found. To use different music, replace the file with your own `nyan.ogg` (`nyan.wav` and `nyan.mp3` also work). Building with `-p:IncludeMusic=false` makes a package without it.

## Item models

**Files:** `bundles/nyanboss/item_equipment_helmet_ulach_nyan.bundle`, `bundles/nyanboss/patron_556x45_nyan.bundle`

- The Nyan ULACH is a modified version of the HighCom Striker ULACH IIIA helmet from Escape from Tarkov. The NyanBoss changes are the cat ears, the recoloured shell texture and the setup that makes it a separate item.
- The 5.56x45mm Nyan round is a modified version of the game's 5.56x45mm M855A1 cartridge. The NyanBoss change is the rainbow-painted tip.

Their models and textures are derived from the game's own assets, which are the property of Battlestate Games. The bundles are not covered by the GPL. They are included only so that the items display in SPT, which already requires a legitimate copy of Escape from Tarkov.

## Names and characters

"Nyan Cat" and the Nyan Cat character are the work of Chris Torres, and their rights belong to their owners. Escape from Tarkov and its items, names and artwork belong to Battlestate Games. NyanBoss is an unofficial fan mod. It is not affiliated with, endorsed by or sponsored by Chris Torres, daniwell or Battlestate Games.
