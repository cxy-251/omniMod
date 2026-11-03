using HarmonyLib;
using Klei.AI;

namespace OmniMod.Rooms
{
    /// <summary>
    /// Learning 只影响属性训练；技能点使用独立的 MinionResume 经验。
    /// 在最终入账入口增加一次，不修改升级曲线、不重算旧经验，也不把负数扣减放大。
    /// </summary>
    [HarmonyPatch(typeof(MinionResume), nameof(MinionResume.AddExperience))]
    internal static class LandmarkSkillExperiencePatch
    {
        private static void Prefix(MinionResume __instance, ref float amount)
        {
            if (amount <= 0f || float.IsNaN(amount) || float.IsInfinity(amount)) return;
            Effects effects = __instance.GetComponent<Effects>();
            if (effects != null) amount *= LandmarkRooms.GetSkillExperienceMultiplier(effects);
        }
    }
}
