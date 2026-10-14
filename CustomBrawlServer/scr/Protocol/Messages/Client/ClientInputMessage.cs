using System;
using System.Collections.Generic;
using CustomBrawlServer.Logic;
using CustomBrawlServer.Protocol;

namespace CustomBrawlServer.Protocol.Messages.Client;

public sealed record ClientBattleInput(int Index, int Type, int X, int Y, bool AutoAttack);

/// <summary>
/// Supercell Laser ClientInputMessage (packet 10555).
/// Carries real-time player movement (Type=2), basic attack (Type=0), super ability (Type=1),
/// gadget (Type=5), emote (Type=8), and hypercharge (Type=10) inputs during a match.
/// </summary>
public class ClientInputMessage : PiranhaMessage
{
    public List<ClientBattleInput> Inputs { get; } = new();

    public override void Decode()
    {
        base.Decode();

        if (ByteStream.IsAtEnd()) return;

        try
        {
            var count = ByteStream.ReadVInt();
            if (count <= 0 || count > 32)
            {
                // Fallback single-input layout
                ByteStream.ResetOffset();
                var type = ByteStream.ReadVInt();
                var x = ByteStream.ReadVInt();
                var y = ByteStream.ReadVInt();
                var auto = !ByteStream.IsAtEnd() && ByteStream.ReadBoolean();
                Inputs.Add(new ClientBattleInput(0, type, x, y, auto));
                return;
            }

            for (var i = 0; i < count && !ByteStream.IsAtEnd(); i++)
            {
                var index = ByteStream.ReadVInt();
                var type = ByteStream.ReadVInt();
                var x = ByteStream.ReadVInt();
                var y = ByteStream.ReadVInt();
                var auto = !ByteStream.IsAtEnd() && ByteStream.ReadBoolean();
                Inputs.Add(new ClientBattleInput(index, type, x, y, auto));
            }
        }
        catch
        {
            // Defensive ignore
        }
    }

    public void Execute(Player player)
    {
        foreach (var input in Inputs)
        {
            BattleManager.Instance.HandleClientInput(player, input.Type, input.X, input.Y, input.AutoAttack);
        }
    }

    public override int GetMessageType() => 10555;
    public override int GetServiceNodeType() => 27;
}
