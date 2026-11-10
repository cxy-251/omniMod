using System;
using HarmonyLib;
using Klei.AI;
using OmniMod.Core.Logging;
using UnityEngine;

namespace OmniMod.Farming
{
    /// <summary>
    /// 全自动农业站（Auto Farm Station）：
    /// 当农业站位于房间中且处于启用状态时，自动对所在房间内的所有农作物提供“农民的呵护”（FarmTinker），
    /// 生长速度翻倍（+100%），免去复制人搓微肥包和奔波照料，实现真正的无人化自动化农业。
    /// </summary>
    public class AutoFarmStation : KMonoBehaviour, ISim1000ms
    {
        internal static readonly AccessTools.FieldRef<TinkerStation, Chore> TinkerStationChoreRef =
            AccessTools.FieldRefAccess<TinkerStation, Chore>("chore");

        internal static readonly AccessTools.FieldRef<Tinkerable, Chore> TinkerableChoreRef =
            AccessTools.FieldRefAccess<Tinkerable, Chore>("chore");

        internal static readonly Action<Tinkerable> UpdateVisualDelegate =
            AccessTools.MethodDelegate<Action<Tinkerable>>(AccessTools.Method(typeof(Tinkerable), "UpdateVisual"));

        internal static readonly Func<Tinkerable, bool> HasCorrectRoomDelegate =
            AccessTools.MethodDelegate<Func<Tinkerable, bool>>(AccessTools.Method(typeof(Tinkerable), "HasCorrectRoom"));

        internal static readonly Func<Tinkerable, bool> RoomHasTinkerstationDelegate =
            AccessTools.MethodDelegate<Func<Tinkerable, bool>>(AccessTools.Method(typeof(Tinkerable), "RoomHasTinkerstation"));

        private Operational _operational;
        private TinkerStation _tinkerStation;
        private ManualDeliveryKG _manualDelivery;

        protected override void OnPrefabInit()
        {
            base.OnPrefabInit();
            _operational = GetComponent<Operational>();
            _tinkerStation = GetComponent<TinkerStation>();
            _manualDelivery = GetComponent<ManualDeliveryKG>();
        }

        protected override void OnSpawn()
        {
            base.OnSpawn();
            SuppressManualChores();
            TendAllPlantsInRoom();
        }

        public void Sim1000ms(float dt)
        {
            if (_operational != null && !_operational.IsOperational) return;
            SuppressManualChores();
            TendAllPlantsInRoom();
        }

        private void SuppressManualChores()
        {
            // 屏蔽向农业站运送肥料的手工任务
            if (_manualDelivery != null && !_manualDelivery.IsPaused)
            {
                _manualDelivery.Pause(true, "AutoFarmStation");
                _manualDelivery.AbortDelivery("AutoFarmStation active");
            }

            // 屏蔽在农业站制作微肥包的手工任务
            if (_tinkerStation != null)
            {
                Chore chore = TinkerStationChoreRef(_tinkerStation);
                if (chore != null)
                {
                    chore.Cancel("AutoFarmStation active");
                    TinkerStationChoreRef(_tinkerStation) = null;
                }
            }
        }

        public void TendAllPlantsInRoom()
        {
            int cell = Grid.PosToCell(this);
            if (!Grid.IsValidCell(cell) || Game.Instance == null || Game.Instance.roomProber == null) return;

            CavityInfo cavity = Game.Instance.roomProber.GetCavityForCell(cell);
            if (cavity == null) return;

            // 1. 扫描本空间/房间内的直接植物实体（野生或直接长在地面）
            if (cavity.plants != null)
            {
                for (int i = 0; i < cavity.plants.Count; i++)
                {
                    KPrefabID plant = cavity.plants[i];
                    if (plant != null && plant.gameObject != null)
                    {
                        TendPlant(plant.gameObject);
                    }
                }
            }

            // 2. 扫描本空间/房间内的种植箱、农耕瓷砖、水培瓷砖中的作物
            if (cavity.buildings != null)
            {
                for (int i = 0; i < cavity.buildings.Count; i++)
                {
                    KPrefabID b = cavity.buildings[i];
                    if (b == null || b.gameObject == null) continue;
                    PlantablePlot plot = b.GetComponent<PlantablePlot>();
                    if (plot != null && plot.Occupant != null)
                    {
                        TendPlant(plot.Occupant);
                    }
                }
            }
        }

        public static void TendPlant(GameObject go)
        {
            if (go == null) return;
            Effects effects = go.GetComponent<Effects>();
            if (effects == null) return;

            Tinkerable tinkerable = go.GetComponent<Tinkerable>();
            string effectName = (tinkerable != null && !string.IsNullOrEmpty(tinkerable.addedEffect))
                ? tinkerable.addedEffect
                : "FarmTinker";

            if (!effects.HasEffect(effectName))
            {
                effects.Add(effectName, should_save: true);
                if (tinkerable != null)
                {
                    UpdateVisualDelegate?.Invoke(tinkerable);
                    Chore chore = TinkerableChoreRef(tinkerable);
                    if (chore != null)
                    {
                        chore.Cancel("Auto Farm Station Tended");
                        TinkerableChoreRef(tinkerable) = null;
                    }
                }
            }
        }
    }

    /// <summary>
    /// 为农业站挂载 AutoFarmStation 自动化组件的补丁
    /// </summary>
    [HarmonyPatch(typeof(FarmStationConfig), "DoPostConfigureComplete")]
    internal static class FarmStationConfig_AutoFarm_Patch
    {
        private static void Postfix(GameObject go)
        {
            if (go != null)
            {
                go.AddOrGet<AutoFarmStation>();
            }
        }
    }

    /// <summary>
    /// 确保读档已有农业站或动态生成的农业站均挂载 AutoFarmStation
    /// </summary>
    [HarmonyPatch(typeof(TinkerStation), "OnSpawn")]
    internal static class TinkerStation_OnSpawn_AutoFarm_Patch
    {
        private static void Postfix(TinkerStation __instance)
        {
            if (__instance != null && __instance.outputPrefab == "FarmStationTools")
            {
                __instance.gameObject.AddOrGet<AutoFarmStation>();
            }
        }
    }

    /// <summary>
    /// 拦截作物生成照料任务：若植物所在房间已有农业站，直接施加呵护效果并拦截复制人任务
    /// </summary>
    [HarmonyPatch(typeof(Tinkerable), "UpdateChore")]
    internal static class Tinkerable_UpdateChore_AutoFarm_Patch
    {
        private static bool Prefix(Tinkerable __instance)
        {
            if (__instance == null) return true;
            bool hasRoom = AutoFarmStation.HasCorrectRoomDelegate?.Invoke(__instance) ?? false;
            bool hasStation = AutoFarmStation.RoomHasTinkerstationDelegate?.Invoke(__instance) ?? false;
            if (hasRoom && hasStation)
            {
                AutoFarmStation.TendPlant(__instance.gameObject);
                return false;
            }
            return true;
        }
    }

    /// <summary>
    /// 农业站无须派发人工制作微肥包任务
    /// </summary>
    [HarmonyPatch(typeof(TinkerStation), "ToolsRequested")]
    internal static class TinkerStation_ToolsRequested_AutoFarm_Patch
    {
        private static bool Prefix(TinkerStation __instance, ref bool __result)
        {
            if (__instance != null && __instance.outputPrefab == "FarmStationTools")
            {
                __result = false;
                return false;
            }
            return true;
        }
    }
}
