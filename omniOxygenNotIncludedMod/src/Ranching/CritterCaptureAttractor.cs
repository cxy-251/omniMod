using System;
using System.Collections.Generic;
using HarmonyLib;
using OmniMod.Core.Logging;
using OmniMod.Core.Safety;
using OmniMod.Ranching.Registries;
using UnityEngine;

namespace OmniMod.Ranching
{
    /// <summary>
    /// 辅助实现小动物自动打包、释放与防卡死逻辑
    /// </summary>
    internal static class AutoWrangleHelper
    {
        internal static void AutoBagCreature(GameObject go, FixedCapturePoint.Instance capturePoint)
        {
            if (go == null) return;

            ExceptionShield.Execute("AutoWrangleHelper.AutoBagCreature", () =>
            {
                int captureCell = Grid.PosToCell(capturePoint != null ? capturePoint.transform.GetPosition() : go.transform.position);
                if (capturePoint != null && capturePoint.def != null && capturePoint.def.postCaptureOffset.HasValue)
                {
                    captureCell = Grid.OffsetCell(captureCell, capturePoint.def.postCaptureOffset.Value);
                }
                if (!Grid.IsValidCell(captureCell))
                {
                    captureCell = Grid.PosToCell(go);
                }

                // 1. 先移动实体至取出点目标格
                if (Grid.IsValidCell(captureCell))
                {
                    go.transform.SetPosition(Grid.CellToPosCCC(captureCell, Grid.SceneLayer.Creatures));
                }

                // 2. 停止小动物当前的导航/脑部任务（退出 FixedCaptureStates）
                var driver = go.GetComponent<ChoreDriver>();
                if (driver != null && driver.HasChore())
                {
                    driver.StopChore();
                }

                // 3. 取消常规捕捉标记
                var capturable = go.GetComponent<Capturable>();
                if (capturable != null)
                {
                    capturable.MarkForCapture(false);
                }

                // 4. 打包为袋装小动物并打上可搬运标签
                var baggable = go.GetComponent<Baggable>();
                if (baggable != null)
                {
                    baggable.SetWrangled();
                    go.AddTag(GameTags.Creatures.Deliverable);
                }

                // 5. 关键：更新 Pickupable 的 cachedCell 与空间分区系统，确保机械手与引擎精准感知其新位置！
                var pickupable = go.GetComponent<Pickupable>();
                if (pickupable != null && Grid.IsValidCell(captureCell))
                {
                    pickupable.UpdateCachedCell(captureCell);
                }

                // 5.5 确保 ReachabilityMonitor 立即将袋装小动物设为 Reachable，供机械臂与取物系统实时识别
                var reachability = go.GetSMI<ReachabilityMonitor.Instance>();
                if (reachability != null)
                {
                    reachability.sm.isReachable.Set(true, reachability);
                }

                // 6. 注册到袋装小动物注册表
                if (baggable != null)
                {
                    BaggedCreatureRegistry.Register(baggable);
                }

                // 7. 重置取出点，释放占用并立刻准备吸引下一只过剩动物
                if (capturePoint != null && capturePoint.IsRunning())
                {
                    capturePoint.isCurrentlyCapturingCreature = false;
                    capturePoint.ResetCapturePoint();
                }

                ModLog.DebugLog($"小动物取出点：成功自动打包 {go.name} 于格子 {captureCell}");
            });
        }
    }

    /// <summary>
    /// 放宽取出点对小动物可达性的判定：原版由于 captureCell 往往被建筑本体占用导致 GetNavigationCost 返回 -1，
    /// 并且 PathGrid 未对远距离小动物及时探查，此处只要同房间或同星球直线距离内且数量超标即视为可捕捉。
    /// </summary>
    [HarmonyPatch(typeof(FixedCapturePoint.Instance), "CanCapturableBeCapturedAtCapturePoint")]
    internal static class FixedCapturePoint_CanCapturableBeCapturedAtCapturePoint_Patch
    {
        private static void Postfix(FixedCapturableMonitor.Instance capturable, FixedCapturePoint.Instance capture_point, CavityInfo capture_cavity_info, int capture_cell, ref bool __result)
        {
            if (__result) return;
            try
            {
                if (capturable == null || !capturable.IsRunning() || capture_point == null) return;
                if (capturable.targetCapturePoint != capture_point && !capturable.targetCapturePoint.IsNullOrStopped()) return;
                if (capturable.HasTag(GameTags.Creatures.Bagged)) return;
                if (capturable.isBaby && !capture_point.def.allowBabies) return;

                int cell = Grid.PosToCell(capturable.transform.GetPosition());
                int pointCell = Grid.PosToCell(capture_point);

                if (Grid.WorldIdx == null || (uint)cell >= (uint)Grid.WorldIdx.Length || (uint)pointCell >= (uint)Grid.WorldIdx.Length) return;
                if (Grid.WorldIdx[cell] != Grid.WorldIdx[pointCell]) return;

                CavityInfo cavity = Game.Instance.roomProber?.GetCavityForCell(cell);
                bool sameRoom = (cavity != null && cavity == capture_cavity_info);
                bool nearby = (capture_cavity_info == null && Grid.GetCellDistance(cell, pointCell) <= 30);
                if (!sameRoom && !nearby) return;

                if (capture_point.def == null || capture_point.def.isAmountStoredOverCapacity == null) return;
                if (!capture_point.def.isAmountStoredOverCapacity(capture_point, capturable)) return;

                __result = true;
            }
            catch (Exception ex)
            {
                ModLog.Error("FixedCapturePoint_CanCapturableBeCapturedAtCapturePoint_Patch failed", ex);
            }
        }
    }

    /// <summary>
    /// 取出点寻找过剩小动物并吸引其走近：
    /// 当选中小动物后，保持 shouldCreatureGoGetCaptured 为 true，唤醒小动物大脑，
    /// 并在走近（<= 2 格）或寻路超时（> 12 秒）时瞬间自动打包。
    /// </summary>
    [HarmonyPatch(typeof(FixedCapturePoint.Instance), nameof(FixedCapturePoint.Instance.FindFixedCapturable))]
    internal static class FixedCapturePoint_FindFixedCapturable_Patch
    {
        private static readonly AccessTools.FieldRef<FixedCapturePoint.Instance, int> CaptureCellRef =
            AccessTools.FieldRefAccess<FixedCapturePoint.Instance, int>("captureCell");

        private static readonly AccessTools.FieldRef<FixedCapturePoint.Instance, FixedCapturableMonitor.Instance> TargetCapturableRef =
            AccessTools.FieldRefAccess<FixedCapturePoint.Instance, FixedCapturableMonitor.Instance>("<targetCapturable>k__BackingField");

        private static readonly Dictionary<int, float> _captureStartTimes = new Dictionary<int, float>();

        private static void Prefix(FixedCapturePoint.Instance __instance)
        {
            if (__instance == null || !__instance.IsOperational) return;
            // 如果已有目标正在行进中，保持正在捕捉标记，防止原版 FindFixedCapturable 误调用 ResetCapturePoint() 清理目标
            if (__instance.targetCapturable != null && !__instance.targetCapturable.IsNullOrStopped())
            {
                __instance.isCurrentlyCapturingCreature = true;
            }
        }

        private static void Postfix(FixedCapturePoint.Instance __instance)
        {
            if (__instance == null || !__instance.IsOperational) return;

            ExceptionShield.Execute("FixedCapturePoint.FindFixedCapturable", () =>
            {
                int stationId = __instance.gameObject != null ? __instance.gameObject.GetInstanceID() : 0;
                var target = __instance.targetCapturable;

                // 兜底补救：如果原版由于未探测到腔体等原因未能匹配目标，自行检索过剩动物
                if (target == null || target.IsNullOrStopped() || target.gameObject == null)
                {
                    if (__instance.critterCapactiy is IUserControlledCapacity capacity && capacity.AmountStored > capacity.UserMaxCapacity)
                    {
                        TreeFilterable filter = __instance.GetComponent<TreeFilterable>();
                        int stationCell = Grid.PosToCell(__instance);
                        int worldId = __instance.GetMyWorldId();

                        FixedCapturableMonitor.Instance bestCandidate = null;
                        int bestDist = int.MaxValue;

                        foreach (FixedCapturableMonitor.Instance monitor in Components.FixedCapturableMonitors)
                        {
                            if (monitor == null || !monitor.IsRunning() || monitor.gameObject == null) continue;
                            if (monitor.HasTag(GameTags.Creatures.Bagged)) continue;
                            if (!monitor.targetCapturePoint.IsNullOrStopped()) continue;
                            if (monitor.isBaby && !__instance.def.allowBabies) continue;
                            if (filter != null && !filter.ContainsTag(monitor.PrefabTag)) continue;

                            int cCell = Grid.PosToCell(monitor.gameObject);
                            if (!Grid.IsValidCell(cCell) || Grid.WorldIdx[cCell] != worldId) continue;

                            int dist = Grid.GetCellDistance(cCell, stationCell);
                            if (dist < bestDist)
                            {
                                bestDist = dist;
                                bestCandidate = monitor;
                            }
                        }

                        if (bestCandidate != null)
                        {
                            if (TargetCapturableRef != null)
                            {
                                TargetCapturableRef(__instance) = bestCandidate;
                            }
                            bestCandidate.targetCapturePoint = __instance;
                            target = bestCandidate;
                        }
                    }
                }

                if (target != null && !target.IsNullOrStopped() && target.gameObject != null)
                {
                    // 1. 常驻牧场主就位标记，确保 FixedCapturableMonitor.ShouldGoGetCaptured() 持续返回 true
                    __instance.SetRancherIsAvailableForCapturing();
                    __instance.isCurrentlyCapturingCreature = true;

                    // 2. 唤醒小动物大脑，立即触发前往行为
                    CreatureBrain brain = target.GetComponent<CreatureBrain>();
                    if (brain != null)
                    {
                        Game.BrainScheduler?.PrioritizeBrain(brain);
                    }
                    target.Trigger((int)GameHashes.RancherReadyAtCapturePoint);
                    target.GetComponent<ChoreConsumer>()?.Trigger((int)GameHashes.ChoreInterrupt);

                    // 3. 记录或检查行走耗时
                    if (stationId != 0)
                    {
                        if (!_captureStartTimes.TryGetValue(stationId, out float startTime))
                        {
                            startTime = Time.time;
                            _captureStartTimes[stationId] = startTime;
                        }

                        int creatureCell = Grid.PosToCell(target.transform.GetPosition());
                        int captureCell = CaptureCellRef != null ? CaptureCellRef(__instance) : Grid.PosToCell(__instance);

                        if (Grid.IsValidCell(creatureCell) && Grid.IsValidCell(captureCell))
                        {
                            int dist = Grid.GetCellDistance(creatureCell, captureCell);
                            // 距离 2 格以内自动打包；若行走超过 12 秒则兜底自动打包防卡死
                            if (dist <= 2 || (Time.time - startTime > 12f))
                            {
                                _captureStartTimes.Remove(stationId);
                                AutoWrangleHelper.AutoBagCreature(target.gameObject, __instance);
                            }
                        }
                    }
                }
                else
                {
                    if (stationId != 0)
                    {
                        _captureStartTimes.Remove(stationId);
                    }
                    __instance.isCurrentlyCapturingCreature = false;
                }
            });
        }
    }

    /// <summary>
    /// 确保小动物大脑在被取出点选中时，立即判定为应当去被打包（无需等待复制人到达）
    /// </summary>
    [HarmonyPatch(typeof(FixedCapturableMonitor.Instance), nameof(FixedCapturableMonitor.Instance.ShouldGoGetCaptured))]
    internal static class FixedCapturableMonitor_ShouldGoGetCaptured_Patch
    {
        private static void Postfix(FixedCapturableMonitor.Instance __instance, ref bool __result)
        {
            if (__result || __instance == null) return;
            if (__instance.targetCapturePoint != null && __instance.targetCapturePoint.IsRunning())
            {
                if (!__instance.isBaby || __instance.targetCapturePoint.def.allowBabies)
                {
                    __result = true;
                }
            }
        }
    }

    /// <summary>
    /// 小动物走到取出点就位时（进入 waitforranchertobeready 状态），瞬间触发免人工自动打包
    /// </summary>
    [HarmonyPatch(typeof(FixedCaptureStates), nameof(FixedCaptureStates.InitializeStates))]
    internal static class FixedCaptureStates_InitializeStates_Patch
    {
        private static readonly AccessTools.FieldRef<FixedCaptureStates, FixedCaptureStates.CaptureStates> CaptureFieldRef =
            AccessTools.FieldRefAccess<FixedCaptureStates, FixedCaptureStates.CaptureStates>("capture");

        private static void Postfix(FixedCaptureStates __instance)
        {
            var capture = CaptureFieldRef != null ? CaptureFieldRef(__instance) : null;
            if (capture?.move?.waitforranchertobeready != null)
            {
                capture.move.waitforranchertobeready.Enter("OmniMod_AutoBagOnArrival", (FixedCaptureStates.Instance smi) =>
                {
                    if (smi != null && smi.gameObject != null)
                    {
                        var capturePoint = smi.GetCapturePoint();
                        AutoWrangleHelper.AutoBagCreature(smi.gameObject, capturePoint);
                    }
                });
            }
        }
    }

    /// <summary>
    /// 取出点启动时注册小动物到达事件监听，实现双重保障
    /// </summary>
    [HarmonyPatch(typeof(FixedCapturePoint.Instance), nameof(FixedCapturePoint.Instance.StartSM))]
    internal static class FixedCapturePoint_StartSM_Patch
    {
        private static void Postfix(FixedCapturePoint.Instance __instance)
        {
            if (__instance == null || __instance.gameObject == null) return;
            __instance.Subscribe((int)GameHashes.CreatureArrivedAtCapturePoint, (data) =>
            {
                ExceptionShield.Execute("FixedCapturePoint.CreatureArrived", () =>
                {
                    if (__instance != null && __instance.targetCapturable != null && !__instance.targetCapturable.IsNullOrStopped() && __instance.targetCapturable.gameObject != null)
                    {
                        AutoWrangleHelper.AutoBagCreature(__instance.targetCapturable.gameObject, __instance);
                    }
                });
            });
        }
    }

    /// <summary>
    /// 屏蔽复制人前往取出点抓捕小动物的任务，实现全自动免人工
    /// </summary>
    [HarmonyPatch(typeof(FixedCapturePoint.Instance), nameof(FixedCapturePoint.Instance.CreateChore))]
    internal static class FixedCapturePoint_CreateChore_Patch
    {
        private static void Postfix(ref Chore __result)
        {
            if (__result != null)
            {
                __result.AddPrecondition(new Chore.Precondition
                {
                    id = "OmniMod_PreventDuplicantCapture",
                    description = "OmniMod Automated Capture",
                    fn = (ref Chore.Precondition.Context context, object data) => false
                });
                __result.showAvailabilityInHoverText = false;
            }
        }
    }

    [HarmonyPatch(typeof(Game), "OnDestroy")]
    internal static class AutoRanch_Game_OnDestroy_Patch
    {
        private static void Postfix()
        {
            BaggedCreatureRegistry.Clear();
            CreatureDeliveryRegistry.Clear();
        }
    }
}
