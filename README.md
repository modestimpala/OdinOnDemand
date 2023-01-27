
# OdinOnDemand

OdinOnDemand (OOD) adds several forms of multimedia to Valheim that allow 
players to watch YouTube and direct video files on an in-game screen. The mod also contains a boombox that 
can either play direct audio files, or SoundCloud links (with availability) - 
and all of this is multiplayer compatible and seemingly relatively low-impact.   

[<img alt="Preview Video" width="256px" src="https://i.imgur.com/0BaY28I.jpg" />](https://www.youtube.com/watch?v=hePW1dueKjE)

[YouTube Server Setup Tutorial Video](https://www.youtube.com/watch?v=9_vs8MItO38)

## Features

- Cinema screens
   - Flatscreen TV, Table TV, Old TV, Laptop
- Audio Players
   - Boombox
- Multiplayer functionality
- Audio Control <sub>(Client Side)</sub>
- Forward Tracking <sub>(Client Side)</sub>

This first release can be considered a beta as it has not been extensively tested. Please report any issues on
the github tracker.

## Installation

Installation of the plugin is fairly straightforward, just install into Bepinex/plugins or use r2modman. It must be installed on both server and client.

See NodeJS Server Installation for YouTube functionality.


## Use
In game, place down a cinema screen or boombox. Every player costs 2 bronze currently. Interact with it to open the GUI.

For cinema screens, you must have
either a direct link to a remote or local video file of a 
[compatible codec](https://docs.unity3d.com/2020.1/Documentation/Manual/VideoSources-FileCompatibility.html)
or have the yt-dlp nodejs server installed and config enabled to support Youtube links.
In which case, you may use youtube/youtu.be links to watch Youtube videos and shorts. YouTube is not enabled 
by default.

For boomboxes, you must have a direct link to a remote or local audio file of a 
[compatible codec](https://support.unity.com/hc/en-us/articles/206484803-What-are-the-supported-Audio-formats-in-Unity-)
or use a soundcloud.com link. Through testing I've noticed some SoundCloud songs are 
unavailable. I think it depends on the artist and how they upload/license their art. 

Local files are not synced over multiplayer.

Put the link into the input field and click set to set a file. Control playback
with play, pause, and stop. Press "Pickup" to remove the mediaplayer and refund materials.
Control individual player's volume with "+" and "-" and toggle mute with the "M" button on the overflow menu.
">" to track forward. 

In the mod settings you can control master volume. This will affect every screen and boombox, be careful not 
to set this too high. 

All set file/play/pause/stop commands should be synced for multiplayer through RPC events. 
Volume and tracking are client sided. Tracking will de-sync. 
Screens do not currently check for sync or update to currently playing videos when connecting/loading new mediaplayers.

## NodeJS Server Installation
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

 - [SoundCloudExplode](https://github.com/jerry08/SoundCloudExplode) | [youtube-exec-dl](https://www.npmjs.com/package/youtube-dl-exec) | [nodejs](https://nodejs.org/en/) | [yt-dlp](https://github.com/yt-dlp/yt-dlp)
 - Inspired by [Raft Cinema Mod](https://www.raftmodding.com/mods/cinema-mod)

## Notes
 I do plan on uploading the source code soon but its C# so 
 I welcome anyone to try and figure out how to get YoutubeExplode 
 or libvid to work in Valheim as I was unsuccessful.

### Versions:

0.9.0-beta Initial Release
