using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Items;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCItemGradeEnchantResultPacket : GamePacket
{
    // result :
    //  0 = break, 1 = downgrade, 2 = fail, 3 = success, 4 = great success 
    private readonly byte _result;
    private readonly Item _item;
    private readonly byte _initialGrade;
    private readonly byte _grade;
    private readonly uint _type3;
    private readonly int _breakRewardItemCount;
    private readonly bool _breakRewardByMail;

    public SCItemGradeEnchantResultPacket(
        byte result,
        Item item,
        byte initialGrade,
        byte grade,
        uint type3,
        int breakRewardItemCount,
        bool breakRewardByMail)
        : base(SCOffsets.SCItemGradeEnchantResultPacket, 5)
    {
        _result = result;
        _item = item;
        _initialGrade = initialGrade;
        _grade = grade;
        _type3 = type3;
        _breakRewardItemCount = breakRewardItemCount;
        _breakRewardByMail = breakRewardByMail;
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(_result);
        stream.Write(_item);
        stream.Write(_initialGrade);
        stream.Write(_grade);
        stream.Write(_type3);
        stream.Write(_breakRewardItemCount);
        stream.Write(_breakRewardByMail);

        return stream;
    }
}
