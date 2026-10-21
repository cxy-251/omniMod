using HarmonyLib;
using OmniMod.Core.Logging;
using OmniMod.Core.Safety;

namespace OmniMod.Machinery.Refineries
{
    /// <summary>
    /// 原油精炼机（Oil Refinery）免小人全自动精炼与安全运转
    /// </summary>
    [HarmonyPatch(typeof(OilRefinery.States), nameof(OilRefinery.States.InitializeStates))]
    internal static class OilRefinery_States_InitializeStates_AutoRun_Patch
    {
        private static void Postfix(OilRefinery.States __instance)
        {
            if (__instance == null) return;

            __instance.ready
                .Enter((OilRefinery.StatesInstance smi) =>
                {
                    if (smi == null || smi.master == null) return;
                    Operational op = smi.master.GetComponent<Operational>();
                    if (op != null)
                    {
                        op.SetActive(true);
                    }
                    BuildingAnimHelper.PlayWorkingAnim(smi.GetComponent<KBatchedAnimController>());
                    smi.GetComponent<KSelectable>()?.AddStatusItem(Db.Get().BuildingStatusItems.Working);
                })
                .Exit((OilRefinery.StatesInstance smi) =>
                {
                    if (smi == null || smi.master == null) return;
                    Operational op = smi.master.GetComponent<Operational>();
                    if (op != null)
                    {
                        op.SetActive(false);
                    }
                    BuildingAnimHelper.StopWorkingAnim(smi.GetComponent<KBatchedAnimController>(), op);
                    smi.GetComponent<KSelectable>()?.RemoveStatusItem(Db.Get().BuildingStatusItems.Working);
                });
        }
    }

    [HarmonyPatch(typeof(OilRefinery), "OnSpawn")]
    internal static class OilRefinery_OnSpawn_AutoRun_Patch
    {
        private static void Postfix(OilRefinery __instance)
        {
            if (__instance == null) return;

            ExceptionShield.Execute("OilRefinery.OnSpawn", () =>
            {
                var smi = __instance.GetSMI<OilRefinery.StatesInstance>();
                if (smi != null && smi.GetCurrentState() == smi.sm.ready)
                {
                    __instance.GetComponent<Operational>()?.SetActive(true);
                    BuildingAnimHelper.PlayWorkingAnim(__instance.GetComponent<KBatchedAnimController>());
                    __instance.GetComponent<KSelectable>()?.AddStatusItem(Db.Get().BuildingStatusItems.Working);
                }

                var remote = __instance.GetComponent<ManuallySetRemoteWorkTargetComponent>();
                if (remote != null && remote.RemoteDockChore != null)
                {
                    remote.RemoteDockChore.AddPrecondition(MachineryPreconditions.NeverAvailable);
                }

                ModLog.Throttle($"oil_ref_{__instance.PrefabID().Name}", $"机械自动运行：{__instance.name} 设为免人力自动精炼原油", 60f);
            });
        }
    }

    [HarmonyPatch(typeof(ManuallySetRemoteWorkTargetComponent), nameof(ManuallySetRemoteWorkTargetComponent.SetChore))]
    internal static class OilRefinery_SetChore_AutoRun_Patch
    {
        private static void Postfix(ManuallySetRemoteWorkTargetComponent __instance, Chore chore_)
        {
            if (__instance != null && __instance.GetComponent<OilRefinery>() != null && chore_ != null)
            {
                chore_.AddPrecondition(MachineryPreconditions.NeverAvailable);
            }
        }
    }
}
