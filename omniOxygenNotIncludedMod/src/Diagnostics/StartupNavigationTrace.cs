using System.Diagnostics;
using System.Text;
using HarmonyLib;
using Klei;
using OmniMod.Core.Logging;

namespace OmniMod.Diagnostics
{
    [HarmonyPatch(typeof(MainMenu), "OnSpawn")]
    internal static class MainMenu_StartupNavigationTrace
    {
        private static void Prefix()
        {
            var settings = GenericGameSettings.instance;
            ModLog.Info($"[启动流程] 进入主菜单；自动继续={settings.autoResumeGame}；" +
                $"重启续档标志={KPlayerPrefs.HasKey("AutoResumeSaveFile")}；" +
                $"脚本指定存档={!string.IsNullOrEmpty(settings.scriptedProfile?.saveGame)}。");
        }
    }

    [HarmonyPatch(typeof(MainMenu), "ResumeGame")]
    internal static class MainMenu_ResumeNavigationTrace
    {
        private static void Prefix()
        {
            var trace = new StackTrace(1, false);
            var caller = new StringBuilder();
            for (int i = 0; i < trace.FrameCount && i < 6; i++)
            {
                var method = trace.GetFrame(i)?.GetMethod();
                if (method == null) continue;
                if (caller.Length > 0) caller.Append(" <- ");
                caller.Append(method.DeclaringType?.Name).Append('.').Append(method.Name);
            }
            ModLog.Info("[启动流程] 原版继续游戏被调用：" + caller + "；重启续档标志=" +
                KPlayerPrefs.HasKey("AutoResumeSaveFile") + "。");
        }
    }
}
