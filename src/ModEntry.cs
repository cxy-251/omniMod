using System;
using System.Reflection;
using HarmonyLib;
using KMod;
using UnityEngine;

namespace OmniMod
{
    /// <summary>
    /// Mod 入口类。
    ///
    /// 缺氧的 Mod 加载器会扫描本 DLL 里继承自 <see cref="KMod.UserMod2"/> 的类并调用 OnLoad。
    ///
    /// 这里不用 base.OnLoad（它内部 harmony.PatchAll，任何一个补丁类出错会导致**整批**补丁都不生效）。
    /// 改成逐个 [HarmonyPatch] 类单独 Patch + try/catch：某个补丁挂不上只影响它自己，
    /// 其余功能照常。挂不上的会在 Player.log 里打出来。
    /// </summary>
    public sealed class OmniModUserMod : UserMod2
    {
        public const string LogTag = "OmniMod";

        public override void OnLoad(Harmony harmony)
        {
            Debug.Log($"[{LogTag}] 正在加载（针对游戏版本 744825 构建）…");

            int ok = 0;
            int failed = 0;
            foreach (Type type in Assembly.GetExecutingAssembly().GetTypes())
            {
                if (type.GetCustomAttribute<HarmonyPatch>() == null && !type.IsDefined(typeof(HarmonyPatch), false))
                {
                    continue;
                }

                try
                {
                    new PatchClassProcessor(harmony, type).Patch();
                    ok++;
                }
                catch (Exception e)
                {
                    failed++;
                    Debug.LogWarning($"[{LogTag}] 补丁类挂载失败（跳过）: {type.FullName} — {e.Message}");
                }
            }

            Debug.Log($"[{LogTag}] Harmony 补丁完成：成功 {ok}，失败 {failed}。");
        }
    }
}
