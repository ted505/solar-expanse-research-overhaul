using Game.ObjectInfoDataScripts.CustomFacilitiesAndModules;
using HarmonyLib;
using Manager;
using ResearchLabMod.Logic;
using ScriptableObjectScripts;

namespace ResearchLabMod.Patches;

[HarmonyPatch]
internal static class ResearchManagerPatches
{
    [HarmonyPatch(typeof(ResearchManager), "GetResearchPointPerHour")]
    [HarmonyPrefix]
    private static bool GetResearchPointPerHourPrefix(ResearchManager __instance, ResearchDefinition rd, ref float __result)
    {
        __result = ResearchLabMath.GetResearchPointPerHour(__instance, rd);
        return false;
    }

    [HarmonyPatch(typeof(LabFacility), "GetBonusFromLab")]
    [HarmonyPrefix]
    private static bool GetBonusFromLabPrefix(LabFacility __instance, ResearchDefinition rd, ref double __result)
    {
        __result = ResearchLabMath.GetFlatLabOutput(__instance, rd);
        return false;
    }
}
