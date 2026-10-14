using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CustomBrawlServer.Config;
using CustomBrawlServer.Network;
using CustomBrawlServer.Protocol;

namespace CustomBrawlServer.Logic;

public sealed class PlayerBattleState
{
    public long PlayerId { get; init; }
    public Player Player { get; set; } = null!;
    public bool InBattle { get; set; }
    public int BrawlerId { get; set; }
    public int SkinId { get; set; }
    public int LocationId { get; set; } = 121;
    public int X { get; set; } = 3150;
    public int Y { get; set; } = 4950;
    public int Z { get; set; } = 0;
    public double Yaw { get; set; } = 0.0;
    public int Hp { get; set; } = 3900;
    public int MaxHp { get; set; } = 3900;
    public int SuperCharge { get; set; } = 0;
    public bool Hypercharged { get; set; }
    public int Tick { get; set; }
    public DateTime LastRealInputUtc { get; set; } = DateTime.MinValue;
}

/// <summary>
/// Manages per-player battle state, decodes TCP (10555) & UDP (9339) battle inputs,
/// and runs the 20 Hz server match tick loop that drives live bridge events to Minecraft.
/// </summary>
public sealed class BattleManager
{
    public static BattleManager Instance { get; } = new();

    // Supercell Laser ClientInput action codes
    public const int InputAttack = 0;
    public const int InputSuper = 1;
    public const int InputMove = 2;
    public const int InputGadget = 5;
    public const int InputEmote = 8;
    public const int InputHypercharge = 10;

    private readonly ConcurrentDictionary<long, PlayerBattleState> _states = new();
    private CancellationTokenSource? _loopCts;
    private Task? _loopTask;
    private PlayerManager? _playerManager;

    private BattleManager()
    {
    }

    public void Initialize(PlayerManager playerManager)
    {
        _playerManager = playerManager;
        _loopCts = new CancellationTokenSource();
        _loopTask = Task.Run(() => RunMatchLoopAsync(_loopCts.Token));
    }

    public void Stop()
    {
        _loopCts?.Cancel();
    }

    public PlayerBattleState GetOrCreateState(Player player)
    {
        return _states.GetOrAdd(player.LowId, _ =>
        {
            var spec = BrawlerCatalog.Get(player.SelectedBrawler);
            var brawlerData = player.GetOrCreateBrawler(player.SelectedBrawler);
            var mult = 1.0 + Math.Max(0, brawlerData.Level - 1) * 0.10;
            var maxHp = (int)Math.Round(spec.BaseHp * mult);
            return new PlayerBattleState
            {
                PlayerId = player.LowId,
                Player = player,
                BrawlerId = player.SelectedBrawler,
                SkinId = brawlerData.SelectedSkin,
                Hp = maxHp,
                MaxHp = maxHp,
                X = 3150,
                Y = 4950,
                Z = 0
            };
        });
    }

    public void StartMatch(Player player, int brawlerId, int skinId, int locationId)
    {
        var state = GetOrCreateState(player);
        var spec = BrawlerCatalog.Get(brawlerId);
        var brawlerData = player.GetOrCreateBrawler(brawlerId);
        var mult = 1.0 + Math.Max(0, brawlerData.Level - 1) * 0.10;
        var maxHp = (int)Math.Round(spec.BaseHp * mult);

        state.Player = player;
        state.InBattle = true;
        state.BrawlerId = brawlerId;
        state.SkinId = skinId;
        state.LocationId = locationId;
        state.MaxHp = maxHp;
        state.Hp = maxHp;
        state.SuperCharge = 0;
        state.Hypercharged = false;
        state.X = 3150;
        state.Y = 4950;
        state.Z = 0;
        state.Tick = 0;

        BridgeServer.Instance?.BroadcastMatchState(player, "start", brawlerId, skinId, locationId);
        BridgeServer.Instance?.BroadcastCharacterState(player, "match_start", online: true, inBattleOverride: true);
    }

    public void EndMatch(Player player)
    {
        var state = GetOrCreateState(player);
        if (!state.InBattle) return;

        state.InBattle = false;
        state.Hypercharged = false;
        BridgeServer.Instance?.BroadcastMatchState(player, "end", state.BrawlerId, state.SkinId, state.LocationId);
        BridgeServer.Instance?.BroadcastCharacterState(player, "match_end", online: true, inBattleOverride: false);
    }

    public void HandleClientInput(Player player, int inputType, int targetX, int targetY, bool autoAttack = false)
    {
        var state = GetOrCreateState(player);
        state.Player = player;
        state.InBattle = true;
        state.BrawlerId = player.SelectedBrawler;
        state.LastRealInputUtc = DateTime.UtcNow;

        var dx = targetX - state.X;
        var dy = targetY - state.Y;
        var yaw = (dx == 0 && dy == 0) ? state.Yaw : Math.Atan2(-dx, dy) * (180.0 / Math.PI);

        switch (inputType)
        {
            case InputMove:
                TriggerMove(player, targetX, targetY, 0);
                break;

            case InputAttack:
                if (autoAttack && dx == 0 && dy == 0)
                {
                    var rad = state.Yaw * (Math.PI / 180.0);
                    var rangeUnits = BrawlerCatalog.Get(state.BrawlerId).WeaponRange * 100;
                    targetX = state.X + (int)(-Math.Sin(rad) * rangeUnits);
                    targetY = state.Y + (int)(Math.Cos(rad) * rangeUnits);
                }
                TriggerAttack(player, targetX, targetY, 0, yaw);
                break;

            case InputSuper:
                if (autoAttack && dx == 0 && dy == 0)
                {
                    var rad = state.Yaw * (Math.PI / 180.0);
                    var rangeUnits = BrawlerCatalog.Get(state.BrawlerId).UltiRange * 100;
                    targetX = state.X + (int)(-Math.Sin(rad) * rangeUnits);
                    targetY = state.Y + (int)(Math.Cos(rad) * rangeUnits);
                }
                TriggerSuper(player, targetX, targetY, 0, yaw);
                break;

            case InputGadget:
                var gadgetId = player.GetOrCreateBrawler(state.BrawlerId).SelectedGadget;
                TriggerAbility(player, "gadget", gadgetId);
                break;

            case InputEmote:
                BridgeServer.Instance?.BroadcastCharacterState(player, "emote", online: true, emoteId: targetX);
                break;

            case InputHypercharge:
                TriggerAbility(player, "hypercharge", 0);
                break;
        }
    }

    /// <summary>
    /// Parses incoming UDP battle packets (Supercell 10-byte UDP session header + ByteStream payload).
    /// </summary>
    public void HandleUdpPacket(byte[] data)
    {
        if (data.Length < 11 || _playerManager == null) return;

        try
        {
            var player = _playerManager.GetActivePlayers().FirstOrDefault();
            if (player == null) return;

            // Supercell UDP battle frame: 10-byte session header followed by VInt count + inputs
            var stream = new ByteStream(data[10..]);
            var seq = stream.ReadVInt();
            var count = stream.ReadVInt();
            if (count <= 0 || count > 16) return;

            for (var i = 0; i < count && !stream.IsAtEnd(); i++)
            {
                var inputIndex = stream.ReadVInt();
                var inputType = stream.ReadVInt();
                var x = stream.ReadVInt();
                var y = stream.ReadVInt();
                var autoAttack = !stream.IsAtEnd() && stream.ReadBoolean();
                HandleClientInput(player, inputType, x, y, autoAttack);
            }
        }
        catch
        {
            // Ignore malformed UDP noise
        }
    }

    public void TriggerMove(Player player, int targetX, int targetY, int targetZ = 0)
    {
        var state = GetOrCreateState(player);
        var dx = targetX - state.X;
        var dy = targetY - state.Y;
        if (dx != 0 || dy != 0)
        {
            state.Yaw = Math.Atan2(-dx, dy) * (180.0 / Math.PI);
        }

        state.X = Math.Clamp(targetX, 0, 18000);
        state.Y = Math.Clamp(targetY, 0, 18000);
        state.Z = targetZ;

        BridgeServer.Instance?.BroadcastMove(player, player.SelectedBrawler, state.X, state.Y, state.Z, targetX, targetY, state.Yaw);
    }

    public void TriggerAttack(Player player, int targetX, int targetY, int targetZ = 0, double? yawOverride = null)
    {
        var state = GetOrCreateState(player);
        var brawlerId = player.SelectedBrawler;
        var spec = BrawlerCatalog.Get(brawlerId);
        var dx = targetX - state.X;
        var dy = targetY - state.Y;
        var yaw = yawOverride ?? ((dx == 0 && dy == 0) ? state.Yaw : Math.Atan2(-dx, dy) * (180.0 / Math.PI));
        state.Yaw = yaw;

        // Dash weapons (e.g. Mortis, Kenji, Kaze) also advance the brawler's position
        if (spec.WeaponBehavior == "Charge")
        {
            state.X = Math.Clamp(targetX, 0, 18000);
            state.Y = Math.Clamp(targetY, 0, 18000);
        }

        state.SuperCharge = Math.Min(100, state.SuperCharge + 25);
        BridgeServer.Instance?.BroadcastAttack(player, brawlerId, state.X, state.Y, state.Z, targetX, targetY, targetZ, yaw);

        if (state.SuperCharge >= 100)
        {
            BridgeServer.Instance?.BroadcastCharacterState(player, "super_ready", online: true);
        }
    }

    public void TriggerSuper(Player player, int targetX, int targetY, int targetZ = 0, double? yawOverride = null)
    {
        var state = GetOrCreateState(player);
        var brawlerId = player.SelectedBrawler;
        var spec = BrawlerCatalog.Get(brawlerId);
        var dx = targetX - state.X;
        var dy = targetY - state.Y;
        var yaw = yawOverride ?? ((dx == 0 && dy == 0) ? state.Yaw : Math.Atan2(-dx, dy) * (180.0 / Math.PI));
        state.Yaw = yaw;

        var startX = state.X;
        var startY = state.Y;
        var startZ = state.Z;

        // Charge / FlyJump / Blink supers move the brawler to the landing coordinates
        if (spec.UltiBehavior is "Charge" or "FlyJump" or "Blink")
        {
            state.X = Math.Clamp(targetX, 0, 18000);
            state.Y = Math.Clamp(targetY, 0, 18000);
        }

        state.SuperCharge = 0;
        BridgeServer.Instance?.BroadcastSuper(player, brawlerId, startX, startY, startZ, targetX, targetY, targetZ, yaw);
        BridgeServer.Instance?.BroadcastCharacterState(player, "super_used", online: true);
    }

    public void TriggerAbility(Player player, string subType, int cardId)
    {
        var state = GetOrCreateState(player);
        var brawlerId = player.SelectedBrawler;
        if (subType == "hypercharge")
        {
            state.Hypercharged = true;
        }
        BridgeServer.Instance?.BroadcastAbility(player, brawlerId, subType, cardId, state.X, state.Y, state.Z);
        BridgeServer.Instance?.BroadcastCharacterState(player, subType, online: true);
    }

    public void ApplyExternalDamage(long playerId, int damage)
    {
        if (!_states.TryGetValue(playerId, out var state)) return;
        state.Hp = Math.Max(0, state.Hp - damage);
        BridgeServer.Instance?.BroadcastCharacterState(state.Player, state.Hp == 0 ? "defeated" : "damaged", online: true);
        if (state.Hp == 0)
        {
            // Respawn with full HP
            state.Hp = state.MaxHp;
            state.X = 3150;
            state.Y = 4950;
        }
    }

    public void TriggerTestEvent(string kind)
    {
        var player = _playerManager?.GetActivePlayers().FirstOrDefault()
                     ?? _playerManager?.GetOrCreatePlayer(0, 1, null);
        if (player == null) return;

        var state = GetOrCreateState(player);
        switch (kind.ToLowerInvariant())
        {
            case "move":
                TriggerMove(player, state.X + 300, state.Y + 150);
                break;
            case "attack":
                TriggerAttack(player, state.X + 1200, state.Y);
                break;
            case "super":
                TriggerSuper(player, state.X + 1800, state.Y + 600);
                break;
            case "gadget":
                TriggerAbility(player, "gadget", player.GetOrCreateBrawler(player.SelectedBrawler).SelectedGadget);
                break;
            case "hypercharge":
                TriggerAbility(player, "hypercharge", 0);
                break;
            default:
                BridgeServer.Instance?.BroadcastCharacterState(player, "sync", online: true);
                break;
        }
    }

    private Task RunMatchLoopAsync(CancellationToken ct)
    {
        // Disabled automatic bot simulation: Steve only moves or attacks when the player gives input.
        return Task.CompletedTask;
    }
}
