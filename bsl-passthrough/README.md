# Minecraft x Brawl Stars: First-Person Passthrough Mod

A real-time hybrid game mod combining **Minecraft Java Edition (Fabric 26.3 / Java 25)** and **Brawl Stars (BSL v68 on Android ARM64 in MuMu Player 12)** into an immersive **First-Person Action Game**.

You play as **Steve** directly inside the 3D Brawl Stars arena, wielding Minecraft weapons, placing blocks, detonating TNT & End Crystals that destroy Brawl Stars walls, and rocket-jumping with Wind Charges into devastating Mace smashes against enemy brawlers!

<p align="center">
  <img src="https://img.shields.io/badge/Minecraft-Fabric%2026.3-52A535?style=for-the-badge&logo=minecraft&logoColor=white" alt="Minecraft 26.3" />
  <img src="https://img.shields.io/badge/Brawl%20Stars-BSL%20v68-FFC700?style=for-the-badge&logo=googleplay&logoColor=black" alt="Brawl Stars BSL v68" />
  <img src="https://img.shields.io/badge/Tested%20On-MuMu%20Player%2012-00C4B4?style=for-the-badge&logo=android&logoColor=white" alt="MuMu Player 12" />
  <img src="https://img.shields.io/badge/Platform-Windows%2010%2F11-0078D6?style=for-the-badge&logo=windows&logoColor=white" alt="Windows" />
  <img src="https://img.shields.io/badge/License-MIT-F89820?style=for-the-badge&logo=open-source-initiative&logoColor=white" alt="MIT License" />
</p>

---

## Features

- **True First-Person 3D POV:**
  - Full 360° mouse look and authentic Minecraft WASD movement directly inside the Brawl Stars 3D arena.
  - The native 2D mobile HUD and Shelly 3D mesh are hidden via crash-proof ARM64 memory patches.
  - Minecraft renders Steve's 3D first-person hand, animated weapon swings, crosshair, and inventory hotbar over the battle.

- **Weapon Combat & Real Brawler Damage:**
  - **Diamond Sword:** Fast melee sweeping cone dealing damage and knockback to enemy brawlers.
  - **Mace with Wind Burst III:** Launch high into the air with Wind Charges and smash down on enemies for massive fall-scaled critical damage (up to 30,000+ damage) while bouncing back into the sky!
  - **Wind Charges:** Authentic rocket-jumps launching Steve ~22 blocks high with hanging airtime.
  - **Trident & Bow & Crossbow:** Ranged projectile combat with multishot fireworks and arrows.
  - **Fishing Rod:** Hook enemy brawlers from afar and reel them directly towards Steve.
  - **Falling Anvils:** Drop anvils from the sky to crush enemy brawlers.

- **Authoritative Wall & Bush Destruction:**
  - **TNT & Flint and Steel:** Place and ignite TNT with a snappy 1.5s fuse.
  - **End Crystals & Respawn Anchors:** Place and detonate high-yield explosives with Left Click.
  - Explosions call `LogicTileMap::destroyEnvironment` in the battle engine, permanently destroying walls, crates, fences, and bushes in the arena.

- **Instant Ender Pearl Teleportation:**
  - Throwing an Ender Pearl or eating Chorus Fruit instantly snaps Steve's position across both Minecraft and Brawl Stars with 0ms visual latency.

- **Full In-Game Inventory & Chat (`E`, `T`, `F10`):**
  - Press `E` to open the full Minecraft creative inventory, hover over items, and press `1-9` to equip them to your hotbar.
  - Press `T` or `/` to use Minecraft chat and server commands.
  - Press `F10` to adjust mouse sensitivity and keybindings live.

---

## Requirements

1. **OS:** Windows 10 or 11 (64-bit).
2. **Python:** Python 3.10+ (64-bit) with `numpy`.
3. **Android Emulator:** [MuMu Player 12](https://www.mumuplayer.com/) (Android 12 64-bit) with ADB enabled.
   - *Tested and confirmed working on MuMu Player 12 on Windows.*
4. **Brawl Stars Client (BSL v68):**
   - 📥 **Download Link**: [BSL v68 Client APK (MEGA)](https://mega.nz/file/rNFSwa7A#TBNCCfyVG3xUkykvnQhM77qlu63wvzGpG5W1bGjR1As)
   - Install by dragging and dropping into MuMu Player 12 (`com.bsl.v68`).
5. **Minecraft Java Edition:** Fabric `26.3` with Fabric API.
6. **.NET Runtime / SDK:** .NET 8.0 or 10.0 (for `CustomBrawlServer`).

---

## Quick Start Guide

### 1. Install Minecraft Mod
Copy the pre-compiled JAR `bsl-minecraft-mod/build/libs/passthrough-0.1.0.jar` into your Minecraft profile's `mods/` directory (e.g. Modrinth or Prism profile).

**Recommended JVM Arguments** (in Modrinth Profile Options):
```
--enable-native-access=ALL-UNNAMED -XX:+UseZGC
```

### 2. Start the Local Brawl Server
In PowerShell / Command Prompt:
```powershell
dotnet run --project CustomBrawlServer/CustomBrawlServer.csproj
```

### 3. Launch MuMu Player 12 & Apply the Steve Patch
1. Launch **MuMu Player 12** and start **BSL v68** (`com.bsl.v68`).
2. Run the patch installer (only needed once or after updating):
```powershell
python bsl-passthrough/mumu_steve_patch.py
```
*(This sets up port forwarding `tcp:9339` and `tcp:8080`, injects the first-person camera, hides the 2D mobile UI, and renames Shelly to Steve).*

### 4. Launch Minecraft
Start Minecraft with the `Fabric 26.3` profile. The mod will automatically create and load the `passthrough` world and initialize the shared memory frame grabber.

### 5. Launch the Passthrough Bridge
In your terminal:
```powershell
python bsl-passthrough/bsl_passthrough.py
```
Focus the MuMu Player window, hop into a battle, and enjoy Minecraft in Brawl Stars!

---

## Controls

| Key / Input | Action |
|---|---|
| **Mouse Look** | 360° Free First-Person Look (pitch & yaw) |
| **W, A, S, D** | Move Forward, Left, Backward, Right |
| **Left Click** | Attack / Sword Swing / Mine Block / Detonate End Crystal |
| **Right Click** | Place Block / Throw Ender Pearl / Shoot Crossbow / Ignite TNT |
| **Space** | Jump |
| **1 – 9** | Select Hotbar Slot (or assign hovered item in Inventory) |
| **E** | Open / Close Minecraft Creative Inventory |
| **F** | Swap Item with Offhand (Fireworks, Shield) |
| **Q** | Drop Held Item |
| **T / /** | Open Minecraft Chat / Command Prompt |
| **F10** | Sensitivity & Keybindings Configuration Menu |
| **Left Alt** | Hold to temporarily unlock mouse cursor |
