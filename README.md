# La-Mulana 2 Archipelago v.0.7.0.0
Archipelago mod for La-Mulana 2 using BepInEx.
Functions as a port of the original **[La-Mulana 2 Randomizer](https://github.com/Coookie93/LaMulana2Randomizer)** by **Coookie93**, with additional features for AP.

Current version no longer depends on the original patch and can be run by simply placing the zipped files into the La-Mulana 2 Steam folder.

## Installing La-Mulana 2 Archipelago v.0.7.0.0+
1. Get the latest release of the AP Mod from https://github.com/Crownmuri/LaMulana2Archipelago/releases
2. Put the contents of the zip file into your La-Mulana 2 root folder (`..\Steam\steamapps\common\La-Mulana 2`)

## Preparing the AP world and AP seed
1. Download the La-Mulana 2 APWorld and YAML template here: https://github.com/Crownmuri/Archipelago/releases
2. Install it (put it into the `archipelago/custom_worlds` folder)
3. Customize the YAML template and put it into the `archipelago/Players` folder
4. Start the Archipelago Server
5. Launch La-Mulana 2. On startup it will load some sprites from certain areas. 
6. On the title screen, **LaMulana2Archipelago** will automatically attempt to connect to `localhost:38281` with slotname `Lumisa` first. If the connection fails, you can manually fill in the server, slotname and password in the GUI on the title screen.
7. Once you're connected you're good to go!

## Packaged dependencies
- BepInEx **5** [last built with **5.4.23.4**] (https://github.com/BepInEx/BepInEx/releases)
- Archipelago.MultiClient.Net.dll
- Newtonsoft.Json.dll
- websocket-sharp.dll
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
- [WIP] Potsanity: Currently the first 30 locations (from Village of Departure to Roots of Yggdrasil) are mapped and have their contents shuffled if enabled in the YAML.
- [UNTESTED] Backwards compatibility if there's a `seed.lm2r` present from the original randomizer 

## Issues
- Minor: Currently, ShopDialogPatch only works if AP is connected before the L2ShopDatabase is constructed. Otherwise, NPCs will still just say the vanilla item on purchase prompt. 
- Minor: Some text does not wrap nicely after patching in AP label names.
- Minor: Death Link sometimes not sending out to other players
- Fake items are currently overwritten as the new filler, so there are no actual traps at this point in development.
- There could be some issues not listed here, feel free to share on Discord or on GitHub.

## Food for thought
- Potsanity: Add all static reward pots to the location pool -- not sure if the ammo pots should be included, or just become coin/weight filler.
- Glossanity: Add all static glossary entries to the location pool 
