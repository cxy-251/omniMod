using System;
using System.Collections.Generic;
using KSerialization;
using OmniMod.Core.Logging;
using UnityEngine;

namespace OmniMod.Conduits
{
    /// <summary>
    /// 全局管道流体守护者（ConduitGuardian - 已停用流体生成逻辑）：
    /// 原版游戏原生的管道保存机制早已完全完备。
    /// 此前管道流体消失的根因是生态舱入口的流体接口抽水，现已通过 HasInteriorFluidOutlet 完全解决。
    /// 为彻底杜绝在管道运行中凭空生成虚假流体包导致恒温流体冷却机堵塞爆管，
    /// 本组件所有流体快照与自动补水逻辑已永久清空禁用，仅保留类定义与字段结构以保证老存档序列化平滑过渡。
    /// </summary>
    [SerializationConfig(MemberSerialization.OptIn)]
    public class ConduitGuardian : KMonoBehaviour, ISaveLoadable
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

        protected override void OnPrefabInit()
        {
            base.OnPrefabInit();
            Instance = this;
        }

        protected override void OnSpawn()
        {
            base.OnSpawn();
            Instance = this;

            // 清空所有历史快照数据，杜绝残留数据向管道注入任何流体
            ClearAllSnapshots();
        }

        protected override void OnCleanUp()
        {
            if (Instance == this)
            {
                Instance = null;
            }

            base.OnCleanUp();
        }

        private void ClearAllSnapshots()
        {
            _liquidCells?.Clear();
            _liquidElements?.Clear();
            _liquidMasses?.Clear();
            _liquidTemps?.Clear();
            _liquidDiseaseIdxs?.Clear();
            _liquidDiseaseCounts?.Clear();

            _gasCells?.Clear();
            _gasElements?.Clear();
            _gasMasses?.Clear();
            _gasTemps?.Clear();
            _gasDiseaseIdxs?.Clear();
            _gasDiseaseCounts?.Clear();
        }

        /// <summary>空方法，保留向后兼容</summary>
        public void CaptureSnapshots() { }

        /// <summary>空方法，保留向后兼容</summary>
        public void RestoreSnapshots() { }
    }
}
