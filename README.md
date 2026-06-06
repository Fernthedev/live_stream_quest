# Prologue
This mod is designed to send your movement packets (camera, sabers) to a Beat Saber mod enabled PC where you can use more traditional recording/streaming programs for Youtube, Twitch etc. 

The intention is to allow streaming without hindering Quest performance and allowing for a better experience for both the viewer and player. 

Currently synchronizes:
- Movement
- Song time
- Song selection (Downloads if necessary)
  - Ensure requirements are met on PC side somehow
- Pause/Resume state

# Setup
Install the mod on both Quest and PC. They do not need to be the same game version to work.

**Note: Both Quest and PC must be accessible through the network in order for this to work.**
## Wired
You may use `adb` to bypass this requirement, though this will require a wired connection. Only TCP is supported under this mode.
```
adb tcpip 9542
```
## Configure
Open the game on the quest and PC. Once both are ready, on PC go to the `LiveStreamQuest` mod menu on the left side and configure your parameters. If you're using `adb tcpip`, use `127.0.0.1` for IP address. Otherwise, use the Quest's local network IP address e.g `192.168.x.x` or `10.x.x.x`.

## Usage
Once configured, press `Connect` on PC. Currently error handling and error UI are lacking as of today. Therefore, it may not be immediately obvious if issues occur. Additionally reconnect attempts are loosely handled. UDP reliability has not been properly developed, thereby requiring multiple connect attempts until a successful connection is made.

Once connected, load maps on Quest. If properly configured and connected, the map should start loading on PC. If the map is a custom level, it may take a while until PC finishes downloading and loading the map.

The Quest and PC will stay in a `Pause` state until both have achknowledged their ready state in game. Once this is done, they will both start the game at the same time barring latency issues.

## Settings
PC has a minimal UI for configuring various settings. However, Quest lacks UI for configuring settings e.g packet rate (in Hz) or TCP/UDP mode. The settings configuration requires manual editing, found at `/sdcard/ModData/com.beatgames.beatsaber/Config/LiveStreamQuest.json`.

Score synchronization is currently not implemented on PC. Ideally, this would allow scores on Quest to override PC scores for consistency. This is technically not allowed for leaderboard submissions but score submission is disabled anyways on PC.

# Development Setup
For both the qmod and PC mod, compile protobuf using vcpkg.

Essentially, install vcpkg then run:
```sh
vcpkg install --triplet arm64-android
```

Don't mind the triplet, that's only for building the Quest mod. This will work on .NET for any platform.

The PC and QMod follow their respective setup process, which hopefully will be documented later in this README.

For now, assume the `qpm` process for QMod and BeatSaberDir + NuGet for PC.

## TODO
- [x] Switch to UDP
  - [ ] Add UDP keep alive to prevent disconnects
- [x] Improve protocol handshake hooks
- [ ] Rewrite interpolation math to something that isn't AI generated
- [x] Improve .NET networking 
- [ ] Batch score updates to avoid TCP spam
- [ ] Improve packets
  - [ ] Change frame to `short` to save bandwidth
  - [ ] Reduce packet size by using smaller or variable size types
- [ ] Fix PC UI
  - [ ] Add song download progress UI
- [ ] Add Quest UI
- [ ] Improve connection stability e.g reconnect
