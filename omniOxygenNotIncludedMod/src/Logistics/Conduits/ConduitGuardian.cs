using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using KSerialization;
using OmniMod.Core.Logging;
using UnityEngine;

namespace OmniMod.Conduits
{
    /// <summary>
    /// 全局管道流体守护者（ConduitGuardian）：
    /// 解决 ONI 管道在读档、重载、崩溃恢复或复杂网络切换时流体莫名消失/排空的顽疾。
    /// 1. 存盘拦截：在每次游戏存档时（手动存盘、周期自动存盘、崩溃恢复存盘），自动对全星系所有管道内流体（水/气体）进行像素级快照；
    /// 2. 读档守护校验：游戏重载或重启进入世界后，持续多轮校验所有管道格。一旦发现原先有流体的管道格变为空管（被原版吞包、重构丢包或异常排空），立即自动守护补回流体；
    /// 3. 重建自适应唤醒：深度挂钩 liquidConduitFlow.onConduitsRebuilt，管网实体加载就位瞬时自动补回流体；
    /// 4. 空快照防冲刷：当星系未完全就绪或突发崩溃存盘导致扫描到 0 流体时，坚决保留上一轮有效快照，永不被冲刷为空。
    /// </summary>
    [SerializationConfig(MemberSerialization.OptIn)]
    public class ConduitGuardian : KMonoBehaviour, ISaveLoadable, ISim1000ms
    {
        public static ConduitGuardian Instance { get; private set; }

        [Serialize] private List<int> _liquidCells = new List<int>();
        [Serialize] private List<int> _liquidElements = new List<int>();
        [Serialize] private List<float> _liquidMasses = new List<float>();
        [Serialize] private List<float> _liquidTemps = new List<float>();
        [Serialize] private List<byte> _liquidDiseaseIdxs = new List<byte>();
        [Serialize] private List<int> _liquidDiseaseCounts = new List<int>();

        [Serialize] private List<int> _gasCells = new List<int>();
        [Serialize] private List<int> _gasElements = new List<int>();
        [Serialize] private List<float> _gasMasses = new List<float>();
        [Serialize] private List<float> _gasTemps = new List<float>();
        [Serialize] private List<byte> _gasDiseaseIdxs = new List<byte>();
        [Serialize] private List<int> _gasDiseaseCounts = new List<int>();

        private int _initialVerifyTicks = 0;
        private const int MaxInitialVerifyTicks = 10;
        private float _periodicSnapshotTimer = 0f;

        protected override void OnPrefabInit()
        {
            base.OnPrefabInit();
            Instance = this;
        }

        protected override void OnSpawn()
        {
            base.OnSpawn();
            Instance = this;

            if (Game.Instance != null)
            {
                Game.Instance.OnSave += OnGameSave;

                if (Game.Instance.liquidConduitFlow != null)
                {
                    Game.Instance.liquidConduitFlow.onConduitsRebuilt += OnConduitsRebuilt;
                }
                if (Game.Instance.gasConduitFlow != null)
                {
                    Game.Instance.gasConduitFlow.onConduitsRebuilt += OnConduitsRebuilt;
                }
            }

            _initialVerifyTicks = 0;
            RestoreSnapshots();
        }

        protected override void OnCleanUp()
        {
            if (Game.Instance != null)
            {
                Game.Instance.OnSave -= OnGameSave;

                if (Game.Instance.liquidConduitFlow != null)
                {
                    Game.Instance.liquidConduitFlow.onConduitsRebuilt -= OnConduitsRebuilt;
                }
                if (Game.Instance.gasConduitFlow != null)
                {
                    Game.Instance.gasConduitFlow.onConduitsRebuilt -= OnConduitsRebuilt;
                }
            }

            if (Instance == this)
            {
                Instance = null;
            }

            base.OnCleanUp();
        }

        private void OnConduitsRebuilt()
        {
            // 当游戏管网实体完成重构与连接（每个管道格的 conduitIdx 注册就位）时，立即触发流体核验与复原
            RestoreSnapshots();
        }

        public void Sim1000ms(float dt)
        {
            // 读档前 10 秒内持续每秒核验，防止原版异步建筑与管道在跨帧构建时吞掉流体包
            if (_initialVerifyTicks < MaxInitialVerifyTicks)
            {
                _initialVerifyTicks++;
                RestoreSnapshots();
            }

            // 运行期间每隔 30 秒执行一次静默快照，确保非存盘崩溃时快照也处于最新健康状态
            _periodicSnapshotTimer += dt;
            if (_periodicSnapshotTimer >= 30f)
            {
                _periodicSnapshotTimer = 0f;
                CaptureSnapshots();
            }
        }

        [OnSerializing]
        private void OnSerializing()
        {
            CaptureSnapshots();
        }

        private void OnGameSave(Game.GameSaveData data)
        {
            CaptureSnapshots();
        }

        /// <summary>
        /// 全量抓取星系所有液体/气体管道内的流体快照。
        /// </summary>
        public void CaptureSnapshots()
        {
            try
            {
                if (Game.Instance == null) return;

                CaptureFlow(
                    Game.Instance.liquidConduitFlow,
                    _liquidCells,
                    _liquidElements,
                    _liquidMasses,
                    _liquidTemps,
                    _liquidDiseaseIdxs,
                    _liquidDiseaseCounts);

                CaptureFlow(
                    Game.Instance.gasConduitFlow,
                    _gasCells,
                    _gasElements,
                    _gasMasses,
                    _gasTemps,
                    _gasDiseaseIdxs,
                    _gasDiseaseCounts);
            }
            catch (Exception ex)
            {
                ModLog.Error($"[ConduitGuardian] 管道流体快照抓取异常: {ex}");
            }
        }

        private void CaptureFlow(
            ConduitFlow flow,
            List<int> cells,
            List<int> elements,
            List<float> masses,
            List<float> temps,
            List<byte> diseaseIdxs,
            List<int> diseaseCounts)
        {
            if (flow == null || flow.soaInfo == null) return;

            var newCells = new List<int>();
            var newElements = new List<int>();
            var newMasses = new List<float>();
            var newTemps = new List<float>();
            var newDiseaseIdxs = new List<byte>();
            var newDiseaseCounts = new List<int>();

            int num = flow.soaInfo.NumEntries;
            for (int i = 0; i < num; i++)
            {
                ConduitFlow.Conduit conduit = flow.soaInfo.GetConduit(i);
                ConduitFlow.ConduitContents contents = conduit.GetContents(flow);
                if (contents.mass > 0.001f && contents.element != SimHashes.Void)
                {
                    int cell = flow.soaInfo.GetCell(conduit.idx);
                    if (Grid.IsValidCell(cell))
                    {
                        newCells.Add(cell);
                        newElements.Add((int)contents.element);
                        newMasses.Add(contents.mass);
                        newTemps.Add(contents.temperature);
                        newDiseaseIdxs.Add(contents.diseaseIdx);
                        newDiseaseCounts.Add(contents.diseaseCount);
                    }
                }
            }

            // 安全防线：如果新扫描到的流体格数为 0，但原有快照存有流体数据，
            // 坚决不允许被空数据覆盖（防止管网瞬时丢包或崩溃保存冲刷掉有效快照）
            if (newCells.Count > 0 || cells.Count == 0)
            {
                cells.Clear(); cells.AddRange(newCells);
                elements.Clear(); elements.AddRange(newElements);
                masses.Clear(); masses.AddRange(newMasses);
                temps.Clear(); temps.AddRange(newTemps);
                diseaseIdxs.Clear(); diseaseIdxs.AddRange(newDiseaseIdxs);
                diseaseCounts.Clear(); diseaseCounts.AddRange(newDiseaseCounts);
            }
            else
            {
                ModLog.Warn($"[ConduitGuardian] 拦截到异常空快照抓取：保留上一轮有效的 {cells.Count} 格管道流体快照，防止被冲刷为空！");
            }
        }

        /// <summary>
        /// 校验并守护恢复所有异常排空的管道流体包。
        /// </summary>
        public void RestoreSnapshots()
        {
            try
            {
                if (Game.Instance == null) return;

                RestoreFlow(
                    Game.Instance.liquidConduitFlow,
                    _liquidCells,
                    _liquidElements,
                    _liquidMasses,
                    _liquidTemps,
                    _liquidDiseaseIdxs,
                    _liquidDiseaseCounts,
                    isLiquid: true);

                RestoreFlow(
                    Game.Instance.gasConduitFlow,
                    _gasCells,
                    _gasElements,
                    _gasMasses,
                    _gasTemps,
                    _gasDiseaseIdxs,
                    _gasDiseaseCounts,
                    isLiquid: false);
            }
            catch (Exception ex)
            {
                ModLog.Error($"[ConduitGuardian] 管道流体守护恢复校验异常: {ex}");
            }
        }

        private void RestoreFlow(
            ConduitFlow flow,
            List<int> cells,
            List<int> elements,
            List<float> masses,
            List<float> temps,
            List<byte> diseaseIdxs,
            List<int> diseaseCounts,
            bool isLiquid)
        {
            if (flow == null || cells == null || cells.Count == 0) return;

            int restoredCount = 0;
            float restoredMass = 0f;

            for (int i = 0; i < cells.Count; i++)
            {
                int cell = cells[i];
                if (!Grid.IsValidCell(cell)) continue;

                // 只有当前格子存在对应类型的管道实体时才处理
                if (!flow.HasConduit(cell)) continue;

                ConduitFlow.ConduitContents current = flow.GetContents(cell);

                // 若当前管道格为空，但快照记录原本存有流体：执行安全补回
                if (current.mass <= 0.001f || current.element == SimHashes.Void)
                {
                    SimHashes elemHash = (SimHashes)elements[i];
                    float mass = masses[i];
                    float temp = temps[i];
                    byte disease = diseaseIdxs[i];
                    int diseaseCount = diseaseCounts[i];

                    Element element = ElementLoader.FindElementByHash(elemHash);
                    if (element != null && mass > 0.001f)
                    {
                        // 智能安全温度约束：避免在读档时流体因处于相变边缘而立即爆管
                        if (isLiquid && element.IsLiquid)
                        {
                            temp = Mathf.Clamp(temp, element.lowTemp + 2.0f, element.highTemp - 2.0f);
                        }
                        else if (!isLiquid && element.IsGas)
                        {
                            temp = Mathf.Clamp(temp, element.lowTemp + 2.0f, 9999f);
                        }

                        ConduitFlow.ConduitContents packet = new ConduitFlow.ConduitContents(
                            elemHash, mass, temp, disease, diseaseCount
                        );

                        flow.SetContents(cell, packet);
                        restoredCount++;
                        restoredMass += mass;
                    }
                }
            }

            if (restoredCount > 0)
            {
                string typeStr = isLiquid ? "液体" : "气体";
                ModLog.Warn($"[ConduitGuardian] 管道守护盾已生效：检测到读档后管道{typeStr}异常排空，已自动补回 {restoredCount} 格管道流体（共计 {restoredMass:0.0} kg），流体无损！");
            }
        }
    }
}
