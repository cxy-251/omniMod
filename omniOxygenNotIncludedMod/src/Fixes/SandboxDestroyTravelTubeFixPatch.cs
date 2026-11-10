using HarmonyLib;
using UnityEngine;

namespace OmniMod.Fixes
{
    /// <summary>
    /// 修复《缺氧》原版沙盒销毁工具（SandboxDestroyerTool）无法擦除/拆除运载管道（TravelTube）的陈年 Bug。
    /// 
    /// 原因：
    /// 原版 GameUtil.DestroyCell 硬编码枚举并清理了电线、水管、气管、运输轨道、逻辑线、常规建筑、背景墙等网格层，
    /// 唯独彻底遗漏了 ObjectLayer.TravelTube（34）、TravelTubeTile（35）、ReplacementTravelTube（36）与 TravelTubeConnection（37）。
    /// 
    /// 修复机制：
    /// 在 DestroyCell 完成常规网格清理后，追加检查运载管道及其穿墙套件与连接层，
    /// 若存在残余管道对象则一并彻底销毁，使沙盒擦除工具能够像对待其它管道一样顺手秒拆运载管道。
    /// </summary>
    [HarmonyPatch(typeof(GameUtil), nameof(GameUtil.DestroyCell))]
    public static class SandboxDestroyTravelTubeFixPatch
    {
        private static readonly int[] MissingLayers = new int[]
        {
            (int)ObjectLayer.TravelTube,
            (int)ObjectLayer.TravelTubeTile,
            (int)ObjectLayer.ReplacementTravelTube,
            (int)ObjectLayer.TravelTubeConnection
        };

        public static void Postfix(int cell)
        {
            if (!Grid.IsValidCell(cell)) return;

            for (int i = 0; i < MissingLayers.Length; i++)
            {
                int layer = MissingLayers[i];
                GameObject go = Grid.Objects[cell, layer];
                if (go != null)
                {
                    Util.KDestroyGameObject(go);
                }
            }
        }
    }
}
