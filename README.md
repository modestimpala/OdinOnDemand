
# OdinOnDemand

OdinOnDemand (OOD) adds several forms of multimedia to Valheim that allow 
players to watch YouTube and direct video files on an in-game screen. Create your own cinema!
The mod also contains a boombox that can either play direct audio files, or SoundCloud links (with availability) - 
and all of this is multiplayer compatible and seemingly relatively low-impact.   

YouTube now works natively! Be sure to wipe configs.

[<img alt="Preview Video" width="256px" src="https://i.imgur.com/0BaY28I.jpg" />](https://www.youtube.com/watch?v=hePW1dueKjE)



## Features

- Cinema screens
   - Flatscreen TV, Table TV, Old TV, Laptop
   - Can play YouTube, Remote and Local files
- Audio Players
   - Boombox
- Multiplayer functionality
- Audio Control <sub>(Client Side)</sub>
- Forward Tracking <sub>(Client Side)</sub>

These first releases can be considered betas as it has not been extensively tested. Please report any issues on
the GitHub tracker.

## Installation

Installation of the plugin is fairly straightforward, just install into Bepinex/plugins or use r2modman. It must be installed on both server and client.

See NodeJS Server Installation for backup/selfhosted YouTube functionality.

## Use
In game, place down a cinema screen or boombox. Every player costs 2 bronze currently. Interact with it to open the GUI.

For cinema screens, you must have
either a direct link to a remote or local video file of a 
[compatible codec](https://docs.unity3d.com/2020.1/Documentation/Manual/VideoSources-FileCompatibility.html)
or use a youtube/youtu.be link. YouTube is now enabled by default. You can change between the built in YouTube library vs the self-hosted 
NodeJS server for grabbing YouTube links in the config. The NodeJS server may be more consistent 
or higher quality. See NodeJS setup for more. 

For boomboxes, you must have a direct link to a remote or local audio file of a 
[compatible codec](https://support.unity.com/hc/en-us/articles/206484803-What-are-the-supported-Audio-formats-in-Unity-)
or use a soundcloud.com link. Through testing I've noticed some SoundCloud songs are 
unavailable. I think it depends on the artist and how they upload/license their art. 

Local files are not synced over multiplayer.

Put the link into the input field and click set to set a file. Control playback
with play, pause, and stop. Press "Pickup" to remove the mediaplayer and refund materials.
Control individual player's volume with "+" and "-" and toggle mute with the "M" button on the overflow menu.
">" to track forward. 

In the mod settings you can control master volume. This will affect every screen/boombox.

All set file/play/pause/stop commands should be synced for multiplayer through RPC events. 
Volume and tracking are client sided. Tracking will de-sync. 
Screens do not currently check for sync or update to currently playing videos when connecting/loading new mediaplayers.


## NodeJS Server Installation

No need to use an external server now, but it's still there if you want it. yt-dlp may often return more consistently and with better quality videos. 
There is also the self-hosted benefit.

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
![Mod Screenshot](https://i.imgur.com/QL6gvwc.jpg)
![Mod Screenshot](https://i.imgur.com/Y88KuWV.jpg)
![Mod Screenshot](https://i.imgur.com/wTmD6Cc.jpeg)
<sub>[Build by DanAugust](https://www.valheimians.com/build/small-simple-cabin-pre-plains/) Last picture is with backlight setting turned off.</sub>

## Acknowledgements

 - [SoundCloudExplode](https://github.com/jerry08/SoundCloudExplode) | [YouTubeExplode](https://github.com/Tyrrrz/YoutubeExplode) | [youtube-exec-dl](https://www.npmjs.com/package/youtube-dl-exec) | [nodejs](https://nodejs.org/en/) | [yt-dlp](https://github.com/yt-dlp/yt-dlp)
 - Inspired by [Raft Cinema Mod](https://www.raftmodding.com/mods/cinema-mod)


## Notes
 I do plan on uploading the source code soon, it's just really messy so I want to clean it up and work on it a bit first. Hopefully by 1.0.0.

### Versions:

0.9.5-beta
 - Added native Youtube Functionality. No need to use an external server now, but it's still there if you want it.
   Youtube is now enabled by default and should work just fine. Let me know if you have any issues. Be sure to wipe configs.

0.9.1-beta 
 - Fixed Boombox Default Distance
 - Separate Boombox Default Distance and Master Volume

0.9.0-beta Initial Release
