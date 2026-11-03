using System.Runtime.CompilerServices;
using HarmonyLib;

namespace OmniMod.Buildings
{
    internal static class IrradiatorParticleTransit
    {
        private sealed class Passage { internal int cell; }
        private static readonly ConditionalWeakTable<HighEnergyParticle, Passage> Passing =
            new ConditionalWeakTable<HighEnergyParticle, Passage>();

        internal static bool IsPassing(HighEnergyParticle particle)
        {
            if (!Passing.TryGetValue(particle, out Passage passage)) return false;
            if (passage.cell == Grid.PosToCell(particle)) return true;
            Passing.Remove(particle);
            return false;
        }

        internal static void Begin(HighEnergyParticle particle)
        {
            Passing.Remove(particle);
            Passing.Add(particle, new Passage { cell = Grid.PosToCell(particle) });
        }
    }

    [HarmonyPatch(typeof(HighEnergyParticle), "Capture")]
    internal static class IrradiatorParticle_Capture_Patch
    {
        private static bool Prefix(HighEnergyParticle __instance, HighEnergyParticlePort input)
        {
            var receiver = input != null ? input.GetComponent<RadboltIrradiator>() : null;
            if (receiver == null) return true;
            receiver.ReceiveParticle(__instance);
            return false;
        }
    }

    [HarmonyPatch(typeof(HighEnergyParticle), nameof(HighEnergyParticle.MovingUpdate))]
    internal static class IrradiatorParticle_Departure_Patch
    {
        private static void Postfix(HighEnergyParticle __instance) => IrradiatorParticleTransit.IsPassing(__instance);
    }
}
