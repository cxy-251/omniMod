using System.Collections.Generic;
using System;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace OmniMod.Buildings
{
    /// <summary>
    /// 区域定值作为 Grid.Radiation 的模拟层，植物、复制人、传感器和覆盖图读取相同值。
    /// 不向原版衰减场不断追加辐射，不写原生指针；撤销后立即恢复实时背景值。
    /// </summary>
    internal static class UniformRadiationField
    {
        private sealed class Field
        {
            internal int world, minX, maxX, minY, maxY;
            internal float rads;
            internal bool Contains(int x, int y, int worldId) => world == worldId &&
                x >= minX && x <= maxX && y >= minY && y <= maxY;
            internal bool Same(Field other) => world == other.world && minX == other.minX &&
                maxX == other.maxX && minY == other.minY && maxY == other.maxY && rads == other.rads;
        }

        private sealed class Page
        {
            internal readonly float[] values = new float[256];
            internal readonly byte[] worlds = new byte[256];
            internal int used;
        }

        private sealed class Samples
        {
            internal readonly int width = Grid.WidthInCells;
            internal readonly int height = Grid.HeightInCells;
            internal readonly Page[] pages = new Page[(Grid.CellCount + 255) / 256];
        }

        private static readonly Dictionary<RadboltIrradiator, Field> Fields = new Dictionary<RadboltIrradiator, Field>();
        // 按 256 格分配小页，空页即时释放；读取只做索引，不扫描设备或分配集合。
        private static volatile Samples samples;

        internal static void Set(RadboltIrradiator owner, int world, int minX, int maxX, int minY, int maxY, float rads)
        {
            if (samples == null || samples.width != Grid.WidthInCells || samples.height != Grid.HeightInCells)
            {
                Fields.Clear();
                samples = new Samples();
            }
            var next = new Field { world = world, minX = minX, maxX = maxX, minY = minY, maxY = maxY, rads = rads };
            Fields.TryGetValue(owner, out Field previous);
            if (previous != null && previous.Same(next)) return;
            Fields[owner] = next;
            if (previous != null) Refresh(previous);
            Refresh(next);
        }

        internal static void Remove(RadboltIrradiator owner)
        {
            if (!Fields.TryGetValue(owner, out Field previous)) return;
            Fields.Remove(owner);
            if (Fields.Count == 0) { samples = null; return; }
            Refresh(previous);
        }

        private static void Refresh(Field changed)
        {
            Samples current = samples;
            if (current == null || Grid.WorldIdx == null) return;
            var overlapping = new List<Field>();
            foreach (Field field in Fields.Values)
                if (field.minX <= changed.maxX && field.maxX >= changed.minX &&
                    field.minY <= changed.maxY && field.maxY >= changed.minY) overlapping.Add(field);
            for (int y = changed.minY; y <= changed.maxY && y < current.height; y++)
            {
                if (y < 0) continue;
                for (int x = changed.minX; x <= changed.maxX && x < current.width; x++)
                {
                    if (x < 0) continue;
                    int cell = y * current.width + x;
                    if (cell >= Grid.WorldIdx.Length) continue;
                    byte world = Grid.WorldIdx[cell];
                    float target = 0f;
                    foreach (Field field in overlapping)
                        if (field.Contains(x, y, world)) target = Mathf.Max(target, field.rads);
                    int pageIndex = cell >> 8;
                    int offset = cell & 255;
                    Page page = current.pages[pageIndex];
                    if (page == null)
                    {
                        if (target <= 0f) continue;
                        current.pages[pageIndex] = page = new Page();
                    }
                    if (page.values[offset] <= 0f && target > 0f) page.used++;
                    else if (page.values[offset] > 0f && target <= 0f) page.used--;
                    // 先撤销旧值，再发布世界和新值；生态舱网格重新归属时不会串入其它世界。
                    page.values[offset] = 0f;
                    page.worlds[offset] = world;
                    page.values[offset] = target;
                    if (page.used == 0) current.pages[pageIndex] = null;
                }
            }
        }

        internal static bool TryGet(int cell, out float rads)
        {
            rads = 0f;
            Samples current = samples;
            if (current == null || cell < 0 || current.width != Grid.WidthInCells || Grid.WorldIdx == null ||
                cell >= Grid.WorldIdx.Length) return false;
            int pageIndex = cell >> 8;
            if (pageIndex >= current.pages.Length) return false;
            Page page = current.pages[pageIndex];
            if (page == null) return false;
            int offset = cell & 255;
            if (page.worlds[offset] != Grid.WorldIdx[cell]) return false;
            rads = page.values[offset];
            return rads > 0f;
        }

        // 与原版 struct 索引器保持相同栈参数，供各读取点直接调用，避免小 getter 内联绕过补丁。
        internal static float Read(ref Grid.RadiationIndexer original, int cell) =>
            TryGet(cell, out float rads) ? rads : original[cell];

        internal static void Reset() { samples = null; Fields.Clear(); }
    }

    // 根据本机 U59-744825 程序集的所有 RadiationIndexer 调用点核对。
    // 只替换具体方法里的该索引器调用；不挂泛型状态机基类，也不操作原生内存。
    [HarmonyPatch]
    internal static class UniformRadiation_Read_Patch
    {
        private static readonly string[][] Readers =
        {
            new[] { "SafeCellQuery", "GetFlags" },
            new[] { "AbsorbCellQuery", "GetAbsorbOxygenFlags" },
            new[] { "HighEnergyParticleSpawner", "LauncherUpdate" },
            new[] { "LogicRadiationSensor", "Sim200ms" },
            new[] { "SeedProducer", "RollForMutation" },
            new[] { "DiseaseContainers", "CalculateDelta" },
            new[] { "RadiationMonitor", "CheckRadiationLevel" },
            new[] { "PropertyTextures", "UpdateRadiation" },
            new[] { "SimDebugView", "GetRadiationColour" },
            new[] { "SelectToolHoverTextCard", "UpdateHoverElements" },
            new[] { "AdditionalDetailsPanel", "BuildFactorsStrings" },
            new[] { "RadiationBalanceDisplayer", "GetTooltip" },
            new[] { "MinionVitalsPanel", "GetRadiationTooltip" },
            new[] { "MinionVitalsPanel", "check_radiation" },
            new[] { "TemplateSelectionInfoPanel", "TotalRadiation" },
            new[] { "TemplateSelectionInfoPanel", "AverageRadiation" },
            new[] { "AmbienceManager+Quadrant", "Update" },
            new[] { "RadiationVulnerable+StatesInstance", "GetRadiationThresholdCrossed" },
            new[] { "SelectToolHoverTextCard+<>c", "<OnSpawn>b__13_2" }
        };

        private static IEnumerable<MethodBase> TargetMethods()
        {
            foreach (string[] reader in Readers)
            {
                Type type = AccessTools.TypeByName(reader[0]);
                if (type == null) throw new MissingMemberException(reader[0]);
                bool found = false;
                foreach (MethodInfo method in AccessTools.GetDeclaredMethods(type))
                    if (method.Name == reader[1] && !method.ContainsGenericParameters && method.GetMethodBody() != null)
                    { found = true; yield return method; }
                if (!found) throw new MissingMethodException(reader[0], reader[1]);
            }
        }

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo getter = AccessTools.PropertyGetter(typeof(Grid.RadiationIndexer), "Item");
            MethodInfo read = AccessTools.Method(typeof(UniformRadiationField), nameof(UniformRadiationField.Read));
            foreach (CodeInstruction instruction in instructions)
            {
                if (instruction.Calls(getter))
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = read;
                }
                yield return instruction;
            }
        }
    }

    [HarmonyPatch(typeof(Game), "OnDestroy")]
    internal static class UniformRadiation_Reset_Patch
    {
        private static void Postfix() => UniformRadiationField.Reset();
    }
}
