using System;
using System.Collections.Generic;
using Data.ScriptableObject;
using Game.ObjectInfoDataScripts;
using HarmonyLib;

namespace ResearchLabMod.Patches;

[HarmonyPatch]
internal static class FacilityPlacementPatches
{
    private static readonly HashSet<string> SpecializedLabIds = new HashSet<string>(StringComparer.Ordinal)
    {
        "rf_life_science_facility",
        "rf_industrial_research_facility",
        "rf_spaceflight_research_facility",
        "rf_power_research_facility",
    };

    [HarmonyPatch(typeof(ObjectInfoData), "CanAddFacility")]
    [HarmonyPostfix]
    private static void ObjectInfoDataCanAddFacilityPostfix(ObjectInfoData __instance, FacilityBaseDescriptor facilityBaseDescriptor, ref bool __result)
    {
        if (!__result || !IsSpecializedResearchLab(facilityBaseDescriptor) || !IsEarth(__instance))
            return;

        __result = false;
    }

    private static bool IsSpecializedResearchLab(FacilityBaseDescriptor facilityBaseDescriptor)
    {
        return facilityBaseDescriptor != null && SpecializedLabIds.Contains(facilityBaseDescriptor.ID);
    }

    private static bool IsEarth(ObjectInfoData objectInfoData)
    {
        var objectInfo = objectInfoData?.ObjectInfo;
        if (objectInfo == null)
            return false;

        return string.Equals(objectInfo.idTranslation, "Earth", StringComparison.OrdinalIgnoreCase)
            || string.Equals(objectInfo.ObjectNameForLogs, "Earth", StringComparison.OrdinalIgnoreCase)
            || string.Equals(objectInfo.ObjectName, "EARTH", StringComparison.OrdinalIgnoreCase);
    }
}
