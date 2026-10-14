using CustomBrawlServer.Logic;
using CustomBrawlServer.Protocol;

namespace CustomBrawlServer.Protocol.Messages.Server;

public class StartLoadingMessage : PiranhaMessage
{
    private readonly Player _player;
    private readonly int _brawlerId;
    private readonly int _skinId;
    private readonly int _locationId;
    private readonly int _playerCount;

    public StartLoadingMessage(Player player, int brawlerId, int skinId, int locationId, int playerCount = 6)
    {
        _player = player;
        _brawlerId = brawlerId;
        _skinId = skinId;
        _locationId = locationId;
        _playerCount = Math.Max(1, playerCount);
    }

    public override void Encode()
    {
        base.Encode();

        ByteStream.WriteInt(_playerCount);
        ByteStream.WriteInt(0);
        ByteStream.WriteInt(0);

        ByteStream.WriteInt(_playerCount);

        // Local Player (Team 0, Index 0)
        ByteStream.WriteInt(0); // High ID
        ByteStream.WriteInt((int)_player.LowId); // Low ID
        ByteStream.WriteVInt(0); // Player Index
        ByteStream.WriteVInt(0); // Team Index
        ByteStream.WriteVInt(0);
        ByteStream.WriteInt(0);
        ByteStream.WriteDataReference(16, _brawlerId);
        ByteStream.WriteDataReference(29, _skinId);
        ByteStream.WriteByte(0);
        ByteStream.WriteString(_player.Name);
        ByteStream.WriteVInt(100);
        ByteStream.WriteVInt(28000000 + _player.ProfileIcon);
        ByteStream.WriteVInt(43000000 + _player.NameColor);

        // Bots
        for (var i = 1; i < _playerCount; i++)
        {
            var team = (i < _playerCount / 2) ? 0 : 1;
            ByteStream.WriteInt(0);
            ByteStream.WriteInt(i + 1);
            ByteStream.WriteVInt(i);
            ByteStream.WriteVInt(team);
            ByteStream.WriteVInt(0);
            ByteStream.WriteInt(0);
            ByteStream.WriteDataReference(16, (i * 3) % 45);
            ByteStream.WriteDataReference(29, 0);
            ByteStream.WriteByte(0);
            ByteStream.WriteString($"Bot {i}");
            ByteStream.WriteVInt(100);
            ByteStream.WriteVInt(28000000);
            ByteStream.WriteVInt(43000000);
        }

        ByteStream.WriteInt(0);
        ByteStream.WriteInt(0);
        ByteStream.WriteInt(0);

        ByteStream.WriteVInt(0);
        ByteStream.WriteVInt(1);
        ByteStream.WriteVInt(1);

        ByteStream.WriteBoolean(true);
        ByteStream.WriteVInt(0);
        ByteStream.WriteVInt(0);

        ByteStream.WriteDataReference(15, _locationId);
        ByteStream.WriteBoolean(false);
    }

    public override int GetMessageType() => 20559;
    public override int GetServiceNodeType() => 9;
}
