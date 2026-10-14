# BrawlXMinecraft (BSL Passthrough)

<p align="center">
  <b>Play as Minecraft Steve inside Brawl Stars in True First-Person 3D!</b><br>
  <i>A real-time cross-game passthrough combining Minecraft Java Edition (Fabric 26.3) and Brawl Stars (BSL v68 on Android ARM64) into an immersive First-Person action shooter.</i>
</p>

<p align="center">
  <img src="https://img.shields.io/badge/Minecraft-Fabric%2026.3-52A535?style=for-the-badge&logo=minecraft&logoColor=white" alt="Minecraft 26.3" />
  <img src="https://img.shields.io/badge/Brawl%20Stars-BSL%20v68-FFC700?style=for-the-badge&logo=googleplay&logoColor=black" alt="Brawl Stars BSL v68" />
  <img src="https://img.shields.io/badge/Tested%20On-MuMu%20Player%2012-00C4B4?style=for-the-badge&logo=android&logoColor=white" alt="MuMu Player 12" />
  <img src="https://img.shields.io/badge/Platform-Windows%2010%2F11-0078D6?style=for-the-badge&logo=windows&logoColor=white" alt="Windows" />
  <img src="https://img.shields.io/badge/License-MIT-F89820?style=for-the-badge&logo=open-source-initiative&logoColor=white" alt="MIT License" />
</p>

---

## Overview

**BrawlXMinecraft** bridges two entire game engines in real time:
- **Brawl Stars (BSL v68)** running inside MuMu Player 12 (Android 12 ARM64).
- **Minecraft Java Edition (Fabric 26.3)** running natively on your PC.

Through an ultra-low-latency shared-memory pipeline, Win32 input synchronization, and ADB memory hooks:
- You walk around the 3D Brawl Stars arena using authentic Minecraft WASD physics.
- You aim with seamless 360° First-Person mouse look.
- You hold and swing genuine Minecraft weapons: **Diamond Swords**, **Maces**, **Tridents**, **Bows**, **Crossbows**, and **Wind Charges**.
- Explosions from Minecraft **TNT**, **End Crystals**, and **Respawn Anchors** permanently obliterate Brawl Stars walls, bushes, and crates in real time!
- **Ender Pearls** teleport Steve instantly across both games with zero void loss.
- Press **E** to open the full Minecraft Creative inventory, search for items, and drag them to your hotbar.

---

## Features

- **True First-Person Camera**:
  - Full 360° mouse-look at 60.0° vertical FOV matching Minecraft 1:1.
  - The native 2D mobile joysticks, HUD, and Shelly 3D model are cleanly hidden via memory patches.
- **Authoritative Combat & Knockback**:
  - **Diamond Sword**: Fast sweeping melee attacks against enemy brawlers.
  - **Mace + Wind Burst**: Rocket-jump with Wind Charges ~22 blocks into the air, then smash down on enemy brawlers for thousands of critical fall-scaled damage!
  - **Wind Charges**: True rocket-jumping with hang-time and horizontal air-strafing.
  - **Ranged Weapons**: Bows, Crossbows with fireworks, and Tridents damage enemies on direct hits.
- **Environment Destruction**:
  - Detonating TNT, End Crystals, or Respawn Anchors calls `LogicTileMap::destroyEnvironment` directly in the battle engine, clearing obstacles and opening sightlines.
- **Void-Safe Ender Pearls**:
  - Throwing an Ender Pearl or eating Chorus Fruit instantly synchronizes coordinates between Minecraft and Brawl Stars. If a pearl is thrown past the arena borders, it safely teleports Steve to the ground at $y=64$.
- **Seamless Controls & UI**:
  - Automatic keybind detection: reads your custom Minecraft `options.txt` (including inverted attack/use mouse buttons, sprint, jump, drop).
  - Press `E` to browse creative items; search, backspace, delete, and copy/paste work without closing the inventory.
  - Press `F10` to adjust Minecraft video settings, mouse sensitivity, or FOV on the fly.
  - Press `F12` to cleanly exit at any moment.

---

## Requirements

1. **Operating System**: Windows 10 or 11 (64-bit).
2. **Android Emulator**: [MuMu Player 12](https://www.mumuplayer.com/) (Android 12 64-bit) with ADB enabled.
   > **Important:** This mod was specifically developed, tested, and confirmed working on **MuMu Player 12** on Windows. Other emulators (e.g. BlueStacks, LDPlayer) may have different memory layouts or ADB port behaviors.
3. **Brawl Stars Client (BSL v68)**:
   - 📥 **Download APK**: [BSL v68 Client APK (MEGA)](https://mega.nz/file/rNFSwa7A#TBNCCfyVG3xUkykvnQhM77qlu63wvzGpG5W1bGjR1As)
   - Drag and drop this APK into MuMu Player 12 to install `com.bsl.v68`.
4. **Python**: Python 3.10+ (64-bit) with `numpy` installed (`pip install -r requirements.txt`).
5. **Minecraft Java Edition**: Fabric `26.3` with Fabric API installed (via Modrinth, Prism, or official launcher).
6. **.NET**: .NET 8.0 or 10.0 runtime / SDK (to run `CustomBrawlServer`).
7. **Java**: JDK 21+ (only needed if you want to recompile the Minecraft mod from source).

---

## Quick Start Guide

### Step 1: Install MuMu Player 12 & BSL v68 APK
1. Install **[MuMu Player 12](https://www.mumuplayer.com/)**.
2. Download the required **[BSL v68 APK from MEGA](https://mega.nz/file/rNFSwa7A#TBNCCfyVG3xUkykvnQhM77qlu63wvzGpG5W1bGjR1As)**.
3. Drag and drop the downloaded `.apk` directly into MuMu Player 12 to install it.
4. Ensure **Root Permission** is enabled in MuMu Player Settings (Settings ➔ General ➔ Root Permission ➔ Turn On).

### Step 2: Install Python Requirements
```cmd
pip install -r requirements.txt
```

### Step 3: Install the Minecraft Mod
A pre-compiled mod JAR is included ready to play!
- Copy `bsl-minecraft-mod/build/libs/passthrough-0.1.0.jar` into your Minecraft profile's `mods/` directory.
- *(Recommended)* In your Minecraft launcher profile options, add these JVM arguments for maximum performance and zero garbage collection stutter:
  ```
  --enable-native-access=ALL-UNNAMED -XX:+UseZGC
  ```

### Step 4: Patch MuMu Player
1. Launch **MuMu Player 12** with BSL v68 installed.
2. Double-click **`patch_mumu.bat`** (or run `python bsl-passthrough/mumu_steve_patch.py`).
   - *This automatically detects MuMu Player, sets up ADB port forwarding, and applies the First-Person POV and Steve patches.*

### Step 5: Start the Passthrough Bridge
1. Double-click **`start_passthrough.bat`** (or run `python bsl-passthrough/bsl_passthrough.py`).
   - *The script automatically boots `CustomBrawlServer` in the background and listens for both games.*
2. Launch **Minecraft (Fabric 26.3)**.
3. Open **BSL v68** in MuMu Player and enter a Practice / Friendly match!

---

## Controls

| Key / Input | Action |
|---|---|
| **Mouse Look** | 360° Free First-Person Camera (uses your Minecraft Mouse Sensitivity) |
| **W, A, S, D** | Move Forward, Left, Backward, Right (camera-relative) |
| **Space** | Jump |
| **Left Click** | Attack / Sword Swing / Mine Block / Detonate End Crystal |
| **Right Click** | Use / Place Block / Throw Ender Pearl / Shoot Crossbow / Ignite TNT |
| **1 – 9** | Select Hotbar Slot (or assign hovered item in Inventory) |
| **Mouse Wheel** | Scroll Hotbar Items |
| **E** | Open / Close Minecraft Creative Inventory |
| **F** | Swap Item with Offhand |
| **Q** | Drop Held Item |
| **T / /** | Open Minecraft Chat / Command Prompt |
| **F10** | Open Minecraft Options (adjust sensitivity, FOV, scale) |
| **F12** | **Emergency Kill Switch** (cleanly stops passthrough and releases cursor) |

---

## How It Works (Architecture)

```
┌────────────────────────────────────────────────────────────────────────┐
│                        Win32 Passthrough Host                          │
│                      (bsl_passthrough.py)                              │
│                                                                        │
│  ┌───────────────────────┐                  ┌───────────────────────┐  │
│  │   Low-Level Inputs    │                  │ Desktop Overlay & PBO │  │
│  │ Mouse Look / WASD / E │                  │ 60 FPS Win32 Compositor│  │
│  └──────────┬────────────┘                  └───────────▲───────────┘  │
└─────────────┼───────────────────────────────────────────┼──────────────┘
              │                                           │
       WebSocket (25599)                       Shared Memory (passthrough.bin)
              │                                           │
┌─────────────▼───────────────────────────────────────────┴──────────────┐
│                  Minecraft Java Edition (Fabric 26.3)                  │
│                                                                        │
│  - First-Person Hand & Weapons (Sword, Mace, Wind Charge, Pearl, TNT)  │
│  - Triple-Buffered Persistent-Mapped PBO Frame Grabber                 │
│  - Proxy Mobs Sync & Damage Calculation Engine                         │
└────────────────────────────────────────────────────────────────────────┘
              │
         ADB Memory Bridge (/proc/<pid>/mem) + Socket (8080)
              │
┌─────────────▼──────────────────────────────────────────────────────────┐
│                   MuMu Player 12 (BSL v68 Android)                     │
│                                                                        │
│  - First-Person Camera POV (m_cameraMode = 3)                          │
│  - Authoritative Environment Destruction (LogicTileMap::destroyEnv)    │
│  - CustomBrawlServer (Port 9339) Packet Routing                        │
└────────────────────────────────────────────────────────────────────────┘
```

---

## Contributing & Forking

This project is open-source and ready for improvements! Check out [CONTRIBUTING.md](CONTRIBUTING.md) for architecture details, roadmap items, and guidelines.

---

## Disclaimer

This mod is an independent, non-commercial fan project developed for educational and single-player/private-server exploration. It is not affiliated with, endorsed by, or connected to Supercell or Mojang/Microsoft. Never use cross-game passthroughs on public competitive matchmaking servers.
