using CustomBrawlServer.Config;
using CustomBrawlServer.Logic;
using CustomBrawlServer.Protocol;

namespace CustomBrawlServer.Protocol.Messages.Server;

public class LoginOkMessage : PiranhaMessage
{
    private readonly Player _player;
    private readonly int _clientMinor;
    private readonly int _clientBuild;

    public LoginOkMessage(Player player, int clientMinor = 250, int clientBuild = 1)
    {
        MessageVersion = 1;
        _player = player;
        _clientMinor = clientMinor > 0 ? clientMinor : 250;
        _clientBuild = clientBuild > 0 ? clientBuild : 1;
    }

    public override void Encode()
    {
        base.Encode();

        // Account ID (LogicLong)
        ByteStream.WriteInt((int)_player.HighId);
        ByteStream.WriteInt((int)_player.LowId);

        // Home ID (LogicLong)
        ByteStream.WriteInt((int)_player.HighId);
        ByteStream.WriteInt((int)_player.LowId);

        ByteStream.WriteString(_player.Token);
        ByteStream.WriteString(string.Empty); // Facebook ID
        ByteStream.WriteString(string.Empty); // GameCenter ID
        ByteStream.WriteInt(68);              // Client Major
        ByteStream.WriteInt(_clientMinor);    // Client Minor (matches client)
        ByteStream.WriteInt(_clientBuild);    // Client Build (matches client)
        ByteStream.WriteString(ServerConfig.Instance.Server.Environment); // "dev" or "prod"
        ByteStream.WriteInt(0);               // Session count
        ByteStream.WriteInt(0);               // Play time seconds
        ByteStream.WriteInt(0);               // Days since started
        ByteStream.WriteString(string.Empty); // Facebook App ID
        ByteStream.WriteString(string.Empty); // Server Time
        ByteStream.WriteString(string.Empty); // Account Created Date
        ByteStream.WriteInt(0);               // Startup cooldown
        ByteStream.WriteString(string.Empty); // Google Service ID
        ByteStream.WriteString("US");         // Region
        ByteStream.WriteString(string.Empty); // Content URL
        ByteStream.WriteInt(0);
        ByteStream.WriteString(string.Empty);
        ByteStream.WriteInt(2);
        ByteStream.WriteString("https://game-assets.brawlstarsgame.com");
        ByteStream.WriteString("http://a678dbc1c015a893c9fd-4e8cc3b1ad3a3c940c504815caefa967.r87.cf2.rackcdn.com");
        ByteStream.WriteInt(2);
        ByteStream.WriteString("https://event-assets.brawlstars.com");
        ByteStream.WriteString("https://24b999e6da07674e22b0-8209975788a0f2469e68e84405ae4fcf.ssl.cf2.rackcdn.com/event-assets");
        ByteStream.WriteVInt(0);
        ByteStream.WriteString(string.Empty);
        ByteStream.WriteBoolean(true);
        ByteStream.WriteBoolean(false);
        ByteStream.WriteString(string.Empty);
        ByteStream.WriteString(string.Empty);
        ByteStream.WriteString(string.Empty);
        ByteStream.WriteString(string.Empty);
        ByteStream.WriteString(string.Empty);
        ByteStream.WriteBoolean(false);
        ByteStream.WriteBoolean(false);
        ByteStream.WriteBoolean(false);
        ByteStream.WriteBoolean(false);
        ByteStream.WriteBoolean(false);
    }

    public override int GetMessageType() => 20104;
    public override int GetServiceNodeType() => 1;
}
