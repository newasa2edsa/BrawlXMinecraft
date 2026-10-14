using System;
using System.Collections.Generic;
using CustomBrawlServer.Logic;
using CustomBrawlServer.Protocol;
using CustomBrawlServer.Protocol.Commands;

namespace CustomBrawlServer.Protocol.Messages.Client;

public class EndClientTurnMessage : PiranhaMessage
{
    public bool Subtick { get; set; }
    public int Tick { get; set; }
    public int Checksum { get; set; }
    public int CommandsCount { get; set; }
    public List<LogicCommand> Commands { get; } = new();

    public override void Decode()
    {
        base.Decode();

        if (ByteStream.IsAtEnd()) return;

        try
        {
            Subtick = ByteStream.ReadBoolean();
            Tick = ByteStream.ReadVInt();
            CommandsCount = ByteStream.ReadVInt();

            if (CommandsCount < 0 || CommandsCount > 64)
                return;

            for (var i = 0; i < CommandsCount; i++)
            {
                if (ByteStream.IsAtEnd()) break;

                var commandType = ByteStream.ReadVInt();
                var command = LogicCommandFactory.Create(commandType);
                if (command != null)
                {
                    command.Decode(ByteStream);
                    Commands.Add(command);
                    Console.ForegroundColor = ConsoleColor.DarkYellow;
                    Console.WriteLine($"[EndClientTurn] Executing command {commandType} ({command.GetType().Name})");
                    Console.ResetColor();
                }
                else
                {
                    // We can't know how long an unknown command is, so the rest of the turn can't be parsed.
                    Admin.UnhandledTracker.RecordCommand(commandType, ByteStream.GetByteArray(), ByteStream.GetOffset());
                    Console.ForegroundColor = ConsoleColor.DarkGray;
                    Console.WriteLine($"[EndClientTurn] Received unhandled command {commandType} (type 'unhandled' to inspect)");
                    Console.ResetColor();
                    break;
                }
            }

            if (!ByteStream.IsAtEnd())
            {
                var checksumLength = ByteStream.ReadInt();
                if (checksumLength > 0 && !ByteStream.IsAtEnd())
                {
                    ByteStream.ReadBytes(checksumLength);
                }
            }
        }
        catch
        {
            // Defensive ignore
        }
    }

    public void Execute(Player player)
    {
        foreach (var cmd in Commands)
        {
            try
            {
                cmd.Execute(player);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[EndClientTurn] Error executing command {cmd.GetCommandType()}: {ex.Message}");
            }
        }
    }

    public override int GetMessageType() => 14102;
    public override int GetServiceNodeType() => 9;
}
