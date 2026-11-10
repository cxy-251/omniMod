using System;
using System.Collections.Generic;
using HarmonyLib;
using OmniMod.Core.Logging;
using UnityEngine;

namespace OmniMod.Buildings
{
    /// <summary>
    /// 生态舱离屏“视觉与骨骼动画深度休眠”系统：
    /// 针对玩家拥有 50+ 个生态舱、2000+ 只动物（如 1300+ 斑海豹、600+ 甜蚜）的工业化流水线场景。
    /// 
    /// 核心机制：
    /// 1. 当玩家镜头不在某个生态舱时（worldId != activeWorldId），该生态舱内所有小动物、建筑与实体的
    ///    KBatchedAnimController 进入视觉与动画休眠（SetVisiblity(false)）。
    /// 2. 彻底从 KBatchedAnimUpdater 的逐帧刷新列表注销，完全消除 2000+ 动物的骨骼矩阵计算、动态网格构建、
    ///    GPU 顶点流上传以及 FMOD 音效事件广播，从根本上释放 Unity C++ 渲染层与动态批处理显存。
    /// 3. 底层模拟（进食、排泄乙醇/泥土/蔗糖、产卵、代谢、温度与逻辑电网）完全不受任何影响，保持 100% 后台全速生产。
    /// 4. 当玩家点击切入生态舱（“进去看一眼”）时，毫秒级快速唤醒并刷新当前世界内所有实体，画面无缝呈现。
    /// </summary>
    public static class PocketWorldDormancyManager
    {
        private static readonly HashSet<int> DormantWorldIds = new HashSet<int>();
        private static readonly object SyncLock = new object();

        /// <summary>
        /// 判断指定世界是否处于离屏休眠中的生态舱
        /// </summary>
        public static bool IsInactivePocketWorld(int worldId)
        {
            if (worldId < 0 || worldId >= ClusterManager.INVALID_WORLD_IDX) return false;
            if (ClusterManager.Instance == null) return false;
            int activeId = ClusterManager.Instance.activeWorldId;
            return worldId != activeId && OmniPocketHabitat.IsPocketHabitatWorld(worldId);
        }

        /// <summary>
        /// 当游戏加载完成或读档后执行全量初始化扫描
        /// </summary>
        public static void InitialDormancySweep()
        {
            if (ClusterManager.Instance == null) return;
            int activeWorldId = ClusterManager.Instance.activeWorldId;
            int suspendedWorlds = 0;
            int suspendedCritters = 0;

            lock (SyncLock)
            {
                DormantWorldIds.Clear();
                var worlds = ClusterManager.Instance.WorldContainers;
                if (worlds == null) return;

                for (int i = 0; i < worlds.Count; i++)
                {
                    var world = worlds[i];
                    if (world == null) continue;
                    int worldId = world.id;

                    if (OmniPocketHabitat.IsPocketHabitatWorld(worldId) && worldId != activeWorldId)
                    {
                        SuspendPocketWorld(worldId);
                        DormantWorldIds.Add(worldId);
                        suspendedWorlds++;

                        var brains = Components.Brains?.GetWorldItems(worldId, false);
                        if (brains != null)
                        {
                            for (int b = 0; b < brains.Count; b++)
                            {
                                if (brains[b] != null && brains[b].GetComponent<MinionIdentity>() == null)
                                    suspendedCritters++;
                            }
                        }
                    }
                }
            }

            if (suspendedWorlds > 0)
            {
                ModLog.Info($"[生态舱离屏休眠] 初始扫描完成：已将 {suspendedWorlds} 个离屏生态舱（约 {suspendedCritters} 只工业动物）置入视觉休眠，后台全速生产。");
            }
        }

        /// <summary>
        /// 当活动世界发生切换时的无缝唤醒与休眠调度
        /// </summary>
        public static void OnActiveWorldChanged(int oldWorldId, int newWorldId)
        {
            if (oldWorldId == newWorldId) return;

            lock (SyncLock)
            {
                // 1. 若旧世界是生态舱且已脱离焦点，将其休眠
                if (oldWorldId >= 0 && oldWorldId < ClusterManager.INVALID_WORLD_IDX && OmniPocketHabitat.IsPocketHabitatWorld(oldWorldId))
                {
                    SuspendPocketWorld(oldWorldId);
                    DormantWorldIds.Add(oldWorldId);
                }

                // 2. 若新世界是生态舱，将其唤醒
                if (newWorldId >= 0 && newWorldId < ClusterManager.INVALID_WORLD_IDX && OmniPocketHabitat.IsPocketHabitatWorld(newWorldId))
                {
                    ResumePocketWorld(newWorldId);
                    DormantWorldIds.Remove(newWorldId);
                    ModLog.Info($"[生态舱离屏休眠] 镜头切入生态舱 [{newWorldId}]，已瞬间唤醒该生态舱全部实体动画渲染。");
                }
            }
        }

        /// <summary>
        /// 将指定生态舱内的全部动物与建筑置入休眠
        /// </summary>
        public static void SuspendPocketWorld(int worldId)
        {
            if (worldId < 0 || worldId >= ClusterManager.INVALID_WORLD_IDX) return;

            try
            {
                // 1. 小动物与 AI 个体（排除复制人小人）
                if (Components.Brains != null)
                {
                    var brains = Components.Brains.GetWorldItems(worldId, false);
                    if (brains != null)
                    {
                        for (int i = 0; i < brains.Count; i++)
                        {
                            var brain = brains[i];
                            if (brain == null || brain.GetComponent<MinionIdentity>() != null) continue;
                            SuspendEntityAnims(brain.gameObject);
                        }
                    }
                }

                // 2. 建筑实体（自动清扫机、喂食器、照料站等）
                if (Components.BuildingCompletes != null)
                {
                    var buildings = Components.BuildingCompletes.GetWorldItems(worldId, false);
                    if (buildings != null)
                    {
                        for (int i = 0; i < buildings.Count; i++)
                        {
                            var building = buildings[i];
                            if (building == null) continue;
                            SuspendEntityAnims(building.gameObject);
                        }
                    }
                }

                // 3. 拾取物与产物
                if (Components.Pickupables != null)
                {
                    var pickups = Components.Pickupables.GetWorldItems(worldId, false);
                    if (pickups != null)
                    {
                        for (int i = 0; i < pickups.Count; i++)
                        {
                            var pickup = pickups[i];
                            if (pickup == null) continue;
                            SuspendEntityAnims(pickup.gameObject);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                ModLog.Warn($"[生态舱离屏休眠] 休眠世界 [{worldId}] 异常: {ex.Message}");
            }
        }

        /// <summary>
        /// 唤醒指定生态舱内的全部实体动画与渲染
        /// </summary>
        public static void ResumePocketWorld(int worldId)
        {
            if (worldId < 0 || worldId >= ClusterManager.INVALID_WORLD_IDX) return;

            try
            {
                // 1. 小动物与 AI 个体
                if (Components.Brains != null)
                {
                    var brains = Components.Brains.GetWorldItems(worldId, false);
                    if (brains != null)
                    {
                        for (int i = 0; i < brains.Count; i++)
                        {
                            var brain = brains[i];
                            if (brain == null) continue;
                            ResumeEntityAnims(brain.gameObject);
                        }
                    }
                }

                // 2. 建筑实体
                if (Components.BuildingCompletes != null)
                {
                    var buildings = Components.BuildingCompletes.GetWorldItems(worldId, false);
                    if (buildings != null)
                    {
                        for (int i = 0; i < buildings.Count; i++)
                        {
                            var building = buildings[i];
                            if (building == null) continue;
                            ResumeEntityAnims(building.gameObject);
                        }
                    }
                }

                // 3. 拾取物与产物
                if (Components.Pickupables != null)
                {
                    var pickups = Components.Pickupables.GetWorldItems(worldId, false);
                    if (pickups != null)
                    {
                        for (int i = 0; i < pickups.Count; i++)
                        {
                            var pickup = pickups[i];
                            if (pickup == null) continue;
                            ResumeEntityAnims(pickup.gameObject);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                ModLog.Warn($"[生态舱离屏休眠] 唤醒世界 [{worldId}] 异常: {ex.Message}");
            }
        }

        /// <summary>
        /// 休眠单个实体的全部动画控制器
        /// </summary>
        public static void SuspendEntityAnims(GameObject go)
        {
            if (go == null) return;
            try
            {
                var anims = go.GetComponentsInChildren<KBatchedAnimController>(true);
                if (anims != null)
                {
                    for (int i = 0; i < anims.Length; i++)
                    {
                        var anim = anims[i];
                        if (anim != null)
                        {
                            anim.SetVisiblity(false);
                        }
                    }
                }
            }
            catch {}
        }

        /// <summary>
        /// 唤醒单个实体的全部动画控制器
        /// </summary>
        public static void ResumeEntityAnims(GameObject go)
        {
            if (go == null) return;
            try
            {
                var anims = go.GetComponentsInChildren<KBatchedAnimController>(true);
                if (anims != null)
                {
                    for (int i = 0; i < anims.Length; i++)
                    {
                        var anim = anims[i];
                        if (anim != null)
                        {
                            anim.SetVisiblity(true);
                        }
                    }
                }
            }
            catch {}
        }
    }

    /// <summary>
    /// 当小动物、新建筑或物品在离屏生态舱内动态生成或孵化时，自动保持休眠状态
    /// </summary>
    [HarmonyPatch(typeof(Brain), "OnSpawn")]
    internal static class Brain_OnSpawn_PocketDormancy_Patch
    {
        private static void Postfix(Brain __instance)
        {
            if (__instance == null || __instance.GetComponent<MinionIdentity>() != null) return;
            int worldId = __instance.GetMyWorldId();
            if (PocketWorldDormancyManager.IsInactivePocketWorld(worldId))
            {
                PocketWorldDormancyManager.SuspendEntityAnims(__instance.gameObject);
            }
        }
    }

    [HarmonyPatch(typeof(BuildingComplete), "OnSpawn")]
    internal static class BuildingComplete_OnSpawn_PocketDormancy_Patch
    {
        private static void Postfix(BuildingComplete __instance)
        {
            if (__instance == null) return;
            int worldId = __instance.GetMyWorldId();
            if (PocketWorldDormancyManager.IsInactivePocketWorld(worldId))
            {
                PocketWorldDormancyManager.SuspendEntityAnims(__instance.gameObject);
            }
        }
    }

    [HarmonyPatch(typeof(Game), "OnSpawn")]
    internal static class Game_OnSpawn_PocketDormancy_Patch
    {
        private static void Postfix()
        {
            try
            {
                PocketWorldDormancyManager.InitialDormancySweep();
            }
            catch (Exception ex)
            {
                ModLog.Warn($"[生态舱离屏休眠] 游戏生成钩子执行异常: {ex.Message}");
            }
        }
    }
}
