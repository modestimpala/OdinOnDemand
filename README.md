


# OdinOnDemand

OdinOnDemand (OOD) adds several forms of multimedia to Valheim that allow players to natively watch YouTube as well as direct video files on in-game screens. The mod also offers two types of musicplayers that can play YouTube, SoundCloud links, as well as direct audio files --  additionally, all of this is multiplayer compatible and seemingly relatively low-impact.

ValMedia recommends playing without bloom while watching for the best viewing experience. 

### Due to Valheim's recent upgrade to Unity version 2020.3.45, you may experience crashes during video playback because of a new [VideoPlayer bug](https://forum.unity.com/threads/crash-windowsvideotextureoutput-releasedrawnsamples-bool.1396480/). This may be related to MP4 files. We recommend utilizing VP8 codec videos (webm) or MP3/SoundCloud options as a temporary measure. If a fix is discovered an update will be pushed out, otherwise we're waiting for another Unity update. Apologies for any inconvenience. - ValMedia

0.9.85 introduces new assets, relative path lookup from plugin dir (local://) and more. Check out the updated documentation below.

[<img alt="Preview Video" width="256px" src="https://i.imgur.com/0BaY28I.jpg" />](https://www.youtube.com/watch?v=hePW1dueKjE)

I am available in the larger Valheim / Jotunn modding Discords for contact under ModestImpala.

## Features

- Cinema screens
   - Theater Screen, Flatscreen TV, Table TV, Monitor, Old TV, Laptop
   - Plays YouTube, Remote and Local files
- Musicplayers
   - Gramophone, Boombox, Radio
   - Plays SoundCloud, YouTube, Remote and Local files
- Speakers, Receiver props
- YouTube playlist support
- Remote control to use screens from a distance
- Multiplayer functionality
- Full mediaplayer configuration
	- Audio control, forward tracking, looping, autoplay, admin only options
- Piece Recipe Configuration (item recipes coming soon)

This mod is in beta and has not been extensively tested. Please report any issues on
the Nexus or GitHub tracker.

## Installation

Installation of the plugin is fairly straightforward, just install into Bepinex/plugins or use r2modman. **It must be installed on both server and client.**

## Building
To build the project, Nuget restore. Then fix any dependencies in the .csproj file - make sure to use publicized dlls. We use a custom build of YoutubeExplode. You can either grab YoutubeExplode from a package repository or build it yourself. If you grab it from the repository you may experience issues in-game due to the package creators "Deorcify" package, which is why we use a custom build - to remove this package. Simply remove the Deorcify dependency from it's source code and build the dll, copy it over to packages folder and set the hint path. 

## Use

### MediaPlayers
In game, place down a cinema screen or radio. Interact with it to open the GUI.

#### Remote Playback
You can paste direct links into the URL field to play online remote files. The linked file should be of a [compatible codec](https://docs.unity3d.com/2020.1/Documentation/Manual/VideoSources-FileCompatibility.html).
You can also paste youtube/youtu.be links and the plugin will process this for you on both cinema screens and radios.

Radios have the added ability to play audio files of [compatible codecs](https://support.unity.com/hc/en-us/articles/206484803-What-are-the-supported-Audio-formats-in-Unity-), as well as soundcloud.com links - some SoundCloud songs are unavailable, depending on the artist and how they upload/license their art.

#### Local Playback
Mediaplayers can also play files from your computer's local filesystem. You can use [absolute and relative path](https://www.computerhope.com/issues/ch001708.htm) lookup for local files.

Absolute path lookup includes a drive letter, and is **not** synced over multiplayer unless players have identical file path structures (which is unlikely). For example, C:\videos\bunny.mp4 or file://C:\videos\bunny.mp4

Relative path lookup begins searching for files from **Bepinex's plugin folder.** Relative path lookup must include prefix `local://` or `local:\\`
To load files from the plugin's folder, identify your media location - for example, the OOD plugin folder name. For r2modman installations, it's usually "ValMedia-OdinOnDemand" - here the path would be`local://Valmedia-OdinOnDemand/media.mp4`  <- insert this into the URL field in-game. 
This would translate into `F:\SteamLibrary\steamapps\common\Valheim\BepInEx\plugins\Valmedia-OdinOnDemand\media.mp4` or wherever the user's Valheim installation may reside.
You can bundle video files with your modpacks or instruct Vikings to place files in appropriate locations for easier synced local playback. Autoplay them in-game for events and lore and more. You can pull from anywhere in the local plugin folder.

#### Youtube Playlists
Mediaplayers have support for Youtube playlists. When a playlist is set, new info will appear in the UI. The Viking who initially sets the playlist handles playlist logic, so if they leave the area or disconnect playlist playback will stop. It is multiplayer synced. Do not skip through tracks too fast. You can choose to shuffle or loop the playlist. If looping, the whole playlist will loop - not individual videos. If autoplay enabled, the last video played will be saved as the autoplay video when the Viking unloads the mediaplayer object.

#### Remote Control

Mediaplayers can be controlled from a distance with a remote control. A remote control can be crafted with 1 bronze. Simply equip, point at a Mediaplayer and click.
Config options include key configs, max controller distance, and an option to make remote control access private. 

#### Locking

Mediaplayers can be locked in the top right corner of the menu. When a Mediaplayer is locked, Vikings can only access it if they have access to the private area created by a Ward. 
Admins can lock down mediaplayers from the cog menu, preventing normal Vikings from interacting with specific screens.

#### Additional Options

The cog button in the bottom right displays extra options. Options for autoplay, admin only access, listening distance and master volume exist here.

Autoplay does not currently support playlists. If a viking is playing a playlist with autoplay enabled and unloads the mediaplayer object, the autoplay video will be set to the last played video.

Autoplayed videos are not synced. **Vikings will load videos upon mediaplayer load-in.**

- Master volume controls the overall volume level for that type of mediaplayer, from every single mediaplayer. It is a client sided option designed to allow Vikings to mute or boost audio for mediaplayers to their personal liking.
- Listening distance is a **multiplayer synced** parameter that changes how far away audio is heard from individual mediaplayers. This can be limited in the plugin's config, which is not enforced on admins.

Default values and client sided master volume values can also be changed in the config.

Most features are synced over multiplayer through RPC events. 
Volume and tracking are client sided. Tracking will de-sync. 
Screens do not currently check for video sync or update to currently playing videos when connecting/loading new Mediaplayers (unless autoplay is enabled and a video is set)

## Recipes

Recipes can now be tweaked through a json file located at BepInEx\config\com.ood.valmedia.recipes.json, the mod will automatically make this. However the default recipe file can be found [here](https://github.com/modestimpala/OdinOnDemand/blob/main/plugin/Assets/default.json) if needed. 

Some helpful links to help you edit this file are here

[Beautify/Validate JSON](https://codebeautify.org/jsonviewer)
[Valheim Item List](https://valheim-modding.github.io/Jotunn/data/objects/item-list.html)

Avoid changing item names. If you have any issues keep in mind you can delete your .json file and the mod will re-create it with default values.

The remote control recipe can't be tweaked yet, you can always use a mod like WackysDB for the time being.

## Known Issues

- Unity crashes, see above notice.

- Boomboxes are currently difficult to place. Just.. keep trying. They place easier on terrain it seems. I'm not quite sure why this is happening. Gramophones are not affected. 

### If you have V-Sync force enabled in your GPU driver control panel, the mod may not function. 

![V-Sync Screenshot](https://i.imgur.com/JZTG3CZ.png)

If the mod fails to play any sort of video file, not just YouTube - Please check your GPU driver's control panel and make sure the vertical sync setting is either identical your in-game settings or set to "match application setting". You can also check your Player.log. If there are errors about Direct3D and refresh rate / v-sync you may have to do this. 

## NodeJS Server Installation

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
 
 