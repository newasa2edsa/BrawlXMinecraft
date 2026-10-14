# MODLOG: BSL v68 C# Server <-> Minecraft Java Edition Bridge

## 1. Recon & Source of Truth Analysis

### Target 1: C# Server (`CustomBrawlServer` / `./bsl-server/`)
- **Runtime**: .NET 10 (`net10.0`, tested with SDK `10.0.400`).
- **Network Ports**:
  - `9339` (TCP): Main Supercell Piranha protocol listener (`TcpServer.cs`, `ClientSession.cs`). Uses 7-byte big-endian packet headers (`ushort MessageType`, `uint24 Length`, `ushort Version`) via `PacketFramer.cs` and Dual Crypto (Pepper `TweetNaCl` + `Blake2B` 24-byte nonce or plaintext fallback).
  - `9339` (UDP): Battle traffic listener (`UdpServer.cs`).
  - `8080` (WebSocket / TCP): Newly injected `BridgeServer.cs` broadcasting live match and character state JSON payloads (`ws://localhost:8080` and raw TCP newline-delimited JSON on `127.0.0.1:8080`).
- **Main Player Loop & Packet Handler**:
  - `ClientSession.StartAsync()` -> `PacketFramer.ReadPackets()` -> `ProcessPacketAsync(RawPacket)` -> `MessageFactory.CreateMessage(type)` -> `HandleMessageAsync(PiranhaMessage)`.
  - `EndClientTurnMessage` (`14102`) executes queued `LogicCommand` instances (`505` thumbnail, `506` skin, `520` upgrade brawler, `522` hero seen, `525` select brawler, `526` select hero, `527` name color, `532` star power/gadget, `538` emote, `557` generic setting).
  - `SinglePlayerMatchRequestMessage` (`14118`) and `MatchmakeMessage` (`14104` / `18977`) initiate matches.
- **Server Bugs & Incomplete Features Identified ("grain of salt" audit)**:
  1. **`OwnHomeDataMessage.WriteCardCommodity` (`scr/Protocol/Messages/Server/OwnHomeDataMessage.cs:451`)**: The docstring states that both brawler unlock cards (carrying `brawler.Level`) and star powers/gadgets (carrying `1`) are written to Commodity `0`, and `ServerConfig.Gameplay.UnlockAllCards` exists (`true` by default, `MaxCardId = 1321`), but `WriteCardCommodity` only wrote `unlockedBrawlers` and omitted the `CardRegistry.GetAbilityCards(cfg.MaxCardId)` entries.
  2. **`AskForBattleMessage` (`14109`) vs `GoHomeFromOfflinePractiseMessage` (`14109`)**: Both classes declared packet ID `14109`, and `AskForBattleMessage` was never registered in `MessageFactory.cs`.
  3. **No Battle Input Handler (`ClientInputMessage` `10555`) or UDP Input Parser**: `MessageFactory.cs` had no handler for `10555` (`ClientInputMessage`), and `UdpServer.cs` only hex-dumped incoming UDP packets without parsing player battle actions (`Move`, `Attack`, `Super`, `Gadget`, `Hypercharge`).
  4. **Offline Battles (`offlineBattles: true` in APK)**: Because the BSL v68 client runs single-player/training cave battles locally in `libg.so` when `offlineBattles: true` is set in `libBSL.c.so`, the server never emitted in-match tick events unless driven by `ClientInputMessage` (`10555`), UDP battle frames, or a server-side match simulation loop (`BattleManager`).

### Target 2: BSL v68 Client APK (`com.bsl.v68-rev2.apk`)
- **`lib/arm64-v8a/libBSL.c.so`**: Frida Gadget JSON configuration pointing to script `libBSL.68250_64.so` with parameters `{"redirectHost": "192.168.0.2", "redirectPort": 9339, "offlineBattles": true}`.
- **`lib/arm64-v8a/libBSL.68250_64.so`**: Obfuscated QuickJS bytecode (`758,094` bytes). Each byte `r` is encoded from QuickJS bytecode byte `c` via `r = (c - 0x38) ^ 0x0c` (decoded via `c = ((r ^ 0x0c) + 0x38) & 0xFF`, with modified QuickJS bytecode version byte `0x9d`).
- **`assets/csv_logic/characters.csv` & `skills.csv` & `projectiles_logic.csv`**:
  - Contains `435` named character rows; indices `0..108` are the Brawler roster (`106` playable in `BrawlerRegistry`: indices `33`, `55`, `88` excluded).
  - Extracted exact brawler metadata (`Name`, `Hitpoints`, `Speed`, `WeaponSkill`, `UltimateSkill`, `BehaviorType`, `Damage`, `CastingRange`, `SummonedCharacter`) into `BrawlerCatalog.cs` so the bridge broadcasts rich, authentic v68 combat and character payloads.

## 2. Chosen Route
- **Mashup Route**: State & Event Passthrough Bridge (`CustomBrawlServer` C# TCP/WebSocket server on `127.0.0.1:8080` $\to$ Minecraft Java Edition Fabric Mod WebSocket client).
- **Why**: Lightweight, zero external NuGet dependencies required in C# (.NET 10 built-in `TcpListener` + RFC 6455 WebSocket + raw TCP dual framing), and native `java.net.http.WebSocket` in the Minecraft Fabric mod.
