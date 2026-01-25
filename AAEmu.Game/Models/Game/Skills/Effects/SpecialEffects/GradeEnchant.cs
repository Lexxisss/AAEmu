using System;
using System.Collections.Generic;

using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Formulas;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.Items.Templates;
using AAEmu.Game.Models.Game.Units;
using NLog;

namespace AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects;

public class GradeEnchant : SpecialEffectAction
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    protected override SpecialType SpecialEffectActionType => SpecialType.GradeEnchant;

    private enum GradeEnchantResult
    {
        Break = 0,
        Downgrade = 1,
        Fail = 2,
        Success = 3,
        GreatSuccess = 4
    }

    public override void Execute(
        BaseUnit caster,
        SkillCaster casterObj,
        BaseUnit target,
        SkillCastTarget targetObj,
        CastAction castObj,
        Skill skill,
        SkillObject skillObject,
        DateTime time,
        int value1,
        int value2,
        int value3,
        int value4)
    {
        Logger.Info($"[GRADE_ENCHANT] Starting grade enchant - Character: {caster?.Name}, ItemTargetId: {(targetObj as SkillCastItemTarget)?.Id}, ScrollId: {(casterObj as SkillItem)?.ItemId}, ItemType: {value3}");

        if (caster is not Character character)
            return;

        if (casterObj is not SkillItem scroll)
            return;

        if (targetObj is not SkillCastItemTarget itemTarget)
            return;

        var item = character.Inventory.GetItemById(itemTarget.Id);
        if (item == null)
        {
            Logger.Error($"[GRADE_ENCHANT] Target item not found: {itemTarget.Id}");
            return;
        }

        Logger.Info($"[GRADE_ENCHANT] Processing item: {item.TemplateId} ({item.Template?.Name}), Grade: {item.Grade}");

        bool isLucky = value1 != 0;

        bool useCharm = false;
        ItemGradeEnchantingSupport charmInfo = null;
        Item charmItem = null;

        if (skillObject is SkillObjectItemGradeEnchantingSupport charmObj &&
            charmObj.SupportItemId != 0)
        {
            charmItem = character.Inventory.GetItemById(charmObj.SupportItemId);
            if (charmItem == null)
                return;

            charmInfo = ItemManager.Instance.GetItemGradEnchantingSupportByItemId(charmItem.TemplateId);
            if (charmInfo == null)
                return;

            if ((charmInfo.RequireGradeMin != -1 && item.Grade < charmInfo.RequireGradeMin) ||
                (charmInfo.RequireGradeMax != -1 && item.Grade > charmInfo.RequireGradeMax))
            {
                character.SendErrorMessage(ErrorMessageType.GradeEnchantMax);
                return;
            }

            useCharm = true;
            Logger.Info($"[GRADE_ENCHANT] Using charm: {charmItem.TemplateId}, RequireGradeMin: {charmInfo.RequireGradeMin}, RequireGradeMax: {charmInfo.RequireGradeMax}");
        }

        var gradeTemplate = ItemManager.Instance.GetGradeTemplate(item.Grade);
        if (gradeTemplate == null)
        {
            Logger.Error($"[GRADE_ENCHANT] Grade template not found for grade: {item.Grade}");
            return;
        }

        int cost = GoldCost(gradeTemplate, item, value3);
        Logger.Info($"[GRADE_ENCHANT] Calculated cost: {cost}, Character money: {character.Money}, ItemType: {value3}");
        
        if (cost < 0)
        {
            Logger.Error($"[GRADE_ENCHANT] Invalid cost calculation: {cost} for item {item.TemplateId}, grade {item.Grade}, type {value3}");
            character.SendErrorMessage(ErrorMessageType.ItemCannotUse);
            return;
        }
        
        if (character.Money < cost)
        {
            Logger.Warn($"[GRADE_ENCHANT] Not enough money - Need: {cost}, Have: {character.Money}");
            character.SendErrorMessage(ErrorMessageType.NotEnoughMoney);
            return;
        }

        if (!character.Inventory.CheckItems(SlotType.Inventory, scroll.ItemTemplateId, 1))
        {
            Logger.Error($"[GRADE_ENCHANT] Scroll not found in inventory: {scroll.ItemTemplateId}");
            character.SendErrorMessage(ErrorMessageType.NotEnoughRequiredItem);
            return;
        }

        byte initialGrade = item.Grade;

        var result = RollRegrade(item, isLucky, useCharm, charmInfo);

        Logger.Info($"[GRADE_ENCHANT] Regrade result: {result}, Final grade: {item.Grade}");

        if (result == GradeEnchantResult.Break)
        {
            Logger.Info($"[GRADE_ENCHANT] Item broke during regrade: {item.TemplateId}");
            item._holdingContainer.RemoveItem(ItemTaskType.GradeEnchant, item, true);
        }
        else
        {
            character.SendPacket(new SCItemTaskSuccessPacket(
                ItemTaskType.GradeEnchant,
                new List<ItemTask> { new ItemGradeChange(item, item.Grade) },
                new List<ulong>()));
        }

        character.SubtractMoney(SlotType.Inventory, cost);
        Logger.Info($"[GRADE_ENCHANT] Subtracted {cost} money from character {character.Name}");

        if (useCharm)
        {
            character.Inventory.Bag.ConsumeItem(
                ItemTaskType.GradeEnchant,
                charmItem.TemplateId,
                1,
                charmItem);
            Logger.Info($"[GRADE_ENCHANT] Consumed charm: {charmItem.TemplateId}");
        }

        character.SendPacket(new SCItemGradeEnchantResultPacket(
            (byte)result,
            item,
            initialGrade,
            item.Grade,
            0u,
            0,
            false));

        character.BroadcastPacket(new SCSkillEndedPacket(skill.TlId), true);

        if (item.Grade >= 8 &&
            (result == GradeEnchantResult.Success || result == GradeEnchantResult.GreatSuccess))
        {
            Logger.Info($"[GRADE_ENCHANT] High grade achievement: Grade {item.Grade} for item {item.TemplateId}");
            // WorldManager.Instance.BroadcastPacketToServer(...)
        }
    }

    private static GradeEnchantResult RollRegrade(
        Item item,
        bool isLucky,
        bool useCharm,
        ItemGradeEnchantingSupport charmInfo)
    {
        var ratio = ItemManager.Instance.GetGradeEnchantRatio(item);
        if (ratio == null)
        {
            Logger.Warn($"[Enchant] No ratio found for ItemId={item.Template?.Id}, Grade={item.Grade}");
            return GradeEnchantResult.Fail;
        }

        int success = ratio.SuccessRatio;
        int great = ratio.GreatSuccessRatio;
        int brk = ratio.BreakRatio;
        int down = ratio.DowngradeRatio;

        if (useCharm && charmInfo != null)
        {
            success = ApplyCharm(success, charmInfo.AddSuccessRatio, charmInfo.AddSuccessMul);
            great = ApplyCharm(great, charmInfo.AddGreatSuccessRatio, charmInfo.AddGreatSuccessMul);
            brk = ApplyCharm(brk, charmInfo.AddBreakRatio, charmInfo.AddBreakMul);
            down = ApplyCharm(down, charmInfo.AddDowngradeRatio, charmInfo.AddDowngradeMul);
        }

        Logger.Info(
            $"[Enchant Roll] Item={item.Template.Id}, Grade={item.Grade}, " +
            $"Success={success / 100.0}%, Great={great / 100.0}%, Break={brk / 100.0}%, Down={down / 100.0}%");

        if (Rand.Next(10000) < success)
        {
            if (isLucky && Rand.Next(10000) < great)
            {
                int inc = useCharm ? 2 + charmInfo.AddGreatSuccessGrade : 2;
                var next = GetNextGrade(ItemManager.Instance.GetGradeTemplate(item.Grade), inc);
                item.Grade = (byte)next.Grade;
                return GradeEnchantResult.GreatSuccess;
            }

            var nextGrade = GetNextGrade(ItemManager.Instance.GetGradeTemplate(item.Grade), 1);
            item.Grade = (byte)nextGrade.Grade;
            return GradeEnchantResult.Success;
        }

        if (Rand.Next(10000) < brk)
            return GradeEnchantResult.Break;

        if (Rand.Next(10000) < down)
        {
            item.Grade = (byte)Rand.Next(ratio.DowngradeMin, ratio.DowngradeMax + 1);
            return GradeEnchantResult.Downgrade;
        }

        return GradeEnchantResult.Fail;
    }

    private static int GoldCost(GradeTemplate gradeTemplate, Item item, int itemType)
    {
        Logger.Debug($"[GRADE_ENCHANT] Calculating gold cost - ItemId: {item.TemplateId}, Grade: {item.Grade}, ItemType: {itemType}");

        uint slotTypeId = itemType switch
        {
            1 => ((WeaponTemplate)item.Template).HoldableTemplate.SlotTypeId,
            2 => ((ArmorTemplate)item.Template).SlotTemplate.SlotTypeId,
            24 => ((AccessoryTemplate)item.Template).SlotTemplate.SlotTypeId,
            28 => 1000, // Временное решение для корабельной экипировки - используем специальный SlotTypeId
            _ => 0
        };

        Logger.Debug($"[GRADE_ENCHANT] Determined SlotTypeId: {slotTypeId} for ItemType: {itemType}");

        if (slotTypeId == 0)
        {
            Logger.Error($"[GRADE_ENCHANT] Invalid SlotTypeId (0) for item {item.TemplateId}, ItemType: {itemType}");
            return -1;
        }

        var costData = ItemManager.Instance.GetEquipSlotEnchantingCost(slotTypeId);
        if (costData == null)
        {
            if (itemType == 28)
            {
                Logger.Warn($"[GRADE_ENCHANT] No cost data found for ship equipment SlotTypeId: {slotTypeId}, trying armor cost as fallback");
                // Используем стоимость от брони (slot_type_id 2) как запасной вариант
                costData = ItemManager.Instance.GetEquipSlotEnchantingCost(2);
                if (costData != null)
                {
                    Logger.Info($"[GRADE_ENCHANT] Using armor cost as fallback for ship equipment: {costData.Cost}");
                }
            }
            
            if (costData == null)
            {
                Logger.Error($"[GRADE_ENCHANT] No cost data found for SlotTypeId: {slotTypeId}");
                return -1;
            }
        }

        Logger.Debug($"[GRADE_ENCHANT] Cost data for SlotTypeId {slotTypeId}: {costData.Cost}");

        var parameters = new Dictionary<string, double>
        {
            ["item_grade"] = gradeTemplate.EnchantCost,
            ["item_level"] = item.Template.Level,
            ["equip_slot_enchant_cost"] = costData.Cost
        };

        Logger.Debug($"[GRADE_ENCHANT] Formula parameters - item_grade: {gradeTemplate.EnchantCost}, item_level: {item.Template.Level}, equip_slot_enchant_cost: {costData.Cost}");

        var formula = FormulaManager.Instance.GetFormula((uint)FormulaKind.GradeEnchantCost);
        if (formula == null)
        {
            Logger.Error($"[GRADE_ENCHANT] GradeEnchantCost formula not found!");
            return -1;
        }

        var result = (int)formula.Evaluate(parameters);
        Logger.Debug($"[GRADE_ENCHANT] Formula result: {result}");
        
        return result;
    }

    private static GradeTemplate GetNextGrade(GradeTemplate current, int delta)
    {
        return ItemManager.Instance.GetGradeTemplateByOrder(current.GradeOrder + delta);
    }

    private static int ApplyCharm(int baseChance, int add, int mul)
    {
        return baseChance + add + (int)(baseChance * (mul / 100.0));
    }
}
