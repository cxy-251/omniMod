using Klei.AI;

namespace OmniMod.Rooms
{
    /// <summary>四种标志展馆使用同一组数值及说明，能力方向分别指定。</summary>
    internal static class GalleryBenefits
    {
        internal const int Morale = 6;
        internal const int AttributeBonus = 3;
        private const int StressReductionPerCycle = 15;
        internal const int ExperienceBonusPercent = 50;
        internal const float ExperienceMultiplier = 1f + ExperienceBonusPercent / 100f;

        internal static string Summary(string attributeName) => ModStrings.T(
            $"+{Morale} 士气\n-{StressReductionPerCycle}% 压力/周期\n+{AttributeBonus} {attributeName}\n+{ExperienceBonusPercent}% 技能点经验",
            $"+{Morale} Morale\n-{StressReductionPerCycle}% Stress/cycle\n+{AttributeBonus} {attributeName}\n+{ExperienceBonusPercent}% Skill Point Experience").Resolve();

        internal static ModStrings.Bilingual BuildingEffect(ModStrings.Bilingual effectName, ModStrings.Bilingual attributeName) =>
            ModStrings.T(
                $"到访后获得【{effectName.Zh}】：+{Morale} 士气、-{StressReductionPerCycle}% 压力/周期、+{AttributeBonus} {attributeName.Zh}、+{ExperienceBonusPercent}% 技能点经验，持续一周期。",
                $"Visitors gain {effectName.En}: +{Morale} Morale, -{StressReductionPerCycle}% Stress/cycle, +{AttributeBonus} {attributeName.En} and +{ExperienceBonusPercent}% skill point experience for one cycle.");

        internal static void Apply(Effect effect, string attributeId, string effectName)
        {
            var db = Db.Get();
            effect.Add(new AttributeModifier(db.Attributes.QualityOfLife.Id, Morale, effectName));
            effect.Add(new AttributeModifier(db.Amounts.Stress.deltaAttribute.Id, -StressReductionPerCycle / 600f, effectName));
            effect.Add(new AttributeModifier(attributeId, AttributeBonus, effectName));
        }
    }
}
