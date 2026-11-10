using System;
using System.Collections.Generic;
using OmniMod.Core.Logging;
using UnityEngine;

namespace OmniMod.Buildings
{
    /// <summary>
    /// 全图管道流体智能校准与急救保底系统：
    /// 解决管网流体异常排空、错位杂质（如误充纯水导致原油/乙醇/核废料管网堵塞）的问题。
    /// 
    /// 核心原则：
    /// 1. 【按源定质，绝不盲充】：通过 FlowUtilityNetwork 与深度溯源算法，精准识别管网连接的水泵环境、
    ///    作物需求（如顶针芦苇需污染水、小吃豆需乙醇）、机器产出与配方，整网统一赋予纯净正确流体。
    /// 2. 【杜绝全图盲充纯水】：严禁在没有明确依据时将未知管网强制设定为水；未知或非受损管网 100% 保持现状。
    /// 3. 【错位杂质一键纠正与排空】：
    ///    - 提供【一键智能校准管网流体】：将所有管网中的错位杂质（如误充入原油管道、乙醇管道、核废料管道的纯水）
    ///      一键替换为该管网真实的源头纯净流体；
    ///    - 提供【一键排空管网错位杂质】：将管网中与源头不符的杂质流体彻底清空，让水泵自然重新抽送，杜绝爆管堵塞。
    /// </summary>
    public static class LiquidPipeAutoRestorer
    {
        public static void Sim1000ms(float dt)
        {
            // 严禁在游戏运行期或加载后自动全局篡改管网流体，杜绝任何对玩家正常管道流体的意外篡改与错位。
            // 仅在玩家主动在建筑侧边栏（如恒温流体冷却机、核废料处理机）点击校准/排空按钮时才执行针对性操作。
        }

        public static void ForceRestoreNow()
        {
            AutoCorrectAllMismatchedPipes();
        }

        /// <summary>
        /// 智能校准全图所有已知管网：纠正所有因历史原因充入错误流体（如误充纯水）的管道格，
        /// 恢复为该管网真实的源头/需求流体（如原油、乙醇、核废料、超级冷却剂、污染水）。
        /// </summary>
        public static void AutoCorrectAllMismatchedPipes()
        {
            if (Game.Instance == null || Game.Instance.liquidConduitFlow == null || Game.Instance.liquidConduitSystem == null) return;
            ConduitFlow flow = Game.Instance.liquidConduitFlow;

            IList<UtilityNetwork> networks = Game.Instance.liquidConduitSystem.GetNetworks();
            if (networks == null || networks.Count == 0) return;

            int correctedCells = 0;
            int refilledEmptyCells = 0;

            for (int i = 0; i < networks.Count; i++)
            {
                FlowUtilityNetwork net = networks[i] as FlowUtilityNetwork;
                if (net == null || net.conduits == null || net.conduits.Count == 0) continue;

                SimHashes targetElement = DetectNetworkElement(net);
                if (targetElement == SimHashes.Void) continue; // 未知或无明确源头的管网，绝不盲目篡改！

                Element elem = ElementLoader.FindElementByHash(targetElement);
                if (elem == null || !elem.IsLiquid) continue;

                float safeTemp = GetSafeLiquidTemp(targetElement);

                foreach (FlowUtilityNetwork.IItem item in net.conduits)
                {
                    if (item == null) continue;
                    int cell = item.Cell;
                    if (!flow.HasConduit(cell)) continue;

                    ConduitFlow.ConduitContents contents = flow.GetContents(cell);

                    // 1. 若管道完全排空，且该管网有明确源头/受体（如恒温冷却机循环或核废料进料）：补充满管流体
                    if (contents.mass <= 0.05f || contents.element == SimHashes.Void)
                    {
                        flow.SetContents(cell, new ConduitFlow.ConduitContents(targetElement, 10f, safeTemp, byte.MaxValue, 0));
                        refilledEmptyCells++;
                    }
                    // 2. 若管道内含有错位流体（例如管网明确是原油/乙醇/核废料/超冷，但管道格内滞留了纯水或其他杂质）
                    else if (contents.element != targetElement)
                    {
                        // 纠正错位流体为正确流体
                        flow.SetContents(cell, new ConduitFlow.ConduitContents(targetElement, 10f, safeTemp, byte.MaxValue, 0));
                        correctedCells++;
                    }
                }
            }

            if (correctedCells > 0 || refilledEmptyCells > 0)
            {
                ModLog.Info($"[管道智能校准] 成功校验全图管网！已纠正错位流体 {correctedCells} 格，补全干涸管线 {refilledEmptyCells} 格，全线已与源头完全对齐。");
            }
        }

        /// <summary>
        /// 一键排空全图所有与管网源头不符的错位/污染杂质流体
        /// </summary>
        public static void FlushAllMismatchedPipes()
        {
            if (Game.Instance == null || Game.Instance.liquidConduitFlow == null || Game.Instance.liquidConduitSystem == null) return;
            ConduitFlow flow = Game.Instance.liquidConduitFlow;

            IList<UtilityNetwork> networks = Game.Instance.liquidConduitSystem.GetNetworks();
            if (networks == null || networks.Count == 0) return;

            int flushedCount = 0;

            for (int i = 0; i < networks.Count; i++)
            {
                FlowUtilityNetwork net = networks[i] as FlowUtilityNetwork;
                if (net == null || net.conduits == null || net.conduits.Count == 0) continue;

                SimHashes targetElement = DetectNetworkElement(net);
                if (targetElement == SimHashes.Void) continue;

                foreach (FlowUtilityNetwork.IItem item in net.conduits)
                {
                    if (item == null) continue;
                    int cell = item.Cell;
                    if (!flow.HasConduit(cell)) continue;

                    ConduitFlow.ConduitContents contents = flow.GetContents(cell);
                    if (contents.mass > 0.01f && contents.element != targetElement && contents.element != SimHashes.Void)
                    {
                        flow.SetContents(cell, new ConduitFlow.ConduitContents(SimHashes.Void, 0f, 0f, 0, 0));
                        flushedCount++;
                    }
                }
            }

            ModLog.Info($"[管道杂质排空] 成功排空 {flushedCount} 格错位管道流体，管网已完全清洁就绪。");
        }

        /// <summary>
        /// 针对单个建筑所在的管网执行智能校准/补满
        /// </summary>
        public static void CorrectOrRefillNetworkForBuilding(GameObject buildingGo)
        {
            if (buildingGo == null || Game.Instance == null || Game.Instance.liquidConduitSystem == null) return;
            FlowUtilityNetwork net = GetNetworkForBuilding(buildingGo);
            if (net != null)
            {
                CorrectSingleNetwork(net);
            }
            else
            {
                AutoCorrectAllMismatchedPipes();
            }
        }

        /// <summary>
        /// 针对单个建筑所在的管网执行错位杂质一键排空
        /// </summary>
        public static void FlushNetworkForBuilding(GameObject buildingGo)
        {
            if (buildingGo == null || Game.Instance == null || Game.Instance.liquidConduitSystem == null) return;
            FlowUtilityNetwork net = GetNetworkForBuilding(buildingGo);
            if (net != null)
            {
                FlushSingleNetwork(net);
            }
            else
            {
                FlushAllMismatchedPipes();
            }
        }

        private static FlowUtilityNetwork GetNetworkForBuilding(GameObject go)
        {
            Building building = go.GetComponent<Building>();
            if (building == null || Game.Instance?.liquidConduitSystem == null) return null;

            int inCell = building.GetUtilityInputCell();
            int outCell = building.GetUtilityOutputCell();

            if (Grid.IsValidCell(inCell))
            {
                FlowUtilityNetwork net = Game.Instance.liquidConduitSystem.GetNetworkForCell(inCell) as FlowUtilityNetwork;
                if (net != null) return net;
            }
            if (Grid.IsValidCell(outCell))
            {
                FlowUtilityNetwork net = Game.Instance.liquidConduitSystem.GetNetworkForCell(outCell) as FlowUtilityNetwork;
                if (net != null) return net;
            }
            return null;
        }

        public static void CorrectSingleNetwork(FlowUtilityNetwork net)
        {
            if (net == null || Game.Instance?.liquidConduitFlow == null) return;
            ConduitFlow flow = Game.Instance.liquidConduitFlow;
            SimHashes targetElement = DetectNetworkElement(net);
            if (targetElement == SimHashes.Void) return;

            float safeTemp = GetSafeLiquidTemp(targetElement);
            int count = 0;
            foreach (var item in net.conduits)
            {
                if (item == null) continue;
                int cell = item.Cell;
                if (!flow.HasConduit(cell)) continue;
                flow.SetContents(cell, new ConduitFlow.ConduitContents(targetElement, 10f, safeTemp, byte.MaxValue, 0));
                count++;
            }
            ModLog.Info($"[管网校准] 成功对管网 {net.id} 充填/校准 {count} 格纯净 {targetElement}。");
        }

        public static void FlushSingleNetwork(FlowUtilityNetwork net)
        {
            if (net == null || Game.Instance?.liquidConduitFlow == null) return;
            ConduitFlow flow = Game.Instance.liquidConduitFlow;
            int count = 0;
            foreach (var item in net.conduits)
            {
                if (item == null) continue;
                int cell = item.Cell;
                if (!flow.HasConduit(cell)) continue;
                flow.SetContents(cell, new ConduitFlow.ConduitContents(SimHashes.Void, 0f, 0f, 0, 0));
                count++;
            }
            ModLog.Info($"[管网排空] 成功排空管网 {net.id} 全部 {count} 格流体。");
        }

        /// <summary>
        /// 基于全管网深度拓扑与建筑属性，精准识别该管网本应运输的真实流体：
        /// 1. 水泵抽吸环境与历史；
        /// 2. 冷却机/核废料机/石油合成机等 Mod 建筑专属流体；
        /// 3. 水培砖所种作物的灌溉需求；
        /// 4. 原版用液建筑的输入端口定义；
        /// 5. 无法确定的未知管网坚决返回 SimHashes.Void，杜绝盲目注水！
        /// </summary>
        public static SimHashes DetectNetworkElement(FlowUtilityNetwork net)
        {
            if (net == null) return SimHashes.Void;

            // 1. 优先扫描 Sinks 中具有明确专一需求的建筑（如核废料机、流体冷却机、水培砖植物）
            if (net.sinks != null)
            {
                foreach (FlowUtilityNetwork.IItem item in net.sinks)
                {
                    if (item == null || item.GameObject == null) continue;
                    GameObject go = item.GameObject;

                    // 1.1 核废料处理机（进料必须为液态核废料）
                    if (go.GetComponent<NuclearReprocessor>() != null) return SimHashes.NuclearWaste;

                    // 1.2 恒温流体冷却机闭环
                    var chiller = go.GetComponent<LiquidChiller>();
                    if (chiller != null)
                    {
                        return chiller.TargetTemperatureCelsius < -10f ? SimHashes.SuperCoolant : SimHashes.DirtyWater;
                    }

                    // 1.3 水培砖与植物（PlantablePlot / IrrigationMonitor）
                    PlantablePlot plot = go.GetComponent<PlantablePlot>();
                    if (plot != null && plot.Occupant != null)
                    {
                        StateMachineController smc = plot.Occupant.GetComponent<StateMachineController>();
                        IrrigationMonitor.Def irrDef = smc != null ? smc.GetDef<IrrigationMonitor.Def>() : null;
                        if (irrDef != null && irrDef.consumedElements != null)
                        {
                            foreach (var cInfo in irrDef.consumedElements)
                            {
                                Element elem = ElementLoader.GetElement(cInfo.tag);
                                if (elem != null && elem.IsLiquid) return elem.id;
                            }
                        }
                        string pName = plot.Occupant.name;
                        if (pName.Contains("BeanPlant")) return SimHashes.Ethanol;
                        if (pName.Contains("SpiceVine") || pName.Contains("BasicFabricMaterialPlant")) return SimHashes.DirtyWater;
                        if (pName.Contains("ColdWheat") || pName.Contains("PrickleFlower") || pName.Contains("Oxyfern")) return SimHashes.Water;
                        if (pName.Contains("SaltPlant")) return SimHashes.SaltWater;
                    }

                    // 1.4 原版单元素输入建筑
                    KPrefabID kpid = go.GetComponent<KPrefabID>();
                    if (kpid != null)
                    {
                        if (kpid.PrefabTag == "OilRefinery") return SimHashes.CrudeOil;
                        if (kpid.PrefabTag == "PetroleumGenerator") return SimHashes.Petroleum;
                        if (kpid.PrefabTag == "Polymerizer") return SimHashes.Petroleum;
                        if (kpid.PrefabTag == "WaterPurifier") return SimHashes.DirtyWater;
                        if (kpid.PrefabTag == "Desalinator") return SimHashes.SaltWater;
                        if (kpid.PrefabTag == "Electrolyzer") return SimHashes.Water;
                    }

                    ConduitConsumer consumer = go.GetComponent<ConduitConsumer>();
                    if (consumer != null && consumer.conduitType == ConduitType.Liquid && consumer.capacityTag.IsValid)
                    {
                        Element tagElem = ElementLoader.GetElement(consumer.capacityTag);
                        if (tagElem != null && tagElem.IsLiquid && tagElem.id != SimHashes.Water)
                        {
                            return tagElem.id;
                        }
                    }
                }
            }

            // 2. 扫描 Sources（泵、产液机）识别真实流体源
            if (net.sources != null)
            {
                foreach (FlowUtilityNetwork.IItem item in net.sources)
                {
                    if (item == null || item.GameObject == null) continue;
                    GameObject go = item.GameObject;

                    // 2.1 泵（Pump / ElementConsumer / Storage）
                    Pump pump = go.GetComponent<Pump>();
                    if (pump != null && pump.conduitType == ConduitType.Liquid)
                    {
                        int pumpCell = Grid.PosToCell(go);

                        // A. 泵所在液池环境采样格
                        ElementConsumer consumer = go.GetComponent<ElementConsumer>();
                        if (consumer != null)
                        {
                            int sampleCell = Grid.PosToCell(go.transform.position + consumer.sampleCellOffset);
                            if (Grid.IsValidCell(sampleCell))
                            {
                                Element sampleElem = Grid.Element[sampleCell];
                                if (sampleElem != null && sampleElem.IsLiquid) return sampleElem.id;
                            }
                        }
                        // B. 泵自身所在格子及周边占位格
                        if (Grid.IsValidCell(pumpCell))
                        {
                            Element cellElem = Grid.Element[pumpCell];
                            if (cellElem != null && cellElem.IsLiquid) return cellElem.id;
                        }
                        OccupyArea area = go.GetComponent<OccupyArea>();
                        if (area != null && area.OccupiedCellsOffsets != null)
                        {
                            foreach (CellOffset offset in area.OccupiedCellsOffsets)
                            {
                                int occCell = Grid.OffsetCell(pumpCell, offset);
                                if (Grid.IsValidCell(occCell))
                                {
                                    Element oe = Grid.Element[occCell];
                                    if (oe != null && oe.IsLiquid) return oe.id;
                                }
                            }
                        }
                        // C. 泵内存储
                        Storage storage = go.GetComponent<Storage>();
                        if (storage != null && storage.items != null)
                        {
                            foreach (var sItem in storage.items)
                            {
                                if (sItem == null) continue;
                                PrimaryElement pe = sItem.GetComponent<PrimaryElement>();
                                if (pe != null && pe.Element != null && pe.Element.IsLiquid) return pe.ElementID;
                            }
                        }
                    }

                    // 2.2 冷却机闭环
                    var chiller = go.GetComponent<LiquidChiller>();
                    if (chiller != null)
                    {
                        return chiller.TargetTemperatureCelsius < -10f ? SimHashes.SuperCoolant : SimHashes.DirtyWater;
                    }

                    // 2.3 产液设备
                    if (go.GetComponent<PetroleumSynthesizer>() != null) return SimHashes.Petroleum;
                    if (go.GetComponent<BioFermenter>() != null) return SimHashes.Ethanol;
                    if (go.GetComponent<NuclearReprocessor>() != null) return SimHashes.Water;
                    if (go.GetComponent<AutoIceMaker>() != null) return SimHashes.Water;
                }
            }

            // 3. 若均未匹配，返回 SimHashes.Void，绝不盲目充水！
            return SimHashes.Void;
        }

        public static float GetSafeLiquidTemp(SimHashes hash)
        {
            Element elem = ElementLoader.FindElementByHash(hash);
            if (elem == null) return 293.15f;
            float low = elem.lowTemp;
            float high = elem.highTemp;

            if (hash == SimHashes.NuclearWaste) return 313.15f;
            if (hash == SimHashes.SuperCoolant) return 253.15f;
            if (hash == SimHashes.DirtyWater) return 283.15f;
            if (hash == SimHashes.CrudeOil) return 333.15f;
            if (hash == SimHashes.Petroleum) return 333.15f;
            if (hash == SimHashes.Ethanol) return 278.15f;
            if (hash == SimHashes.SaltWater) return 293.15f;
            if (hash == SimHashes.Brine) return 278.15f;

            return Mathf.Clamp(293.15f, low + 10f, high - 10f);
        }
    }
}
