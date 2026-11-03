using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using KSerialization;
using UnityEngine;

namespace OmniMod.Logistics
{
    /// <summary>通用建筑供货策略。保留类型名以兼容旧存档；只限制送料，不限制操作和个人取用。</summary>
    [SerializationConfig(MemberSerialization.OptIn)]
    public sealed class ArmOnlyFarmSupply : KMonoBehaviour
    {
        [Serialize]
        private bool onlyArms = true;

        internal bool OnlyArms => onlyArms;

        internal static void Configure(BuildingDef definition)
        {
            GameObject complete = definition != null ? definition.BuildingComplete : null;
            if (complete == null || complete.GetComponent<BuildingComplete>() == null ||
                complete.GetComponent<ChoreConsumer>() != null) return;
            // 从完整预制体识别储存／供货能力，不维护建筑 ID 白名单。普通线管和墙砖不挂组件。
            if (complete.GetComponentInChildren<Storage>(true) == null &&
                complete.GetComponent<SingleEntityReceptacle>() == null &&
                complete.GetComponent<ManualDeliveryKG>() == null) return;
            complete.AddOrGet<ArmOnlyFarmSupply>();
            // 保持已有复制组；没有复制入口的建筑补上同类型复制设置。
            complete.AddOrGet<CopyBuildingSettings>();
        }

        internal static ArmOnlyFarmSupply Resolve(Storage destination)
        {
            if (destination == null || destination.GetComponentInParent<ChoreConsumer>() != null) return null;
            ArmOnlyFarmSupply policy = destination.GetComponent<ArmOnlyFarmSupply>();
            if (policy != null) return policy;
            // 个别建筑把实际输入库放在子对象上，仍由所属建筑的同一个开关控制。
            BuildingComplete building = destination.GetComponentInParent<BuildingComplete>();
            return building != null ? building.GetComponent<ArmOnlyFarmSupply>() : null;
        }

        internal static bool IsSupply(FetchChore request)
        {
            if (request == null || request.choreType == null) return false;
            string id = request.choreType.Id;
            // 个人需求和送餐照护不是补货；普通 FoodFetch（补充食材／调味品）仍由开关控制。
            return id != "BuildFetch" && id != "RepairFetch" && id != "DeliverFood" && request.choreType.urge == null;
        }

        protected override void OnPrefabInit()
        {
            base.OnPrefabInit();
            Subscribe((int)GameHashes.RefreshUserMenu, OnRefreshUserMenu);
            Subscribe((int)GameHashes.CopySettings, OnCopySettings);
        }

        private void OnCopySettings(object data)
        {
            var source = data as GameObject;
            var policy = source != null ? source.GetComponent<ArmOnlyFarmSupply>() : null;
            if (policy != null) SetOnlyArms(policy.onlyArms);
        }

        private void SetOnlyArms(bool value)
        {
            if (onlyArms == value) return;
            onlyArms = value;
            if (onlyArms && !App.isLoading && Components.LiveMinionIdentities != null)
            {
                // 只在用户切换／复制设置时撤销该建筑的人工供货，不在动物状态回调内中断任务。
                for (int i = 0; i < Components.LiveMinionIdentities.Count; i++)
                {
                    var minion = Components.LiveMinionIdentities[i];
                    var driver = minion != null ? minion.GetComponent<ChoreDriver>() : null;
                    if (driver != null && IsSupplyingThisBuilding(driver.GetCurrentChore()))
                        driver.StopChore();
                }
            }
            if (Game.Instance?.userMenu != null) Game.Instance.userMenu.Refresh(gameObject);
        }

        private static readonly AccessTools.FieldRef<FetchAreaChore.StatesInstance, List<FetchChore>> BatchRequests =
            AccessTools.FieldRefAccess<FetchAreaChore.StatesInstance, List<FetchChore>>("chores");

        private bool IsSupplyingThisBuilding(Chore current)
        {
            if (current is FetchChore single) return IsSupply(single) && Resolve(single.destination) == this;
            if (!(current is FetchAreaChore batch) || batch.smi == null) return false;
            List<FetchChore> requests = BatchRequests(batch.smi);
            if (requests != null)
                foreach (FetchChore request in requests)
                    if (IsSupply(request) && Resolve(request.destination) == this) return true;
            return false;
        }

        private void OnRefreshUserMenu(object data)
        {
            if (Game.Instance?.userMenu == null) return;
            string label = onlyArms
                ? ModStrings.T("供货：仅机械臂", "Supply: Auto-Sweepers Only").Resolve()
                : ModStrings.T("供货：允许复制人", "Supply: Duplicants Allowed").Resolve();
            string tooltip = onlyArms
                ? ModStrings.T("当前仅机械臂运送本建筑的补充物资和生产原料。点击允许复制人供货。机械臂仍需覆盖投放点；管道、建造修理、个人取用、拥抱唱歌和实际工作照常。",
                    "Only auto-sweepers deliver this building's supplies and ingredients. Click to allow duplicants. The delivery point must remain in sweeper reach. Pipes, construction, repairs, personal use and work are unaffected.").Resolve()
                : ModStrings.T("当前允许复制人供货。点击改为仅机械臂供货，并取消复制人已接下的本建筑送料订单；个人取用和实际工作照常。",
                    "Duplicant deliveries are allowed. Click to use auto-sweepers only and cancel duplicants' current deliveries to this building. Personal use and actual work remain available.").Resolve();
            Game.Instance.userMenu.AddButton(gameObject, new KIconButtonMenu.ButtonInfo(
                "action_move_to_storage", label, () => SetOnlyArms(!onlyArms), Action.NumActions,
                null, null, null, tooltip));
        }
    }

    [HarmonyPatch(typeof(Assets), nameof(Assets.AddBuildingDef))]
    internal static class BuildingSupply_ConfigurePolicy_Patch
    {
        private static void Postfix(BuildingDef def) => ArmOnlyFarmSupply.Configure(def);
    }

    // ConfigurePost 可能补建储存组件；全部配置完成后再补一次，保持一次性预制体接入。
    [HarmonyPatch(typeof(BuildingConfigManager), nameof(BuildingConfigManager.ConfigurePost))]
    internal static class BuildingSupply_FinalizePolicy_Patch
    {
        private static void Postfix()
        {
            if (Assets.BuildingDefs == null) return;
            foreach (BuildingDef definition in Assets.BuildingDefs) ArmOnlyFarmSupply.Configure(definition);
        }
    }

    [HarmonyPatch]
    internal static class FetchChore_FarmSupplyWorker_Patch
    {
        private static readonly Chore.Precondition ArmSupplyOnly = new Chore.Precondition
        {
            id = "OmniMod_ArmOnlyFarmSupply",
            description = ModStrings.T("仅机械臂供货", "Auto-Sweeper Deliveries Only").Resolve(),
            sortOrder = -100,
            canExecuteOnAnyThread = true,
            fn = (ref Chore.Precondition.Context context, object data) =>
                data is ArmOnlyFarmSupply policy && (!policy.OnlyArms ||
                    (context.consumerState != null && context.consumerState.hasSolidTransferArm))
        };

        private static MethodBase TargetMethod() => AccessTools.Constructor(typeof(FetchChore), new[]
        {
            typeof(ChoreType), typeof(Storage), typeof(float), typeof(HashSet<Tag>),
            typeof(FetchChore.MatchCriteria), typeof(Tag), typeof(Tag[]), typeof(ChoreProvider),
            typeof(bool), typeof(System.Action<Chore>), typeof(System.Action<Chore>),
            typeof(System.Action<Chore>), typeof(Operational.State), typeof(int)
        });

        private static void Postfix(FetchChore __instance, Storage destination)
        {
            if (!ArmOnlyFarmSupply.IsSupply(__instance)) return;
            ArmOnlyFarmSupply policy = ArmOnlyFarmSupply.Resolve(destination);
            if (policy != null) __instance.AddPrecondition(ArmSupplyOnly, policy);
        }
    }

    // 旧存档中的在途订单、切换设置和批量配送都在最终投放前再核对一次。
    [HarmonyPatch(typeof(FetchAreaChore.StatesInstance), nameof(FetchAreaChore.StatesInstance.DeliverComplete))]
    internal static class BuildingSupply_DeliveryPolicy_Patch
    {
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(FetchAreaChore.StatesInstance __instance)
        {
            if (__instance.deliveries.Count == 0) return true;
            FetchChore request = __instance.deliveries[0].chore;
            if (!ArmOnlyFarmSupply.IsSupply(request)) return true;
            ArmOnlyFarmSupply policy = ArmOnlyFarmSupply.Resolve(request.destination);
            if (policy == null || !policy.OnlyArms) return true;
            ChoreConsumer worker = __instance.sm.fetcher.Get<ChoreConsumer>(__instance);
            if (worker != null && worker.consumerState != null && worker.consumerState.hasSolidTransferArm) return true;
            __instance.DeliverFail();
            return false;
        }
    }
}
