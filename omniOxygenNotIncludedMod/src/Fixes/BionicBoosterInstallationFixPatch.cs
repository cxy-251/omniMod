using System;
using HarmonyLib;
using OmniMod.Core.Logging;
using OmniMod.Core.Safety;
using UnityEngine;

namespace OmniMod.Fixes
{
    /// <summary>
    /// 修复仿生人增幅块安装与槽位替换的核心缺陷：
    /// 1. 原版当玩家在已有增幅块的槽位上分配新增幅块时，未先卸下旧增幅块，导致 GetSlotForAssignedUpgrade 返回 null，
    ///    进而导致 InstallUpgrade 将新增幅块丢弃在地面，小人进入“反复取用增幅块”死循环。
    /// 2. 在执行取用与安装时，若槽位仍占用旧增幅块，自动安全卸下旧增幅块并释放至地面供聚合箱回收，确保新增幅块顺利装入。
    /// 3. 当玩家在界面将槽位取消分配（设为无）时，自动触发卸下旧增幅块。
    /// </summary>
    public static class BionicBoosterInstallationFixPatch
    {
        /// <summary>
        /// 拦截 SeekAndInstallBionicUpgradeChore.InstallUpgrade：
        /// 在将新增幅块从小人 DefaultStorage 移除之前，若目标槽位仍装有旧增幅块，先安全卸下旧增幅块，确保安装成功，杜绝增幅块掉落死循环。
        /// </summary>
        [HarmonyPatch(typeof(SeekAndInstallBionicUpgradeChore), nameof(SeekAndInstallBionicUpgradeChore.InstallUpgrade))]
        internal static class SeekAndInstallBionicUpgradeChore_InstallUpgrade_Patch
        {
            private static void Prefix(SeekAndInstallBionicUpgradeChore.Instance smi)
            {
                ExceptionShield.Execute("SeekAndInstallBionicUpgradeChore.InstallUpgrade.Prefix", () =>
                {
                    if (smi == null || smi.upgradeMonitor == null) return;
                    Storage defaultStorage = smi.gameObject.GetComponents<Storage>().FindFirst((Storage s) => s.storageID == GameTags.StoragesIds.DefaultStorage);
                    if (defaultStorage == null) return;
                    GameObject upgradeGo = defaultStorage.FindFirst(GameTags.BionicUpgrade);
                    if (upgradeGo == null) return;
                    BionicUpgradeComponent newUpgrade = upgradeGo.GetComponent<BionicUpgradeComponent>();
                    if (newUpgrade == null) return;

                    var slots = smi.upgradeMonitor.upgradeComponentSlots;
                    if (slots == null) return;
                    for (int i = 0; i < slots.Length; i++)
                    {
                        var slot = slots[i];
                        if (slot != null && slot.HasUpgradeComponentAssigned && slot.assignedUpgradeComponent == newUpgrade)
                        {
                            if (slot.HasUpgradeInstalled && !slot.AssignedUpgradeMatchesInstalledUpgrade)
                            {
                                smi.upgradeMonitor.UninstallUpgrade(slot);
                            }
                            break;
                        }
                    }
                });
            }
        }

        /// <summary>
        /// 增强 BionicUpgradesMonitor.Instance.GetSlotForAssignedUpgrade：
        /// 若原版因 HasUpgradeInstalled 为 true 返回 null，在此识别已分配该增幅块的槽位并安全卸下旧增幅块，返回目标槽位。
        /// </summary>
        [HarmonyPatch(typeof(BionicUpgradesMonitor.Instance), "GetSlotForAssignedUpgrade")]
        internal static class BionicUpgradesMonitor_GetSlotForAssignedUpgrade_Patch
        {
            private static void Postfix(BionicUpgradesMonitor.Instance __instance, BionicUpgradeComponent upgradeComponent, ref BionicUpgradesMonitor.UpgradeComponentSlot __result)
            {
                if (__result != null || __instance == null || upgradeComponent == null || __instance.upgradeComponentSlots == null)
                {
                    return;
                }

                try
                {
                    for (int i = 0; i < __instance.upgradeComponentSlots.Length; i++)
                    {
                        var slot = __instance.upgradeComponentSlots[i];
                        if (slot != null && slot.HasUpgradeComponentAssigned && slot.assignedUpgradeComponent == upgradeComponent)
                        {
                            if (slot.HasUpgradeInstalled)
                            {
                                __instance.UninstallUpgrade(slot);
                            }
                            __result = slot;
                            break;
                        }
                    }
                }
                catch (Exception ex)
                {
                    ModLog.Error("BionicUpgradesMonitor.GetSlotForAssignedUpgrade", ex);
                }
            }
        }

        /// <summary>
        /// 增强 BionicUpgradesMonitor.Instance.GetAnyReachableAssignedSlot：
        /// 原版检查 !slot.HasUpgradeInstalled，导致替换已有增幅块的槽位无法开始取用任务；
        /// 允许已分配新增幅块但仍占用旧增幅块的槽位正常开启任务。
        /// </summary>
        [HarmonyPatch(typeof(BionicUpgradesMonitor.Instance), nameof(BionicUpgradesMonitor.Instance.GetAnyReachableAssignedSlot))]
        internal static class BionicUpgradesMonitor_GetAnyReachableAssignedSlot_Patch
        {
            private static void Postfix(BionicUpgradesMonitor.Instance __instance, ref BionicUpgradesMonitor.UpgradeComponentSlot __result)
            {
                if (__result != null || __instance == null || __instance.upgradeComponentSlots == null)
                {
                    return;
                }

                try
                {
                    for (int i = 0; i < __instance.upgradeComponentSlots.Length; i++)
                    {
                        var slot = __instance.upgradeComponentSlots[i];
                        if (slot != null && slot.HasUpgradeComponentAssigned && !slot.IsLocked &&
                            __instance.IsBionicUpgradeComponentObjectAbleToBePickedUp(slot.assignedUpgradeComponent))
                        {
                            __result = slot;
                            break;
                        }
                    }
                }
                catch (Exception ex)
                {
                    ModLog.Error("BionicUpgradesMonitor.GetAnyReachableAssignedSlot", ex);
                }
            }
        }

        /// <summary>
        /// 监听槽位分配变更：若玩家将已安装的槽位设为“无”（取消分配），立即安全卸下旧增幅块。
        /// </summary>
        [HarmonyPatch(typeof(BionicUpgradesMonitor.Instance), "OnSlotAssignationChanged")]
        internal static class BionicUpgradesMonitor_OnSlotAssignationChanged_Patch
        {
            private static void Postfix(BionicUpgradesMonitor.Instance __instance, BionicUpgradesMonitor.UpgradeComponentSlot slot)
            {
                ExceptionShield.Execute("BionicUpgradesMonitor.OnSlotAssignationChanged", () =>
                {
                    if (__instance == null || slot == null) return;
                    if (slot.HasUpgradeInstalled && !slot.HasUpgradeComponentAssigned)
                    {
                        __instance.UninstallUpgrade(slot);
                    }
                });
            }
        }
    }
}
