using System;
using System.Reflection;
using HarmonyLib;
using KMod;
using OmniMod.Core.Logging;

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
        public override void OnLoad(Harmony harmony)
        {
            ModLog.Info("正在加载（针对游戏版本 744825 构建）…");
            ModLog.Info("构建标识：fix-20261103-food38（过滤失效库存引用、及时释放共享实体、保护食物热量统计）");

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
                    ModLog.Warn($"补丁类挂载失败（跳过）: {type.FullName} — {e.Message}");
                }
            }

            ModLog.Info($"Harmony 补丁完成：成功 {ok}，失败 {failed}。容灾防丢档系统已就绪。");

            // 本次专项排查只计时固定的调度入口，每 10 秒汇总；不挂载逐物品/逐格子的计时。
            if (Diagnostics.StutterProfiler.Enabled)
            {
                try
                {
                    Diagnostics.StutterProfiler.Install(harmony);
                    ModLog.Info("卡顿诊断探针已启动。");
                }
                catch (Exception e)
                {
                    ModLog.Warn($"[卡顿诊断] 安装失败: {e}");
                }
            }
        }
    }
}
