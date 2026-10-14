using System;
using System.Collections.Generic;
using CustomBrawlServer.Protocol;

namespace CustomBrawlServer.Protocol.Commands;

public sealed class LogicSchemaCommand : LogicCommand
{
    private readonly int _commandType;
    private readonly Action<ByteStream> _decodeBody;
    private readonly bool _readIntBeforeBase;

    public LogicSchemaCommand(int commandType, Action<ByteStream> decodeBody, bool readIntBeforeBase = false)
    {
        _commandType = commandType;
        _decodeBody = decodeBody;
        _readIntBeforeBase = readIntBeforeBase;
    }

    public override int GetCommandType() => _commandType;

    public override void Decode(ByteStream stream)
    {
        if (_readIntBeforeBase)
        {
            stream.ReadInt();
        }
        base.Decode(stream);
        _decodeBody(stream);
    }
}

public static class LogicCommandFactory
{
    private static readonly Dictionary<int, Func<LogicCommand>> Commands = BuildCommands();

    private static Dictionary<int, Func<LogicCommand>> BuildCommands()
    {
        var map = new Dictionary<int, Func<LogicCommand>>
        {
            [505] = () => new LogicSetPlayerThumbnailCommand(),
            [506] = () => new LogicChangeSkinCommand(),
            [520] = () => new LogicUpgradeBrawlerCommand(),
            [522] = () => new LogicHeroSeenCommand(),
            [525] = () => new LogicSelectCharacterCommand(),
            [526] = () => new LogicSelectHeroCommand(),
            [527] = () => new LogicSetPlayerNameColorCommand(),
            [532] = () => new LogicSelectStarPowerCommand(),
            [538] = () => new LogicSelectEmoteCommand(),
            [557] = () => new LogicGenericSettingCommand()
        };

        void Reg(Action<ByteStream> decode, params int[] ids)
        {
            foreach (var id in ids)
            {
                if (!map.ContainsKey(id))
                    map[id] = () => new LogicSchemaCommand(id, decode);
            }
        }

        // Exact v68 libg.so client command decode schemas (503-600)
        Reg(_ => { }, 509, 511, 512, 526, 540, 541, 545, 553, 563, 579, 588, 590);
        Reg(s => { s.ReadVInt(); }, 508, 521, 536, 539, 552, 554, 571, 580, 586, 593, 595, 596);
        Reg(s => { s.ReadVInt(); s.ReadVInt(); }, 503, 514, 528, 531, 535, 556, 582, 592, 594);
        Reg(s => { s.ReadVInt(); s.ReadVInt(); s.ReadVInt(); }, 587, 591);
        Reg(s => { s.ReadVInt(); s.ReadVInt(); s.ReadVInt(); s.ReadVInt(); }, 515, 589);
        Reg(s => { s.ReadDataReference(); }, 505, 527, 529, 532, 551, 559, 561, 562, 565, 570, 598, 599);
        Reg(s => { s.ReadDataReference(); s.ReadVInt(); }, 506, 538, 555, 583);
        Reg(s => { s.ReadDataReference(); s.ReadBoolean(); }, 520, 547, 577);
        Reg(s => { s.ReadDataReference(); s.ReadInt(); }, 522, 597);
        Reg(s => { s.ReadDataReference(); s.ReadByte(); }, 525);
        Reg(s => { s.ReadDataReference(); s.ReadDataReference(); }, 560);
        Reg(s => { s.ReadInt(); }, 530, 572, 573, 574, 578);
        Reg(s => { s.ReadInt(); s.ReadInt(); }, 567, 575, 576);
        Reg(s => { s.ReadInt(); s.ReadInt(); s.ReadInt(); }, 581);
        Reg(s => { s.ReadBoolean(); }, 544, 548);
        Reg(s => { s.ReadBoolean(); s.ReadBoolean(); }, 584, 600);
        Reg(s => { s.ReadVInt(); s.ReadBoolean(); }, 557);

        // Composite commands
        Reg(s => { s.ReadVInt(); s.ReadDataReference(); s.ReadVInt(); s.ReadVInt(); }, 517);
        Reg(s => { s.ReadVInt(); s.ReadDataReference(); s.ReadDataReference(); s.ReadVInt(); s.ReadDataReference(); s.ReadVInt(); s.ReadDataReference(); }, 519);
        Reg(s => { s.ReadDataReference(); s.ReadDataReference(); s.ReadByte(); s.ReadBoolean(); }, 543);
        Reg(s => { s.ReadVInt(); s.ReadVInt(); s.ReadVInt(); s.ReadBoolean(); s.ReadVInt(); }, 550);
        Reg(s => { s.ReadDataReference(); s.ReadDataReference(); s.ReadVInt(); s.ReadBoolean(); }, 558);
        Reg(s => { s.ReadDataReference(); s.ReadDataReference(); s.ReadBoolean(); s.ReadVInt(); }, 568);
        map[564] = () => new LogicSchemaCommand(564, s => { s.ReadDataReference(); }, readIntBeforeBase: true);

        return map;
    }

    public static bool CommandExists(int commandType) => Commands.ContainsKey(commandType);

    public static LogicCommand? Create(int commandType)
    {
        if (Commands.TryGetValue(commandType, out var factory))
            return factory();
        return null;
    }
}
