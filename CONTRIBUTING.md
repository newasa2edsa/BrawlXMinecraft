# Contributing to BrawlXMinecraft

Thank you for your interest in improving BrawlXMinecraft! This project is an open-source, hybrid cross-game passthrough that runs Minecraft Java Edition (Fabric 26.3) and Brawl Stars (BSL v68 on Android ARM64) simultaneously in real time.

We welcome all pull requests, bug fixes, performance optimizations, and feature enhancements.

---

## Repository Structure

- **`bsl-passthrough/`**:
  - `bsl_passthrough.py`: Main Python coordinator. Runs the live Win32 mouse hook, OpenGL compositor / screen overlay, ADB memory bridge to `/proc/<pid>/mem`, hotbar/inventory syncing, and WebSocket event handler.
  - `mumu_steve_patch.py`: ARM64 binary patcher for BSL v68 in MuMu Player 12. Implements First-Person POV (`m_cameraMode = 3`), hides 2D HUD and Shelly's 3D mesh, hooks authoritative brawler damage, and handles wall/bush destruction via `LogicTileMap::destroyEnvironment`.
- **`bsl-minecraft-mod/`**:
  - Fabric 26.3 mod source. Intercepts Minecraft rendering, exports OpenGL frames via triple-buffered PBOs / shared memory (`passthrough.bin`), and synchronizes player input, hotbar items, and proxy mobs.
  - Prebuilt JAR: `bsl-minecraft-mod/build/libs/passthrough-0.1.0.jar`.
- **`CustomBrawlServer/`**:
  - High-performance local C# server handling BSL v68 TCP/UDP packets, battle simulation, player profiles, and local HTTP/WebSocket event bridge on port 8080.

---

## Roadmap & Areas Where Help Is Needed
1. **Additional Weapon Conversions**:
   - Enhancing weapons like the Crossbow, Potions, and Splash effects to trigger unique Brawl Stars status effects (e.g. poison, slow, burn).

---

## Rules for PRs

1. **Safety First**:
   - Never commit `.apk` files, extracted proprietary game textures, or copyrighted assets to Git.
   - Do not add cheats, aimbots, or netcode bypasses for public production game servers. This project is strictly for private/local server modding and education.
2. **ARM64 Register Safety**:
   - When modifying assembly patches in `mumu_steve_patch.py`, ensure all registers and stack offsets (`sp`, `x19..x30`, `w0..w7`) are preserved across function calls.
3. **Testing**:
   - Verify that your changes compile cleanly (`python build_mod.py`) and that `F12` cleanly exits without leaving orphan background processes.
