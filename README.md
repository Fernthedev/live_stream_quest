# Prologue
This mod is designed to send your movement packets (camera, sabers) to a Beat Saber mod enabled PC where you can use more traditional recording/streaming programs for Youtube, Twitch etc. 

The intention is to allow streaming without hindering Quest performance and allowing for a better experience for both the viewer and player. 

Currently synchronizes:
- Movement
- Song time
- Song selection (Downloads if necessary)
- Puase/Resume state

# Development Setup
For both the qmod and PC mod, compile protobuf using vcpkg.

Essentially, install vcpkg then run:
```sh
vcpkg install --triplet arm64-android
```

Don't mind the triplet, that's only for building the Quest mod. This will work on .NET for any platform.

The PC and QMod follow their respective setup process, which hopefully will be documented later in this README.

For now, assume the `qpm` process for QMod and BeatSaberDir + NuGet for PC.

