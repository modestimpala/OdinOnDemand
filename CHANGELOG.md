

### Versions:

## 0.9.87-beta
 - Fixed relative path audio file playback
 - Radio mesh edit

## 0.9.86-beta
 - Fixed SetURL behavior
 - Fixed radio behavior, audio properties

## 0.9.85-beta
 - Added prefabs:
   - New mediaplayer: Radio, a vintage style wooden radio
   - Studio Speaker, Standing Speaker, Receiver
      - Decorative pieces, with future planned mechanics
 - Added relative path file lookup from plugin dir (local:// or local:\\)
 - Default recipe JSON is now properly formatted
 - Autoplay YouTube and SoundCloud links are now stored and sent un-parsed, fixing old CDN video timeout
 - Proper key config for Remote Control
 - Modifications to YoutubeExplode Library
 - Readme update
 - Code refactoring

## 0.9.82-beta
 - Added crash disclaimer from 2020.3.45 upgrade

## 0.9.81-beta
 - Updated YoutubeExplode to version 6.2.12

## 0.9.80-beta
 - Theater Screen
   - Very large screen roughly 16m x 9m for theater environments.
 - Fixed ward locking behavior 
 - Fixed musicplayer volume mixer
 - Added settings overlay, click the cog icon!
 - Added ZDO for several variables
   - Distance, Autoplay/URL, Admin Only, isLocked, and isLooping are saved to world and synced over multiplayer. 
   - This means each individual player can have it's own distance and properties, which will sync and persist after reloading world or areas!
   - Custom listening distance can be limited via config. This is not enforced on admins.
 - Added admin only
 - Added autoplay
   - Playlists are currently NOT supported.
   - .mp3/SoundCloud autoplay is currently NOT supported. Direct video links or youtube only.
   - Autoplayed videos are NOT SYCNED. The video will start upon object loading for every viking.
   - Please be aware that YouTube videos saved from autoplay will expire after an indeterminable amount of time. This is simply the nature of Google's content delivery and as such cannot be avoided.
  
## 0.9.76-beta
 - Fixed playlist index string
 - Fixed unnecessary debug logging

## 0.9.75-beta
 - UI Redesign
	- Button icons, combined overflow menus, volume slider
 - Ward interaction, when a media-player is Locked it will check for access with Wards. Media-players are locked by default. You may have to cycle the lock for newly loaded Vikings.
 - Removed pickup button. All media-players can be destroyed via the Hammer just like any piece, you will get appropriate materials refunded. 
 - All media-players now have Wear N Tear - they can take damage, and be destroyed. Media-players are weak to pickaxes.
 - Changed Screen Render Distance calculations to hopefully be more efficient. It no longer does any distance calculations and instead just uses collider OnTrigger events.

## 0.9.71-beta
 - Fixed missing Monitor recipe. If your recipe file is outdated, the default one will be loaded. Check logs and delete recipe file if needed.

## 0.9.70-beta
 - Monitor Prefab added, larger than Old TV but smaller than Table TV.
 - Remote control for interacting with media-players from a distance
 - Basic playlist support implemented
 - Shuffle feature for playlists
 - Fixed resources not refunding on destruction
 - Media-player code refactoring
 - More multiplayer code refactoring 

## 0.9.60-beta
 - First Gramophone prefab release
 - Added custom recipes via json config
 - Added looping with multiplayer sync
 - Added Boombox YouTube support
 - Added loading circle to cinema screens
 - Greatly increased efficiency of how plugin finds media-players for multiplayer
 - Changed volume handling, added default player volume
 - Fixed Boombox volume bug
 - Fixed boombox playing previous SoundCloud song on set video button press when song unavailable
 - Cleaned up debug logging
 - Cleaned up config

## 0.9.58-beta
 - Fixed GUI not closing when media-player destroyed by other player.

## 0.9.57-beta
 - Added Vulkan Support

## 0.9.56-beta
 - Small adjustment in Rendering distance calculations 

## 0.9.55-beta
 - Fixed Screen volume bug
 - Added "Screens Stop Rendering Out of Range" and config

## 0.9.51-beta
 - Fix spelling mistake

## 0.9.5-beta
 - Added native YouTube Functionality. No need to use an external server now, but it's still there if you want it.
   YouTube is now enabled by default and should work just fine. Let me know if you have any issues. Be sure to wipe configs.

## 0.9.1-beta 
 - Fixed Boombox Default Distance
 - Separate Boombox Default Distance and Master Volume

## 0.9.0-beta Initial Release
