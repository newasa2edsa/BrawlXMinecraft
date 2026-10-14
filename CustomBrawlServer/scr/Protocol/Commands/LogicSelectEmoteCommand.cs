using System;
using CustomBrawlServer.Logic;
using CustomBrawlServer.Protocol;

namespace CustomBrawlServer.Protocol.Commands;

public class LogicHeroSeenCommand : LogicCommand
{
    public (int ClassId, int InstanceId) Character { get; set; }
    public int SeenState { get; set; }

    public override int GetCommandType() => 522;

    public override void Decode(ByteStream stream)
    {
        base.Decode(stream);
        Character = stream.ReadDataReference();
        if (!stream.IsAtEnd())
        {
            SeenState = stream.ReadInt();
        }
    }

    public override void Execute(Player player)
    {
        Console.WriteLine($"[Command] Player #{player.LowId} marked Brawler ({Character.ClassId}, {Character.InstanceId}) as seen (state={SeenState}).");
    }
}

public class LogicSelectEmoteCommand : LogicCommand
{
    public (int ClassId, int InstanceId) Emote { get; set; }
    public int Slot { get; set; }

    public override int GetCommandType() => 538;

    public override void Decode(ByteStream stream)
    {
        base.Decode(stream);
        Emote = stream.ReadDataReference();
        if (!stream.IsAtEnd())
        {
            Slot = stream.ReadVInt();
        }
    }

    public override void Execute(Player player)
    {
        Console.WriteLine($"[Command] Player #{player.LowId} selected Emote ({Emote.ClassId}, {Emote.InstanceId}) in slot {Slot}.");
        Network.BridgeServer.Instance?.BroadcastCharacterState(player, "select_emote", online: true, emoteId: Emote.InstanceId);
    }
}
