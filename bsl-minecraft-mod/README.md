# BSL v68 ↔ Minecraft Java Edition Bridge Mod (`bslbridge`)

A lightweight **Fabric (Minecraft 26.3)** mod that connects to the C# BSL v68 server's live event bridge (`ws://localhost:8080`) and translates Brawl Stars character state updates, movement, basic attacks, supers, gadgets, and hypercharges into real-time Minecraft entities, particles, sounds, summons, and explosions.

## Architecture

```
BSL v68 Android Client (com.bsl.v68-rev2.apk)
   │  TCP :9339 (Lobby/Commands/10555 Inputs) + UDP :9339 (Battle Inputs)
   ▼
CustomBrawlServer (C# .NET 10)
   ├── BattleManager (20 Hz arena simulation + real packet handler)
   ├── BrawlerCatalog (107 v68 character & weapon CSV profiles)
   └── BridgeServer (RFC 6455 WebSocket + Raw TCP JSON lines on 127.0.0.1:8080)
          │  ws://localhost:8080 (JSON payloads)
          ▼
bsl-minecraft-mod (Fabric 0.19.5 / Minecraft 26.3 / Java 25)
   ├── BslBridgeClient     (Async java.net.http.WebSocket client + auto-reconnect)
   ├── BslCoordinateMapper (1 tile = 300 BSL units = 1.0 MC block, anchored to player)
   ├── BslEntityTracker    (Spawns & syncs custom brawler mobs, equipment, HP, scale & aura)
   └── BslEffectMapper     (Ray-marched attacks, supers, explosions, Bruce/Scrappy summons)
```

## Pre-Built JAR & Building From Source

The compiled mod JAR is ready at:
`bsl-minecraft-mod/build/libs/bsl-minecraft-bridge-0.1.0.jar`

To rebuild from source:
```powershell
cd bsl-minecraft-mod
.\gradlew.bat build
```

Copy `build/libs/bsl-minecraft-bridge-0.1.0.jar` into your **Minecraft 26.3 (Fabric 0.19.5+)** `mods/` folder alongside **Fabric API (`0.161.0+26.3`)**.

## In-Game Commands

Once inside a Minecraft world (singleplayer or server), the mod automatically connects to `ws://localhost:8080` and anchors the 21×33 Brawl Stars arena at your player's position.

- `/bsl status` — Check WebSocket connection state, tracked brawler entities, and current arena anchor.
- `/bsl anchor` — Re-center the Brawl Stars arena (`X=3150, Y=4950` in BSL units) at your current Minecraft block position.
- `/bsl reconnect` — Reconnect to `ws://localhost:8080`.
- `/bsl test <move|attack|super|gadget|hypercharge>` — Request the C# server (or run locally if offline) to emit a test brawler event.

## Event Mapping Reference

| BSL Event (`event`) | Trigger Source | Minecraft Effect |
|---|---|---|
| `character_state` | Login (`10101`), brawler/skin/power change (`506`/`520`/`525`/`532`), battle start (`14118`), damage | Spawns or updates a tagged brawler mob (`Skeleton`, `Pillager`, `Vindicator`, `IronGolem`, `Breeze`, `Blaze`, `Witch`, or `Zombie`), sets custom nameplate with HP & Super %, scales size, and toggles glowing aura on Hypercharge. |
| `move` | `ClientInputMessage` (`10555` type `2`), UDP `:9339`, or 20 Hz arena sim | Smoothly teleports/interpolates the brawler entity to mapped `(mcX, mcY, mcZ)` and updates head/body yaw + sprinting particles. |
| `attack` | `ClientInputMessage` (`10555` type `0`), UDP `:9339`, or `attack` console cmd | Ray-marches weapon-specific particles (`CRIT`, `FLAME`, `SONIC_BOOM`, `WITCH`, `NOTE`, `CLOUD`, `ELECTRIC_SPARK`) from the brawler along aim vector `(aimX, aimY)`, plays positional audio, and deals AoE damage (`bslDamage / 250 = hearts`). |
| `super` | `ClientInputMessage` (`10555` type `1`), UDP `:9339`, or `super` console cmd | Triggers brawler-specific ultimate: **Shelly/Colt/Brock/Dynamike/Bull/El Primo/Frank/Tick/Bo/Mortis** spawn `TNT_MINECART` / block-safe explosions; **Nita** summons *Bruce* (`PolarBear`); **Jessie/Pam/Penny/8-Bit/Mr.P** summon *Turrets* (`IronGolem`/`SnowGolem`); **Poco/Byron/Pam** cast a healing ring (`HEART` + Instant Health); **Sandy/Leon** create a smoke cloud; others unleash a shockwave beam. |
| `ability` | Gadget (`type 5`), Emote (`type 8`), Hypercharge (`type 10`) | **Gadget**: Totem burst (`TOTEM_OF_UNDYING`) + Speed II + Absorption; **Hypercharge**: Lightning strike sound, `TRIAL_SPAWNER_DETECTION_OMINOUS` + `SOUL_FIRE_FLAME` burst, Strength II + Speed II + Resistance + Glowing; **Emote**: Note/Villager Happy particles above head. |
