# CustomBrawlServer (v68)

A high-performance, extensible, clean Brawl Stars private server emulator built from scratch in **C# (.NET 10)** specifically tailored for the **BSL v68** client APK.

---

## ✨ Features

- **Built Completely from Scratch:** Clean, modern, decoupled object-oriented architecture in C# (.NET 10) with zero external package bloat.
- **Full Supercell Protocol Pipeline:**
  - Standard 7-byte Supercell packet framing (`ushort ID`, `uint24 Length`, `ushort Version`).
  - Bit-packed `ByteStream` serializer with Supercell `VInt` variable-length integer encoding.
  - 64-bit `LogicLong` identifier encoding.
- **Dual Crypto Engine:**
  - Native Pepper encryption with TweetNaCl and Blake2B (24-byte nonce hashing).
  - Automatic detection for both encrypted Pepper handshakes and plaintext clients.
- **Complete Inbound & Outbound Messages:**
  - `ClientHelloMessage` (`10100`) $\to$ `ServerHelloMessage` (`20100`)
  - `LoginMessage` (`10101`) $\to$ `LoginOkMessage` (`20104`)
  - `OwnHomeDataMessage` (`24101`): Dynamic home data with currencies, event slots, catalog offers, profile icons, name colors, and brawler inventory.
  - `KeepAliveMessage` (`10108`) $\to$ `KeepAliveOkMessage` (`20108`)
  - `ChangeAvatarNameMessage` (`10212`) $\to$ `AvailableServerCommandMessage` (`24111`) with command `201` (`LogicChangeAvatarNameCommand`)
  - `EndClientTurnMessage` (`14102`) with command execution
  - `GoHomeMessage` (`14103`) & `AskForBattleMessage` (`14109`)
- **Player Command Dispatcher:**
  - `LogicSelectCharacterCommand` (`525`): Updates active lobby brawler
  - `LogicChangeSkinCommand` (`506`): Changes skin for selected brawler
  - `LogicUpgradeBrawlerCommand` (`520`): Brawler power leveling up to Level 11
  - `LogicSetPlayerThumbnailCommand` (`505`): Updates player profile icon
  - `LogicSetPlayerNameColorCommand` (`527`): Updates name color
  - `LogicSelectStarPowerCommand` (`532`): Equips a star power or gadget (validated against the card tables, saved per brawler)
- **Persistent Player Database:**
  - Individual player state files formatted as JSON in `data/players/{accountId}.json`.
  - Automatic saving on disconnect, name change, and command execution.
  - All 109 brawlers (ids 0-108) unlocked out-of-the-box at Level 11.
  - All star powers and gadgets (431 cards) owned out-of-the-box, see `UnlockAllCards` below.
  - Saves are written atomically (temp file + rename) so a crash can't corrupt an account.
- **Live Interactive Admin Console:**
  - Type commands directly in the server terminal while running:
    - `help`: Command manual
    - `status`: Uptime, RAM usage, and online session count
    - `players`: List loaded players with balances and trophy counts
    - `givegems <id> <amount>`: Add gems
    - `givegold <id> <amount>`: Add gold
    - `settrophies <id> <amount>`: Change trophy count
    - `unlockall <id>`: Instantly unlock all brawlers at max level
    - `rename <id> <name>`: Rename player and push live in-game update
    - `kick <id>`: Disconnect player session
    - `unhandled`: List every client message / turn command the server has no handler for (with a sample payload). Use this to find out which feature is still missing.
    - `reload`: Hot-reload `config.json`
    - `stop` / `exit`: Graceful shutdown

---

## 🚀 Quick Start

### Prerequisites
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) (or .NET 8+)

### Running the Server
In the project directory, run:
```bash
dotnet run
```
Or for an optimized release build:
```bash
dotnet run -c Release
```

The server will automatically start on `0.0.0.0:9339` and display:
```
[Server] Successfully started! Ready for BSL v68 client connections on port 9339.
[Server] Type 'help' in this terminal for available admin commands.
```

---

## 📱 Connecting with BSL v68 Client APK

1. **Host Configuration:**
   - In your BSL v68 APK, open `lib/arm64-v8a/libBSL.c.so` (or Frida config).
   - Set `redirectHost`:
     - If playing on an emulator on the same PC: `"127.0.0.1"`
     - If playing on an Android device on the same local Wi-Fi: your PC's local LAN IP (e.g. `"192.168.1.100"`).
   - Set `redirectPort`: `9339`.
2. **Launch APK:**
   - Open the game. The client will connect to your server, allocate an account ID, and load the main menu with all brawlers, max gems, and gold!

---

## ⚙️ Configuration (`config.json`)

```json
{
  "Server": {
    "ServerName": "CustomBrawlServer v68",
    "BindAddress": "0.0.0.0",
    "Port": 9339,
    "Environment": "dev",
    "DebugLogging": true
  },
  "Gameplay": {
    "StartingGems": 999999,
    "StartingGold": 999999,
    "StartingStarPoints": 50000,
    "StartingTrophies": 1337,
    "StartingLevel": 50,
    "UnlockAllBrawlers": true,
    "DefaultBrawlerLevel": 11,
    "SelectedBrawlerId": 0,
    "UnlockAllCards": true,
    "MaxCardId": 1321
  }
}
```

- `UnlockAllCards`: send every star power + gadget as owned in the home data. If a client ever refuses to load the home screen after an update, set this to `false` first to rule it out.
- `MaxCardId`: highest card id that exists in the client's data (1321 = brawler 108's gadgets). Raise it only if your client ships newer brawlers than `BrawlerRegistry` lists; cards the client has no data for can crash it.

---

## 📂 Project Architecture

```
CustomBrawlServer/
├── config.json                     # Server & gameplay settings
├── CustomBrawlServer.csproj        # .NET 10 project file
├── data/
│   └── players/                    # Saved JSON player files ({id}.json)
└── scr/
    ├── Program.cs                  # Server entry point, banner & shutdown
    ├── Config/
    │   └── ServerConfig.cs         # Configuration loader & models
    ├── Network/
    │   ├── TcpServer.cs            # Async Socket listener
    │   ├── ClientSession.cs        # Session state machine, packet routing & lifecycle
    │   ├── PacketFramer.cs         # 7-byte Supercell packet stream framing
    │   └── Crypto/
    │       ├── TweetNaCl.cs        # Pure C# TweetNaCl (xsalsa20poly1305 / curve25519)
    │       ├── Blake2B*.cs         # Blake2b 24-byte nonce generation
    │       ├── PepperKey.cs        # Supercell / BSL pepper keys
    │       └── PepperEncrypter.cs  # Stream encryptor/decryptor with incrementing nonce
    ├── Protocol/
    │   ├── ByteStream.cs           # Supercell binary serializer (VInt, bit-packing, data refs)
    │   ├── LogicLong.cs            # High/Low 64-bit ID representation
    │   ├── PiranhaMessage.cs       # Base message class
    │   ├── MessageFactory.cs       # Message ID mapping
    │   ├── Messages/
    │   │   ├── Client/             # 10100, 10101, 10108, 10212, 14102, 14103, 14109
    │   │   └── Server/             # 20100, 20103, 20104, 20108, 24101, 24111
    │   └── Commands/
    │       ├── LogicCommand.cs     # Base turn command
    │       ├── LogicCommandFactory.cs # Command ID dispatcher
    │       └── Logic*Command.cs    # 505, 506, 520, 525, 527
    ├── Logic/
    │   ├── Player.cs               # Player account model
    │   ├── BrawlerData.cs          # Brawler stats, skins, equipped star power / gadget
    │   ├── CardRegistry.cs         # Star power + gadget card ids (class 23)
    │   └── PlayerManager.cs        # Player lookup & JSON persistence
    └── Admin/
        ├── ConsoleCommandHandler.cs# Live admin shell
        └── UnhandledTracker.cs     # Records client traffic with no handler (`unhandled`)
```

---

## 🧭 What the server does and does not do

Battles in the BSL client run **locally** (`offlineBattles`), so what a gadget, star power or super *does* in a match is client-side logic and data, not code in this server. The server decides which cards the player **owns** (sent in `OwnHomeDataMessage`) and remembers what they equip. If an ability exists in the menu but misbehaves inside a match, that is fixed in the client/mod, not here.

Not implemented yet:

- **Hypercharge and gear ownership.** Their card ids / home-data fields aren't in this repo, so nothing is sent for them.
- **Echoing the equipped star power / gadget back in the home data.** Selections are validated and saved per brawler (`BrawlerData.SelectedStarPower` / `SelectedGadget`), but the packet field that tells the client which one is equipped isn't known yet, so the client keeps its own local choice until it reloads.
- **Per-brawler trophies, emotes, shop, quests.** The home data still contains fixed values for these.
- **Online matchmaking and the UDP battle server.** `MatchmakeMessage` only logs and the UDP listener only logs incoming packets.

To find the next missing piece quickly, play for a bit and type `unhandled` in the server console.
