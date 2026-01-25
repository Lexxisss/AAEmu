using System;
using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Chat;
using AAEmu.Game.Scripts.Commands; // для UseSkill

namespace AAEmu.Game.Core.Packets.C2G;

public class CSSendChatMessagePacket : GamePacket
{
    public CSSendChatMessagePacket() : base(CSOffsets.CSSendChatMessagePacket, 5)
    {
    }

    public override void Read(PacketStream stream)
    {
        var type = (ChatType)stream.ReadInt16();
        var unk1 = stream.ReadInt16();
        var unk2 = stream.ReadInt32();

        var targetName = stream.ReadString();
        var message = stream.ReadString();
        var languageType = stream.ReadByte();
        var ability = stream.ReadInt32();

        Logger.Debug(message);

        // 1️⃣ Проверка команд
        if (message.StartsWith(CommandManager.CommandPrefix))
        {
            if (CommandManager.Instance.Handle(Connection.ActiveChar, message.Substring(CommandManager.CommandPrefix.Length).Trim(), out _))
                return;
        }

        // 2️⃣ Автоскилл по тексту "\клад"
        if (message.Equals(@"\клад", StringComparison.OrdinalIgnoreCase))
        {
            var useSkillCommand = new UseSkill();
            string[] args = new string[] { "33599" }; // skillId 33599 на себя
            useSkillCommand.Execute(Connection.ActiveChar, args, null);

            // Можно вернуть, чтобы сообщение не шло в чат
            return;
        }

        // 3️⃣ Остальная обработка чата
        switch (type)
        {
            case ChatType.Whisper: // whisper
                var target = WorldManager.Instance.GetCharacter(targetName);
                if ((target == null) || (!target.IsOnline))
                {
                    Connection.ActiveChar.SendErrorMessage(ErrorMessageType.WhisperNoTarget);
                }
                else if (target.Faction.MotherId != Connection.ActiveChar.Faction.MotherId)
                {
                    Connection.ActiveChar.SendErrorMessage(ErrorMessageType.ChatCannotWhisperToHostile);
                }
                else
                {
                    var packet = new SCChatMessagePacket(ChatType.Whisper, Connection.ActiveChar, message, ability, languageType);
                    target.SendPacket(packet);
                    var packet_me = new SCChatMessagePacket(ChatType.Whispered, target, message, ability, languageType);
                    Connection.SendPacket(packet_me);
                }
                break;

            case ChatType.White: // say
                Connection.ActiveChar.BroadcastPacket(
                    new SCChatMessagePacket(type, Connection.ActiveChar, message, ability, languageType), true);
                break;

            case ChatType.RaidLeader:
            case ChatType.Raid:
                var teamRaid = TeamManager.Instance.GetActiveTeamByUnit(Connection.ActiveChar.Id);
                if (teamRaid != null)
                {
                    if ((type == ChatType.RaidLeader) && (teamRaid.OwnerId != Connection.ActiveChar.Id))
                        Connection.ActiveChar.SendErrorMessage(ErrorMessageType.ChatNotRaidOwner);
                    else
                        ChatManager.Instance.GetRaidChat(teamRaid).SendPacket(
                            new SCChatMessagePacket(type, Connection.ActiveChar, message, ability, languageType));
                }
                else
                {
                    Connection.ActiveChar.SendErrorMessage(ErrorMessageType.ChatNotInRaid);
                }
                break;

            case ChatType.Party:
                var partyRaid = TeamManager.Instance.GetActiveTeamByUnit(Connection.ActiveChar.Id);
                if (partyRaid != null)
                {
                    ChatManager.Instance.GetPartyChat(partyRaid, Connection.ActiveChar)
                        .SendMessage(Connection.ActiveChar, message, ability, languageType);
                }
                else
                {
                    Connection.ActiveChar.SendErrorMessage(ErrorMessageType.ChatNotInParty);
                }
                break;

            case ChatType.Trade: // trade
            case ChatType.GroupFind: // lfg
            case ChatType.Shout: // shout
                ChatManager.Instance.GetZoneChat(Connection.ActiveChar.Transform.ZoneId).SendPacket(
                    new SCChatMessagePacket(type, Connection.ActiveChar, message, ability, languageType));
                break;

            case ChatType.Clan:
                if (Connection.ActiveChar.Expedition != null)
                    ChatManager.Instance.GetGuildChat(Connection.ActiveChar.Expedition)
                        .SendMessage(Connection.ActiveChar, message, ability, languageType);
                else
                    Connection.ActiveChar.SendErrorMessage(ErrorMessageType.ChatNotInExpedition);
                break;

            case ChatType.Family:
                if (Connection.ActiveChar.Family > 0)
                    ChatManager.Instance.GetFamilyChat(Connection.ActiveChar.Family)
                        .SendMessage(Connection.ActiveChar, message, ability, languageType);
                else
                    Connection.ActiveChar.SendErrorMessage(ErrorMessageType.ChatNotInFamily);
                break;

            case ChatType.Region:
                ChatManager.Instance.GetNationChat(Connection.ActiveChar.Race)
                    .SendMessage(Connection.ActiveChar, message, ability, languageType);
                break;

            case ChatType.Ally:
                ChatManager.Instance.GetFactionChat(Connection.ActiveChar.Faction.MotherId)
                    .SendMessage(Connection.ActiveChar, message, ability, languageType);
                break;

            default:
                Logger.Warn("Unsupported chat type {0} from {1}", type, Connection.ActiveChar.Name);
                break;
        }
    }
}
