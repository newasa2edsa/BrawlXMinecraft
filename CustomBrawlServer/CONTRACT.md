# Bridge Contract: CustomBrawlServer (BSL v68) <-> Minecraft Java Edition

## 1. Transport & Lifecycle
- **Endpoint**: `ws://localhost:8080` (RFC 6455 WebSocket) and `tcp://127.0.0.1:8080` (newline-delimited UTF-8 JSON lines) served simultaneously by `BridgeServer` in `CustomBrawlServer`.
- **Protocol Version**: `1`
- **Connection Handshake**:
  - Upon connection, `BridgeServer` immediately transmits a `hello` event:
    ```json
    {"event":"hello","v":1,"server":"CustomBrawlServer v68","gamePort":9339,"bridgePort":8080,"ts":1791555600000}
    ```
  - Followed immediately by `character_state` snapshots for all currently connected/active players.
- **Heartbeat & Reconnection**:
  - WebSocket `Ping` (`0x89`) frames are answered with `Pong` (`0x8A`).
  - The Minecraft Fabric mod (`BslBridgeClient`) automatically reconnects with exponential backoff (2s–10s) if the BSL server restarts.

## 2. Coordinate & Unit Mapping
- **Brawl Stars v68 Logic Units**:
  - `1 tile = 300 logic units`.
  - Standard 3v3 Arena: `21 x 33 tiles` (`6300 x 9900` logic units), center at `(3150, 4950)`.
  - Showdown Arena: `60 x 60 tiles` (`18000 x 18000` logic units), center at `(9000, 9000)`.
- **Minecraft World Mapping (`BslCoordinateMapper`)**:
  - Scale: `1 Brawl Stars tile (300 logic units) = 1.0 Minecraft block`.
  - Given Minecraft arena anchor $(X_0, Y_0, Z_0)$ (defaults to the player's position on match start or set via `/bsl anchor`):
    $$X_{\text{MC}} = X_0 + \frac{X_{\text{BSL}} - X_{\text{center}}}{300.0}, \quad Y_{\text{MC}} = Y_0 + \frac{Z_{\text{BSL}}}{300.0}, \quad Z_{\text{MC}} = Z_0 + \frac{Y_{\text{BSL}} - Y_{\text{center}}}{300.0}$$
  - Heading / Yaw:
    $$\text{yaw}_{\text{MC}} = \operatorname{atan2}(-\Delta X_{\text{MC}}, \Delta Z_{\text{MC}}) \cdot \frac{180}{\pi}$$

## 3. Event Schemas (BSL Server -> Minecraft)

### 3.1 `character_state` (Spawn / Sync Brawler Entity)
Triggered on player login (`10101`), brawler selection (`525`), skin change (`506`), brawler upgrade (`520`), star power/gadget equip (`532`), emote (`538`), match start (`14118`/`14104`), damage/heal, or disconnect.
```json
{
  "event": "character_state",
  "v": 1,
  "ts": 1791555600100,
  "playerId": 2,
  "playerName": "Brawler",
  "action": "select_brawler",
  "online": true,
  "inBattle": true,
  "brawlerId": 0,
  "brawlerName": "ShotgunGirl",
  "displayName": "Shelly",
  "skinId": 0,
  "level": 11,
  "starPowerId": 76,
  "gadgetId": 255,
  "emoteId": 0,
  "hp": 3900,
  "maxHp": 3900,
  "speed": 770,
  "superCharge": 100,
  "hypercharged": false,
  "x": 3150,
  "y": 4950,
  "z": 0,
  "yaw": 0.0
}
```

### 3.2 `move` (Player / Brawler Position & Velocity Sync)
Triggered by `ClientInputMessage` (`10555` input type `2`), UDP battle packets, or the 20 Hz `BattleManager` loop.
```json
{
  "event": "move",
  "v": 1,
  "ts": 1791555600150,
  "playerId": 2,
  "playerName": "Brawler",
  "brawlerId": 0,
  "brawlerName": "ShotgunGirl",
  "x": 3450,
  "y": 4950,
  "z": 0,
  "targetX": 3750,
  "targetY": 4950,
  "yaw": -90.0,
  "speed": 770
}
```

### 3.3 `attack` (Basic Attack / Weapon Skill)
Triggered by `ClientInputMessage` (`10555` input type `0`), UDP battle packets, `BattleManager`, or admin command.
```json
{
  "event": "attack",
  "v": 1,
  "ts": 1791555600200,
  "playerId": 2,
  "playerName": "Brawler",
  "brawlerId": 0,
  "brawlerName": "ShotgunGirl",
  "skillName": "ShotgunGirlWeapon",
  "behavior": "Attack",
  "damage": 1500,
  "range": 21,
  "explosive": false,
  "x": 3450,
  "y": 4950,
  "z": 0,
  "targetX": 4500,
  "targetY": 4950,
  "targetZ": 0,
  "yaw": -90.0
}
```

### 3.4 `super` (Super / Ultimate Ability)
Triggered by `ClientInputMessage` (`10555` input type `1`), UDP battle packets, `BattleManager`, or admin command.
```json
{
  "event": "super",
  "v": 1,
  "ts": 1791555600250,
  "playerId": 2,
  "playerName": "Brawler",
  "brawlerId": 0,
  "brawlerName": "ShotgunGirl",
  "skillName": "ShotgunGirlUlti",
  "behavior": "Attack",
  "damage": 3200,
  "range": 23,
  "explosive": true,
  "summon": "",
  "x": 3450,
  "y": 4950,
  "z": 0,
  "targetX": 4800,
  "targetY": 4950,
  "targetZ": 0,
  "yaw": -90.0
}
```

### 3.5 `ability` (`gadget` / `hypercharge`) & `match_state` (`match_start` / `match_end`)
- `ability`: `{"event":"ability","subType":"gadget|hypercharge","playerId":2,"brawlerId":0,"cardId":255,"x":3450,"y":4950,"z":0}`
- `match_state`: `{"event":"match_state","state":"start|end","playerId":2,"brawlerId":0,"skinId":0,"locationId":121,"arenaWidth":6300,"arenaHeight":9900}`
