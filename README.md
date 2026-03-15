# La-Mulana 2 Archipelago v.0.1.0.0
Archipelago mod using BepInEx 5 for the **La-Mulana 2 Randomizer** by **Coookie93** (https://github.com/Coookie93/LaMulana2Randomizer).

In its current alpha state, you will require to install the original randomizer prior to modding in Archipelago.

## Installing the La-Mulana 2 Randomizer with AP mod
### (Original instructions from the La-Mulana 2 Randomizer)
1. Get the latest release of Coookie93's randomizer from https://github.com/Coookie93/LaMulana2Randomizer/releases
2. Place the `LaMulana2Randomizer` folder in the La-Mulana 2 root directory, the one with `lamulana2.exe`
3. Copy all the files from the `LaMulana2Randomizer/Monomod` folder to the `LaMulana2_Data/Managed` folder
4. Now in the `LaMulana2_Data/Managed` folder, drag the `Assembly-CSharp.dll` onto `monomod.exe`
5. Make a backup of `Assembly-CSharp.dll` eg. create an `Original` folder inside `LaMulana2_Data/Managed` and place the file in there.
6. Rename the `MONOMODDED_Assembly-CSharp.dll `file to `Assembly-CSharp.dll`
### (Additional instructions to set up the Archipelago mod)
7. Backup and delete `MonoMod.Utils.dll` inside `LaMulana2_Data/Managed` -- It will conflict with BepInEx's own patcher.
8. Get the latest release of the AP Mod from https://github.com/Crownmuri/LaMulana2Archipelago/releases
9. Put the contents of the zip file into your La-Mulana 2 root folder (`..\Steam\steamapps\common\La-Mulana 2`)

## Preparing the AP world and AP seed
1. Download the La-Mulana 2 APWorld and YAML template here: https://github.com/Crownmuri/Archipelago/releases
2. Install it (put it into the `archipelago/custom_worlds` folder)
3. Customize the YAML template and put it into the `archipelago/Players` folder
4. Generate the seed and hope it doesn't result in a FillError
6. The output will contain an `AP_[#####]_P[#]_[PlayerName].lm2r` seed file
7. Rename your seed file to `seed.lm2r` and put it in the original LaMulana2Randomizer's seed folder:
`..\Steam\steamapps\common\La-Mulana 2\LaMulana2Randomizer\Seed`
8. Start the Archipelago Server
9. Launch La-Mulana 2.
10. **LaMulana2Randomizer** will start patching the game based on the seed, and **LaMulana2Archipelago** will automatically attempt to connect to `localhost:38281` with slotname `Lumisa` first. If the connection fails, you can manually fill in the server, slotname and password in the GUI on the title screen.
11. Once you're connected you're good to go!

## Packaged dependencies
- BepInEx **5** [last built with **5.4.23.4**] (https://github.com/BepInEx/BepInEx/releases)
- Archipelago.MultiClient.Net.dll
- Newtonsoft.Json.dll
- websocket-sharp.dll

## Features
Most original randomizer features are maintained through the seed reading from L2Rando. 
However, some adaptations are made to accommodate AP features.
- **Guardian Specific Ankh Jewels.** If the option is set to `TRUE`, then Ankhs will only activate if the player is holding the specific guardian's Ankh. To help you keep track of which Ankh Jewels you possess, the description of the Ankh Jewel in your inventory will tell you which Guardian Ankhs you are currently holding.
- **Filler distribution is reworked.** Previously, ChestWeight filler would give 1 Weight, FakeItem would show a fake item and disappear, NPCMoney would give 30 coins without popup, FakeScan would give an item dialog popup with "Nothing", and filler in shop would be converted to Weights x 5. Since there is the possibility to receive filler from another world I reconstructed LM2 filler to always be a distribution of the following items: `1 Coin`,`10 Coins`,`30 Coins`,`50 Coins`,`80 Coins`,`100 Coins`,`1 Weight`,`5 Weights`,`10 Weights`,`20 Weights`. If the filler is inside La-Mulana 2 then the following occurs:
  - Chests: will generate the filler directly as item drops. (LM1 style coin chests let's go)
  - FreeStanding: will show as a Shell Horn, picking it up acts like a regular item grant and will update your resources.
  - NPC: acts like a regular item grant and will update your resources.
  - Mural: acts like a regular item grant and will update your resources.
  - Shop: instead of falling back to Weights, will act as a regular item purchase. Currently I have set the price multiplier to 0 -- balancing might be required.
- **AP item sprite.** Currently AP items appear as Holy Grail (custom AP sprite to be implemented)
- **Shops will tell what AP item is for sale.** So that you don't end up wasting money on another world's filler.
- **Three-way Chest Colors.** Regular items, filler items and AP items.
- **DeathLink.** It will trigger the mantra instant death sequence upon receiving a death from AP.  
- **Release upon reaching credits.** If the server is set to release items upon game clear, the flag is set after the sequence at the Cliff transitioning into the credits.

## Issues
- Upon player control (starting the game or loading a save), La-Mulana 2 will always dequeue all previously obtained AP items regardless of whether your save already has them. 
- Related: there is no save tracking other than the grail auto-save; i.e. only on death the mod will try to regrant the AP items missing since last auto-save.
- AP Item label patching for shops may fire too early, causing AP shop labels to return as `DATA Err SheetNo=0 IDNo=-1 RetuNo=3`
- Some text does not wrap nicely after patching in AP label names.
- Death Link sometimes not sending out to other players
- Fake items are currently overwritten as the new filler, so there are no actual traps at this point in development.
- Note: You could generate a non-multiworld solo seed to have Guardian Specific Ankh Jewel logic -- but you will need to be connected to the AP server running the seed to have Ankh react accordingly, as that setting is currently only passed through the server, not in the seed.
- There could be some issues not listed here, feel free to share on Discord or on GitHub.
