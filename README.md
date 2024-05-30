


<p align="center">
	<img src="https://i.imgur.com/tCQeHxN.png" />
</p>

Introducing OdinOnDemand 1.0: The Ultimate Media Experience for Valheim!

OdinOnDemand (OOD) adds tons of unique mediaplayers to Valheim that allow you to watch YouTube, direct video files, listen to Soundcloud, music, and dynamic radio stations all on in-game screens and radios! It's fully multiplayer synced, low resource, and easy to use. 

### 1.0 brings you a brand new experience with full time sync, new cart & belt player, linkable speakers, dynamic radio stations, new waveform visualizer and more! See documentation and changelog.

Please enjoy OdinOnDemand 1.0!
Moddy

Found a bug? -> [Nexus](https://www.nexusmods.com/valheim/mods/2229?tab=bugs) || [GitHub Issue Tracker](https://github.com/modestimpala/OdinOnDemand/issues)

*ValMedia recommends playing without bloom while watching cinema for the best viewing experience.* 

[<img alt="1.0 Video" height="256px" src="https://i.imgur.com/S8uF1Qb.pnga" />](https://www.youtube.com/watch?v=IKFzOQahupA)
[<img alt="Preview Video" width="256px" src="https://i.imgur.com/0BaY28I.jpg" />](https://www.youtube.com/watch?v=hePW1dueKjE)


[<img width="256" src="https://i.imgur.com/ZR3jYpg.png" />](https://ko-fi.com/modestimpala) 
If you are enjoying the mod, please consider donating to my Ko-Fi. 

## Features

- Universal media support
- Full time sync and multiplayer integration
- Cinema screens
   - Theater Screen, Flatscreen TV, Table TV, Monitor, Old TV, Laptop
- Musicplayers
   - Gramophone, Boombox, Radio, Receiver 
- Speakers that can be linked to any stationary mediaplayer using remote control
  - Linked speakers change center of audio location
- Remote control to use screens from a distance
- Mobile Players; 
  - Cart - "Bard's Wagon",  buildable. Used by pointing and clicking with remote control.
  - Belt - "Skald's Girdle", purchased from Haldor, with configurable recipe. Used by equipping & using remote control when pointing at empty space, e.g. not at a mediaplayer. Admins can point and click at other user's Belt and open it's menu.
- YouTube playlist support
- Unique dynamic radio stations system with easy radio addon support 
- New Music Waveform Visualizer 
  - Configurable scaling factor
- Full mediaplayer configuration
	- Audio control, looping, admin only options
- Custom Piece and Item Recipe Configuration 

YouTube error reporting has been completely changed to increase verbosity. If you find harmless exceptions that could be classified as a user UI message, please open up a suggestion on GitHub.
Please report any other issues on the [Nexus](https://www.nexusmods.com/valheim/mods/2229?tab=bugs) or [GitHub tracker.](https://github.com/modestimpala/OdinOnDemand/issues)

## Installation

Installation of the plugin is fairly straightforward, just install into Bepinex/plugins or use r2modman. **It must be installed on both server and client.**

## Building
To build the project, Nuget restore. Then fix any dependencies in the .csproj file - make sure to use publicized dlls. We use a custom build of YoutubeExplode. You can either grab YoutubeExplode from a package repository or build it yourself. If you grab it from the repository you may experience issues in-game due to the package creators "Deorcify" package, which is why we use a custom build - to remove this package. Simply remove the Deorcify dependency from it's source code and build the dll, copy it over to packages folder and set the hint path. 

## Use

### MediaPlayers
In game, place down a mediaplayer. Interact with it to open the GUI.

#### Remote Playback
You can paste direct links into the URL field to play online remote files. The linked file should be of a [compatible codec](https://docs.unity3d.com/2020.1/Documentation/Manual/VideoSources-FileCompatibility.html).
You can also paste youtube/youtu.be links and the plugin will process this for you on all mediaplayers.

All mediaplayers have the added ability to play audio files of [compatible codecs](https://support.unity.com/hc/en-us/articles/206484803-What-are-the-supported-Audio-formats-in-Unity-), as well as soundcloud.com links - some SoundCloud songs are unavailable, depending on the artist and how they upload/license their art.

#### Local Playback
Mediaplayers can play files from your computer's local filesystem. You can use [absolute and relative path](https://www.computerhope.com/issues/ch001708.htm) lookup for local files.

Absolute path lookup includes a drive letter, and is **not** synced over multiplayer unless players have identical file path structures (which is unlikely). For example, C:\videos\bunny.mp4 or file://C:\videos\bunny.mp4

Relative path lookup begins searching for files from **OdinOnDemand's folder.** Relative path lookup must include prefix `local://` or `local:\\`
To load files from the plugin's folder, identify your media location - for example, a media subfolder in OdinOnDemand's plugin folder - here the path would be`local://movies/media.mp4`  <- insert this into the URL field in-game. 
This would translate into `F:\SteamLibrary\steamapps\common\Valheim\BepInEx\plugins\Valmedia-OdinOnDemand\movies\media.mp4` or wherever the user's Valheim installation may reside.
You can bundle video files with your modpacks or instruct Vikings to place files in appropriate locations for easier synced local playback. Play them in-game for events and lore and more. You can pull from anywhere in the local plugin folder.

#### Youtube Playlists
Mediaplayers have support for Youtube playlists. When a playlist is set, new info will appear in the UI. The Viking who initially sets the playlist handles playlist logic, so if they leave the area or disconnect playlist playback will stop. It is multiplayer synced. Do not skip through tracks too fast. You can choose to shuffle or loop the playlist. If looping, the whole playlist will loop - not individual videos. The last video played will be saved as the autoplay video.

#### Time Sync
Mediaplayers will regularly send out requests to sync time with current mediaplayer (ZDO) owner. You can configure the time between requests sent in the Config file.

To manually seek to a time in the media, use the "Set Time" button under the Cog wheel settings menu. Input time in seconds. 

#### Remote Control

Mediaplayers can be controlled from a distance with a remote control. A remote control can be crafted with 1 bronze. Simply equip, point at a Mediaplayer and click. Point and click at Bard's Wagon to use it. Point and click at no other mediaplayer with Skald's Girdle equipped to use it. Admins can point and click at other player's Skald's Girdle.
You can link speakers with mediaplayers using the secondary remote control action. Select a speaker, then select a mediaplayer to pair. Repeat this process to unpair. You can view speaker count and unlink all speakers from the mediaplayer's settings panel.
Config options include key configs, max controller distance, and an option to make remote control access private. 

#### Locking

Mediaplayers can be locked in the top right corner of the menu. When a Mediaplayer is locked, Vikings can only access it if they have access to the private area created by a Ward. 
Admins can lock down mediaplayers from the cog menu, preventing normal Vikings from interacting with specific screens.

#### Additional Options

The cog button in the bottom right displays extra options. Options for admin only access, listening distance and master volume exist here.

- Master volume controls the overall volume level for that type of mediaplayer, from every single mediaplayer. It is a client sided option designed to allow Vikings to mute or boost audio for mediaplayers to their personal liking. There is an individual mixer for each type of media player - Cinema screens, radios, and mobile players.
- Listening distance is a **multiplayer synced** parameter that changes how far away audio is heard from individual mediaplayers. This can be limited in the plugin's config, which is not enforced on admins.

Default values and client sided master volume values can also be changed in the config.

Most features are synced over multiplayer through RPC events including player time. Volume is client sided. 

#### Waveform visualizer

You can configure the scale factor of the audio waveform visualizer from the config settings. If you play with the volume turned down, you will want to turn this up.

## Dynamic Radio Stations
To use the Dynamic Audio Loader, you need to prepare your audio files and additional assets in a specific format. You can create your own station from an asset bundle or from a folder containing audio files, then distribute them via mod repositories or modpacks.

**Note**: File names cannot include the character "#".

### Create From Asset Bundle

1.  **Prepare Asset Bundle:**
    
    -   Your asset bundle should contain all the audio clips you want to include in your station.
    -   Include a `title` text asset that contains the name of your station.
    -   Optionally, include a `thumbnail` sprite asset to represent your station visually. **Ensure it is set to "Sprite" type asset in the properties Inspector.**
    -   If you want your station to play tracks in a random order, include a `shuffle` text asset.
      <img src="https://i.imgur.com/46H7nqw.png" />
2.  **Loading Station:**
    
    -   The station will be automatically created when the asset bundle is placed in (Plugin Path, e.g. OdinOnDemand)\assetbundles.

### Create From Folder

1.  **Prepare Folder:**
    
    -   Place all your audio files (supported formats: `.ogg`, `.wav`, `.mp3`, `.flac`) in a single folder.
    -   Create a `title.txt` file containing the name of your station and place it in the same folder.
    -   Optionally, add an image file named `thumbnail` with one of the supported extensions (`.png`, `.jpg`, `.jpeg`, `.bmp`, `.tga`, `.gif`) to be used as the station's thumbnail.
    -   If you want your station to play tracks in a random order, create an empty file named `shuffle.txt` in the folder.
2.  **Loading Station:**
    
    -   The station will be automatically created when the folder is placed in (Plugin Path, e.g. OdinOnDemand)\radio.

### Radio Station Addons - [Example Mod](https://valheim.thunderstore.io/package/ValMedia/VikingRadioStation_OdinOnDemand/)
To create a custom radio station addon for easy distribution, follow these simple steps:

 1. Create your station folder
 2.    **Create Essential Files**:
       - **odinondemand.txt:** This file signifies that the folder is a radio station addon. Simply create an empty `odinondemand.txt` file in your station's folder.
       - **title.txt:** This file should contain the name of your radio station. Create a `title.txt` file in your station's folder and write your station's name in it.
 3. **Add Audio Files:** Include your audio files (.ogg, .wav, .mp3, or .flac) in the station's folder. The plugin will recognize these files as part of your radio station.
 4. **(Optional) AssetBundle Support:** If you prefer using AssetBundles, follow the instructions above to create your AssetBundle, then place your AssetBundle file in your station folder with the file extension ".assetbundle".
 5. Zip the files up inside for upload to Thunderstore / Nexus, or place the folder in Valheim\BepInEx\plugins\

### Station Playback

When you the game world is initialized, the Server Station Manager initiates the playback simulation:

1.  **Track Selection:** If shuffle mode is enabled, the tracks are shuffled to create a random playback order. Otherwise, tracks are played in the order they appear in the station.
    
2.  **Synchronization:** In multiplayer games, the Station Manager ensures that all players hear the same track at the same time, creating a shared audio experience. This is done by regularly syncing the current track and playback time with all players.
    
3.  **Track Advancement:** Tracks are played in sequence. When one track ends, the next one begins. If shuffle is enabled, the order will change after each full cycle through the station tracklist.
    
4.  **Looping:** The station continuously loops through its tracklist. Once the last track finishes, playback returns to the first track (or a shuffled track if shuffle is enabled).

The server will simulate station playback position or "time" and send this to players when needed.
Once a player initiates playback of a station via a mediaplayer, they receive the most recent station data from the server, load the radio station client side, and play the media. 

**Clients and servers must have identical files for proper playback.**

## Config

Starting with OOD 1.0, config files are now stored in BepInEx\config\OdinOnDemand\
The config includes settings for YouTube API selection, volume control, distance parameters for audio playback, server and client-side configurations, remote control functionalities, VIP mode settings, and audio fade options. It allows for extensive customization of media player behavior in the game, including options for enabling or disabling certain features, adjusting volumes, setting distances for audio reception, and specifying admin-only settings.

## Recipes

Recipes (piece and items) can now be tweaked through a json file located at BepInEx\config\recipes.json & recipes_item.json. the mod will automatically create these. However the default recipe files can be found [here](https://github.com/modestimpala/OdinOnDemand/tree/main/plugin/Assets) if needed. 

Some helpful links to help you edit this file are here

[Beautify/Validate JSON](https://codebeautify.org/jsonviewer)
[Valheim Item List](https://valheim-modding.github.io/Jotunn/data/objects/item-list.html)

Do not change item names unless you want them removed. If you have any issues keep in mind you can delete your .json file and the mod will re-create it with default values.

## VIP System (Beta)

When enabled, Only VIPs can place, remove, and interact with OdinOnDemand Pieces (admins excluded)

Enabled via Config files. Set VIP Mode to True, and add SteamIDs for VIPs. You may customize the message as well. The system is very barebones, so please provide any feedback to Github or Nexus.

## Known Issues

- Boomboxes are currently difficult to place. Just.. keep trying. They place easier on terrain it seems. I'm not quite sure why this is happening. Gramophones are not affected. 

### If you have V-Sync force enabled in your GPU driver control panel, the mod may not function. 

![V-Sync Screenshot](https://i.imgur.com/JZTG3CZ.png)

If the mod fails to play any sort of video file, not just YouTube - Please check your GPU driver's control panel and make sure the vertical sync setting is either identical your in-game settings or set to "match application setting". You can also check your Player.log. If there are errors about Direct3D and refresh rate / v-sync you may have to do this. 

## Screenshots 
![Mod Screenshot](https://cdn.discordapp.com/attachments/602048405702705173/1076343490625151078/valheim_pqt3uMxRgR.png)
![Mod Screenshot](https://i.imgur.com/IOYPOAa.jpeg)
![Mod Screenshot](https://i.imgur.com/QL6gvwc.jpg)
![Mod Screenshot](https://i.imgur.com/Y88KuWV.jpg)
![Mod Screenshot](https://i.imgur.com/wTmD6Cc.jpeg)
<sub>[Build by DanAugust](https://www.valheimians.com/build/small-simple-cabin-pre-plains/) Last picture is with back-light setting turned off.</sub>

For any tech-savvy Vikings out there, there is a backup YouTube api that can be self hosted using Node and youtube-dl. This is **completely optional** and not required as long as the built in library is maintained (YouTubeExplode)

[Tutorial Video](https://www.youtube.com/watch?v=9_vs8MItO38)

[Github Instructions](https://github.com/modestimpala/OdinOnDemand)

## Acknowledgements

 - [SoundCloudExplode](https://github.com/jerry08/SoundCloudExplode) | [YouTubeExplode](https://github.com/Tyrrrz/YoutubeExplode) | [youtube-exec-dl](https://www.npmjs.com/package/youtube-dl-exec) | [nodejs](https://nodejs.org/en/) | [yt-dlp](https://github.com/yt-dlp/yt-dlp)
 - Inspired by [Raft Cinema Mod](https://www.raftmodding.com/mods/cinema-mod)
 - Special shoutout to the [Valhalla server](https://valheim.thunderstore.io/package/FreyaValhalla/Valhalla_Dedicated/) Community and Administration
 - Any and all other project supporters - thanks for all the interest and support along the way.

## NodeJS Server Installation

### NodeJS Server has not been updated in a long time may not work properly 

No need to use an external server now, but it's still there if you want it. yt-dlp may often return more consistently and with better quality videos. 
There is also the self-hosted benefit.

To expose NodeJS server config settings, set API type to NodeJS and run the plugin once. It will populate your config with new settings to allow setup of NodeJS. We hide these to avoid player confusion when using YoutubeExplode. 

[Tutorial Video](https://www.youtube.com/watch?v=9_vs8MItO38)

Setup of YouTube functionality is a little more involved. 
After multiple attempts I could not get any YouTube library to work so we're grabbing
YouTube links through 
[youtube-exec-dl](https://www.npmjs.com/package/youtube-dl-exec) which uses
[nodejs](https://nodejs.org/en/) to interact with
[yt-dlp](https://github.com/yt-dlp/yt-dlp). This solution is not great so see notes if you might have any ideas.

First you will need nodejs installed on a computer. Then you will need to install youtube-dl-exec and express with
```bash
npm install youtube-dl-exec --save
npm install express --save
```
Then in the working directory, place server.js and start.sh from the release zip. You can find this on github or Nexus.
You can configure the server's port through 
```
const port = process.env.PORT || 3000;
```
You must change the auth code at the top of the file. Generate one of a reasonable length [here](https://generate.plus/en/base64).
```
const authString = "CHANGEME=";
```
In the working directory execute 
```bash
./start.sh
or
node server.js
```
This will start the server and first command enables logging. It logs messages 
into log.txt and errors into err.txt, then follows the output of log.txt.
 
Then configure YouTube in the mod config through either an in-game GUI plugin 
or by modifying the appropriate values in BepInEx\config\com.donkboys.OdinOnDemand.cfg.

Is Youtube Enabled must be "true". The NodeJS URL must be set to your server like:
```
http://127.0.0.1:8080/yt/
```
Replace the IP and port with your server's IP and port. 
Set your auth code identical to the one in server.js.

The server does not actually download any files, just returns urls.

Keep in mind this server is very barebones but at least has basic error handling,
input sanitization and authentication. It does not yet fully handle crashes, so if the node server 
somehow fails to catch an exception and exits it will not auto-restart.
Additionally, please practice good saftey standards when opening up a server application
to the internet. You could alternatively use a program like ZeroTier to avoid exposing a port publicly. 
