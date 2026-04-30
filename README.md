# La-Mulana 2 Archipelago v.0.7.2.0
Archipelago mod for La-Mulana 2 using BepInEx.
Functions as a port of the original **[La-Mulana 2 Randomizer](https://github.com/Coookie93/LaMulana2Randomizer)** by **Coookie93**, with additional features for AP.

Current version no longer runs on the original mod and can be run by simply placing the zipped files into the La-Mulana 2 Steam folder.
**NOTE: If you have played on the original randomizer before, you will need to undo the original mod first:**
- Revert the Assembly-CSharp.dll to its original version (either through a backup or Steam verification)
- Remove the Monomod tools from the Managed folder, specifically `MonoMod.Utils.dll`.

## Installing La-Mulana 2 Archipelago v.0.7.0.0+
1. Get the latest release of the AP Mod from https://github.com/Crownmuri/LaMulana2Archipelago/releases
2. Put the contents of the zip file into your La-Mulana 2 root folder (`..\Steam\steamapps\common\La-Mulana 2`)

## Preparing the AP world and AP seed
1. Download the La-Mulana 2 APWorld and YAML template here: https://github.com/Crownmuri/Archipelago/releases (you may also generate a YAML template with the latest version of AP)
2. Install it (put it into the `archipelago/custom_worlds` folder)
3. Customize the YAML template and put it into the `archipelago/Players` folder
4. Start the Archipelago Server
5. Launch La-Mulana 2. On startup it will load some sprites from certain areas. 
6. On the title screen, manually fill in the server, slotname and password into the AP GUI on the bottom left.
7. Once you're connected you're good to go!
8. [Optionally] [Connect the La-Mulana 2 PopTracker pack for auto-tracking](https://github.com/Crownmuri/LaMulana2AP-PopTracker)

## Packaged dependencies
- BepInEx **5** [last built with **5.4.23.4**] (https://github.com/BepInEx/BepInEx/releases)
- [Archipelago.MultiClient.Net.dll](https://github.com/ArchipelagoMW/Archipelago.MultiClient.Net)
- [Newtonsoft.Json.dll](https://github.com/jamesnk/newtonsoft.json)
- [c-wspp.dll](https://github.com/black-sliver/c-wspp-websocket-sharp/releases/tag/v0.4.1)
- ap-icon.png (edited original asset) 

## Features
- **Port of the original randomizer.** 
- **Guardian Specific Ankh Jewels.** If the option is set to `TRUE`, then Ankhs will only activate if the player is holding the specific guardian's Ankh. To help you keep track of which Ankh Jewels you possess, the description of the Ankh Jewel in your inventory will tell you which Guardian Ankhs you are currently holding.
- **Filler distribution is reworked.** Previously, ChestWeight filler would give 1 Weight, FakeItem would show a fake item and disappear, NPCMoney would give 30 coins without popup, FakeScan would give an item dialog popup with "Nothing", and filler in shop would be converted to Weights x 5. Since there is the possibility to receive filler from another world the LM2 filler is reconstructed to always be a distribution of the following items: `1 Coin`,`10 Coins`,`30 Coins`,`50 Coins`,`80 Coins`,`100 Coins`,`1 Weight`,`5 Weights`,`10 Weights`,`20 Weights`. If the filler is inside La-Mulana 2 then the following occurs:
  - Chests: will generate the filler directly as item drops. (LM1 style coin chests let's go)
  - FreeStanding: will show as a Shell Horn, in v.0.7.0.0+ it will now show up as a small indicator above Lumisa.
  - NPC: acts like a regular item grant and will update your resources.
  - Mural: acts like a regular item grant and will update your resources.
  - Shop: instead of falling back to Weights, will act as a regular item purchase showing as Codices (Annoyingly, the Weight sprite comes with the +5). Currently I have set the price multiplier to 0 -- balancing might be required.
- **AP item sprite.** Currently AP items appear as a darkened version of the original AP logo asset.
- **Shops will tell what AP item is for sale.** So that you don't end up wasting money on another world's filler.
- **Three-way Chest Colors.** Regular items, filler items and AP items.
- **DeathLink.** It will trigger the mantra instant death sequence upon receiving a death from AP.  
- **Release upon reaching credits.** If the server is set to release items upon game clear, the flag is set after the sequence at the Cliff transitioning into the credits.
- [WIP] Potsanity: Currently the first 49 locations (from Village of Departure to Annwfn) are mapped and have their contents shuffled if enabled in the YAML.
- Offline Mode: If you wish to just play offline, you can either write seeds through AP or the original randomizer and load the `seed.lm2r` from the title screen.
  - Filepath should be `La-Mulana 2\LaMulana2Randomizer\Seed\seed.lm2r`
  - If you play with an AP generated seed, you can turn on the toggles for AP based filler rewards and restricting ankhs to be guardian specific.

## Issues
- Minor: Only if AP is connected before L2ShopDatabase is constructed, it can patch in the NPC dialog confirmation text. But otherwise you can still rely on the item labels in the shop. 
- Minor: Some text does not wrap nicely after patching in AP label names.
- Minor: Death Link sometimes not sending out to other players
- Fake items are currently overwritten as the new filler, so there are no actual traps at this point in development.
- There could be some issues not listed here, feel free to share on Discord or on GitHub.

## Future plans
- Potsanity: Add all static reward pots to the location pool
- Glossanity: Add all static glossary entries to the location pool 
