using System;
using System.Collections;
using System.Collections.Generic;
using Delaunay.Geo;
using HarmonyLib;
using Klei;
using KSerialization;
using OmniMod.Core.Logging;
using ProcGen;
using STRINGS;
using UnityEngine;

namespace OmniMod.Buildings
{
    public partial class OmniPocketHabitat
    {
        /// <summary>
        /// 检查生态舱内部是否建造了对应类型的流体出口（RocketConduitReceiver 或 RocketConduitStorageAccess）。
        /// 若舱内未建造流体出口，则外部管网消费者容量与速率归零，实现 100% 管道无损路过直通。
        /// </summary>
        public bool HasInteriorFluidOutlet(ConduitType type)
        {
            if (isClosing || !PocketWorldBounds.TryGet(GetTargetWorld(), out PocketWorldBounds bounds)) return false;

            // 1. 原版火箭管线接收器（RocketConduitReceiver，例如 RocketInteriorLiquidOutputPort）
            // 直接索引内部 Items 列表，零堆内存分配，瞬间完成
            if (Components.RocketConduitReceivers != null)
            {
                for (int i = 0; i < Components.RocketConduitReceivers.Count; i++)
                {
                    RocketConduitReceiver receiver = Components.RocketConduitReceivers[i];
                    if (receiver != null && !Util.IsNullOrDestroyed(receiver) &&
                        bounds.Owns(Grid.PosToCell(receiver)) &&
                        receiver.conduitPortInfo != null && receiver.conduitPortInfo.conduitType == type)
                    {
                        return true;
                    }
                }
            }

            // 2. 舱内货仓存取接口（RocketConduitStorageAccess，例如 RocketInteriorLiquidOutput）
            // 从常驻静态集合检索，彻底告别每秒遍历全图上万建筑与高频 GC 垃圾堆积
            if (OmniPocketHabitatPatches.ActiveStorageAccesses.Count > 0)
            {
                var targetCargo = type == ConduitType.Liquid ? CargoBay.CargoType.Liquids : CargoBay.CargoType.Gasses;
                OmniPocketHabitatPatches.ActiveStorageAccesses.RemoveWhere(a => a == null || Util.IsNullOrDestroyed(a));
                foreach (RocketConduitStorageAccess access in OmniPocketHabitatPatches.ActiveStorageAccesses)
                {
                    if (bounds.Owns(Grid.PosToCell(access)) && access.cargoType == targetCargo && access.targetLevel > 0.01f)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// 动态刷新管道流体需求：
        /// 内部无出口需求时：禁用 Consumer，容量与速率置 0，零截留，外部液体/气体直接路过；
        /// 内部有出口需求时：启用 Consumer，对齐原版火箭标准（液 10kg，气 1kg），按需抽取引流。
        /// </summary>
        public void RefreshConduitDemandState()
        {
            if (LiquidConsumer != null)
            {
                bool hasLiquid = HasInteriorFluidOutlet(ConduitType.Liquid);
                if (LiquidConsumer.enabled != hasLiquid)
                {
                    LiquidConsumer.enabled = hasLiquid;
                }
                float targetRate = hasLiquid ? 10f : 0f;
                float targetCap = hasLiquid ? 10f : 0f;
                if (LiquidConsumer.consumptionRate != targetRate || LiquidConsumer.capacityKG != targetCap)
                {
                    LiquidConsumer.consumptionRate = targetRate;
                    LiquidConsumer.capacityKG = targetCap;
                }
                if (LiquidInStorage != null && LiquidInStorage.capacityKg != targetCap)
                {
                    LiquidInStorage.capacityKg = targetCap;
                }

                // 容灾回吐：如果舱内没有出水口，但 LiquidInStorage 滞留了旧版吞噬的液体，自动安全转移回吐至 LiquidOutStorage 排回管网
                if (!hasLiquid && LiquidInStorage != null && LiquidInStorage.MassStored() > 0.01f && LiquidOutStorage != null)
                {
                    for (int i = LiquidInStorage.items.Count - 1; i >= 0; i--)
                    {
                        GameObject go = LiquidInStorage.items[i];
                        if (go == null) continue;
                        Pickupable p = go.GetComponent<Pickupable>();
                        if (p != null)
                        {
                            Pickupable taken = p.Take(LiquidOutStorage.RemainingCapacity());
                            if (taken != null)
                            {
                                LiquidOutStorage.Store(taken.gameObject, hide_popups: true);
                            }
                        }
                    }
                }
            }

            if (GasConsumer != null)
            {
                bool hasGas = HasInteriorFluidOutlet(ConduitType.Gas);
                if (GasConsumer.enabled != hasGas)
                {
                    GasConsumer.enabled = hasGas;
                }
                float targetRate = hasGas ? 1f : 0f;
                float targetCap = hasGas ? 1f : 0f;
                if (GasConsumer.consumptionRate != targetRate || GasConsumer.capacityKG != targetCap)
                {
                    GasConsumer.consumptionRate = targetRate;
                    GasConsumer.capacityKG = targetCap;
                }
                if (GasInStorage != null && GasInStorage.capacityKg != targetCap)
                {
                    GasInStorage.capacityKg = targetCap;
                }

                if (!hasGas && GasInStorage != null && GasInStorage.MassStored() > 0.01f && GasOutStorage != null)
                {
                    for (int i = GasInStorage.items.Count - 1; i >= 0; i--)
                    {
                        GameObject go = GasInStorage.items[i];
                        if (go == null) continue;
                        Pickupable p = go.GetComponent<Pickupable>();
                        if (p != null)
                        {
                            Pickupable taken = p.Take(GasOutStorage.RemainingCapacity());
                            if (taken != null)
                            {
                                GasOutStorage.Store(taken.gameObject, hide_popups: true);
                            }
                        }
                    }
                }
            }
        }

    }
}
