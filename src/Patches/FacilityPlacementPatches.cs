using System;
using Data.ScriptableObject;
using Game.ObjectInfoDataScripts;
using HarmonyLib;

namespace ResearchLabMod.Patches;

[HarmonyPatch]
internal static class FacilityPlacementPatches
{
    [HarmonyPatch(typeof(ObjectInfoData), "CanAddFacility")]
    [HarmonyPostfix]
    private static void ObjectInfoDataCanAddFacilityPostfix(ObjectInfoData __instance, FacilityBaseDescriptor facilityBaseDescriptor, ref bool __result)
    {
        if (!__result || !IsBlockedOnEarth(facilityBaseDescriptor) || !IsEarth(__instance))
            return;

        __result = false;
    }

    private static bool IsBlockedOnEarth(FacilityBaseDescriptor facilityBaseDescriptor)
    {
        return facilityBaseDescriptor != null
            && Plugin.ResearchOverhaulConfig != null
            && Plugin.ResearchOverhaulConfig.IsBlockedOnEarth(facilityBaseDescriptor.ID);
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
