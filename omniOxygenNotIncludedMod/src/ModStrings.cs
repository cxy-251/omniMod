using System;

namespace OmniMod
{
    /// <summary>
    /// 双语文本工具。
    ///
    /// 缺氧的所有界面文字都存在一张全局字符串表里（firstpass 程序集里的 <c>Strings</c> 类，
    /// 无命名空间）。建筑的名字/描述/效果分别对应三个 key：
    ///   STRINGS.BUILDINGS.PREFABS.&lt;ID大写&gt;.NAME
    ///   STRINGS.BUILDINGS.PREFABS.&lt;ID大写&gt;.DESC
    ///   STRINGS.BUILDINGS.PREFABS.&lt;ID大写&gt;.EFFECT
    ///
    /// 一个 key 只能存一个值，没法同时塞中英文。所以这里的做法是：
    /// 检测当前游戏语言，是中文就写中文，否则写英文。
    /// （更完整的多语言方案是提供 po 翻译文件，后续阶段再做。）
    /// </summary>
    internal static class ModStrings
    {
        /// <summary>一条中英对照文本。</summary>
        internal readonly struct Bilingual
        {
            public readonly string Zh;
            public readonly string En;

            public Bilingual(string zh, string en)
            {
                Zh = zh;
                En = en;
            }

            /// <summary>按当前游戏语言取其中一种。</summary>
            public string Resolve()
            {
                return PreferChinese() ? Zh : En;
            }
        }

        internal static Bilingual T(string zh, string en)
        {
            return new Bilingual(zh, en);
        }

        /// <summary>当前游戏语言是否为中文。本地化系统还没就绪时按英文处理。</summary>
        private static bool PreferChinese()
        {
            try
            {
                string code = Localization.GetCurrentLanguageCode();
                if (!string.IsNullOrEmpty(code) && code.ToLowerInvariant().Contains("zh"))
                {
                    return true;
                }

                Localization.Locale locale = Localization.GetLocale();
                if (locale != null && locale.Lang == Localization.Language.Chinese)
                {
                    return true;
                }
            }
            catch
            {
                // 本地化尚未初始化：忽略，返回 false
            }

            return false;
        }

        /// <summary>
        /// 把一个建筑的 NAME / DESC / EFFECT 三条文字写进游戏字符串表。
        /// 必须在建筑被注册之前调用（我们放在 GeneratedBuildings.LoadGeneratedBuildings 的 Prefix 里）。
        /// </summary>
        internal static void AddBuilding(string buildingId, Bilingual name, Bilingual desc, Bilingual effect)
        {
            string prefix = "STRINGS.BUILDINGS.PREFABS." + buildingId.ToUpperInvariant() + ".";
            Strings.Add(prefix + "NAME", name.Resolve());
            Strings.Add(prefix + "DESC", desc.Resolve());
            Strings.Add(prefix + "EFFECT", effect.Resolve());
        }
    }
}
