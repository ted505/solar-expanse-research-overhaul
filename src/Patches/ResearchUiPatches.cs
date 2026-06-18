using System;
using System.Collections.Generic;
using System.Reflection;
using Data.ScriptableObject;
using Extensions;
using Game;
using Game.Info;
using Game.ObjectInfoDataScripts;
using Game.UI.Windows.Elements;
using Game.UI.MainWindow;
using Game.UI.Windows.Windows.ResearchTree;
using HarmonyLib;
using Manager;
using ResearchLabMod.Logic;
using ResearchLabMod.UI;
using ScriptableObjectScripts;
using TMPro;
using UnityEngine;

namespace ResearchLabMod.Patches;

[HarmonyPatch]
internal static class ResearchUiPatches
{
    private static readonly FieldInfo RightPanelCurrentField = AccessTools.Field(typeof(ResearchTreeRightPanel), "current");
    private static readonly FieldInfo RightPanelCostField = AccessTools.Field(typeof(ResearchTreeRightPanel), "cost");
    private static readonly FieldInfo RightPanelCostTooltipField = AccessTools.Field(typeof(ResearchTreeRightPanel), "costShowTooltip");
    private static readonly FieldInfo TreeElementDaysQueueField = AccessTools.Field(typeof(ResearchTreeElement), "daysQueue");
    private static readonly FieldInfo TreeElementResearchDefinitionField = AccessTools.Field(typeof(ResearchTreeElement), "researchDefinition");
    private static readonly FieldInfo TreeElementTitleField = AccessTools.Field(typeof(ResearchTreeElement), "title");
    private static readonly FieldInfo TreeElementPointTextField = AccessTools.Field(typeof(ResearchTreeElement), "pointTextMeshPro");
    private static readonly FieldInfo TreeElementIconProgressField = AccessTools.Field(typeof(ResearchTreeElement), "iconWithProgressBar");
    private static readonly FieldInfo TreeElementLockImageField = AccessTools.Field(typeof(ResearchTreeElement), "lockImage");
    private static readonly FieldInfo LeftPanelTitleResearchField = AccessTools.Field(typeof(ResearchTreeElementLeftPanel), "titleResearch");
    private static readonly FieldInfo LeftPanelPointTextField = AccessTools.Field(typeof(ResearchTreeElementLeftPanel), "pointTextMeshPro");

    [HarmonyPatch(typeof(ResearchTree), "Start")]
    [HarmonyPostfix]
    private static void ResearchTreeStartPostfix(ResearchTree __instance)
    {
        if (__instance != null && __instance.GetComponent<ResearchRateOverlay>() == null)
            __instance.gameObject.AddComponent<ResearchRateOverlay>();
    }

    [HarmonyPatch(typeof(ResearchTreeRightPanel), "UpdateDate")]
    [HarmonyPostfix]
    private static void ResearchTreeRightPanelUpdateDatePostfix(ResearchTreeRightPanel __instance)
    {
        try
        {
            var current = RightPanelCurrentField?.GetValue(__instance) as ResearchDefinition;
            if (current == null)
                return;

            var researchManager = MonoBehaviourSingleton<GameManager>.Instance.Player.ResearchManager;
            var cost = RightPanelCostField?.GetValue(__instance) as TextMeshProUGUI;
            if (cost != null)
            {
                cost.text = ResearchLabMath.FormatWorkMonths(ResearchLabMath.GetResearchWorkCost(researchManager, current))
                    + " RP / "
                    + ResearchLabMath.FormatResearchTime(researchManager.GetResearchCost(current));
            }

            var tooltip = RightPanelCostTooltipField?.GetValue(__instance) as ShowToolTip;
            if (tooltip != null)
                tooltip.CustomTextFromCode = BuildResearchTooltip(researchManager, current);
        }
        catch (Exception ex)
        {
            PluginLogWarning("ResearchTreeRightPanel.UpdateDate patch failed: " + ex.Message);
        }
    }

    [HarmonyPatch(typeof(ResearchTreeElement), "UpdateLook")]
    [HarmonyPostfix]
    private static void ResearchTreeElementUpdateLookPostfix(ResearchTreeElement __instance)
    {
        try
        {
            var daysQueue = TreeElementDaysQueueField?.GetValue(__instance) as TextMeshProUGUI;
            var rd = TreeElementResearchDefinitionField?.GetValue(__instance) as ResearchDefinition;
            if (rd == null)
                return;

            if (daysQueue != null)
            {
                var researchManager = MonoBehaviourSingleton<GameManager>.Instance.Player.ResearchManager;
                daysQueue.text = ResearchLabMath.FormatResearchTime(ResearchLabMath.GetResearchDaysToComplete(researchManager, rd));
            }

            ApplyResearchCategoryTint(__instance, rd);
        }
        catch (Exception ex)
        {
            PluginLogWarning("ResearchTreeElement.UpdateLook patch failed: " + ex.Message);
        }
    }

    [HarmonyPatch(typeof(ResearchTreeElementLeftPanel), "SetData")]
    [HarmonyPostfix]
    private static void ResearchTreeElementLeftPanelSetDataPostfix(ResearchTreeElementLeftPanel __instance)
    {
        try
        {
            var rd = __instance?.ResearchDefinition;
            var category = ResearchLabMath.GetResearchCategory(rd);
            if (category == null)
                return;

            var title = LeftPanelTitleResearchField?.GetValue(__instance) as TextMeshProUGUI;
            if (title != null)
                title.color = category.Color;

            var pointText = LeftPanelPointTextField?.GetValue(__instance) as TextMeshProUGUI;
            if (pointText != null)
                pointText.color = category.Color;
        }
        catch (Exception ex)
        {
            PluginLogWarning("ResearchTreeElementLeftPanel.SetData patch failed: " + ex.Message);
        }
    }

    [HarmonyPatch(typeof(ButtonsOpenWindows), "UpdateTextResearchTooltip")]
    [HarmonyPrefix]
    private static bool ButtonsOpenWindowsUpdateTextResearchTooltipPrefix(ref string __result)
    {
        var slot = MonoBehaviourSingleton<GameManager>.Instance.Player.ResearchManager.Slot1;
        if (slot == null || slot.ResearchDefinition == null)
        {
            __result = Language.LEManager.Get("ButtonsOpenWindows.ResearchTooltipMainUIEmpty");
            return false;
        }

        var hoursRemaining = (1f - slot.Progress01) * slot.ResearchDefinition.GetWorkHourToComplete(MonoBehaviourSingleton<GameManager>.Instance.Player);
        var timeRemaining = ResearchLabMath.FormatResearchTime(hoursRemaining / ResearchLabMath.HoursPerDay);
        __result = Language.LEManager.Get("ButtonsOpenWindows.ResearchTooltipMainUI").MyFormat(slot.ResearchDefinition.GetText(), timeRemaining);
        return false;
    }

    [HarmonyPatch(typeof(FacilityBaseDescriptor), "GetFacilityStats")]
    [HarmonyPostfix]
    private static void FacilityBaseDescriptorGetFacilityStatsPostfix(FacilityBaseDescriptor __instance, ref List<ValueTuple<string, string>> __result, Facility facility)
    {
        if (__result == null || !(__instance is GroundFacilityDescriptor groundFacility) || groundFacility.labData == null)
            return;

        if (!IsResearchLabDescriptor(groundFacility, facility))
        {
            RemoveLabStats(__result);
            return;
        }

        var entry = ResearchLabMath.GetFacilityEntry(groundFacility);
        var rate = entry?.BonusPercent ?? (double)groundFacility.labData.bonusToResearchInPerHour;
        if (facility != null)
            rate *= facility.FinalEfficiencyBasedOnPowerDeliveryAndWorkforceAllocationAndResources * facility.Enabled;

        var ids = entry?.ResearchIds ?? groundFacility.labData.idToBonus ?? Array.Empty<string>();
        var hasConfiguredSubTypes = entry?.ResearchSubTypes != null && entry.ResearchSubTypes.Length > 0;
        var isUniversal = (ids.Length == 0 && !hasConfiguredSubTypes && string.IsNullOrWhiteSpace(groundFacility.labData.idResearchSubType)) || Array.IndexOf(ids, "All") >= 0;
        var output = entry?.UniversalResearchPointPerMonth ?? ResearchLabMath.UniversalLabResearchPointPerMonth;
        if (facility != null)
            output *= facility.FinalEfficiencyBasedOnPowerDeliveryAndWorkforceAllocationAndResources * facility.Enabled;
        __result.Add(new ValueTuple<string, string>("Research output", "+" + ((float)output).ToPostfixString("{0}{1}", gray: false, intFormat: true) + " RP/month"));
        if (!isUniversal)
            __result.Add(new ValueTuple<string, string>("Research bonus", "+" + ((float)rate).ToString("0.#") + "%"));
        __result.Add(new ValueTuple<string, string>("Research category", ResearchLabMath.GetLabCategoryText(groundFacility)));
    }

    private static string BuildResearchTooltip(ResearchManager researchManager, ResearchDefinition current)
    {
        var text = "Base: " + ResearchLabMath.FormatRate(ResearchLabMath.GetBaseResearchPointPerHour(researchManager));
        text += Environment.NewLine + "Universal labs: +" + ResearchLabMath.FormatRate(ResearchLabMath.GetUniversalLabResearchPointPerHour(researchManager));
        text += Environment.NewLine + "Category bonus: " + ResearchLabMath.FormatPercentBonus(ResearchLabMath.GetResearchBonusPercent(researchManager, current), ResearchLabMath.GetRawResearchBonusPercent(researchManager, current));
        text += Environment.NewLine + "Total: " + ResearchLabMath.FormatRate(ResearchLabMath.GetResearchPointPerHour(researchManager, current));

        var allResearch = new List<ResearchDefinition>();
        researchManager.GetAllRequirementsResearch(current, allResearch);
        allResearch.Insert(0, current);

        foreach (var rd in allResearch)
        {
            if (MonoBehaviourSingleton<GameManager>.Instance.Player.ResearchManager.IsComplete(rd))
                continue;

            text += Environment.NewLine + "<indent=2.2>+</indent> ";
            if (rd.isLocked || rd.isLockedForUI)
            {
                text += "inf " + rd.GetText();
                continue;
            }

            var workMonths = ResearchLabMath.FormatWorkMonths(ResearchLabMath.GetResearchWorkMonths(rd)) + " RP";
            var time = ResearchLabMath.FormatResearchTime(ResearchLabMath.GetResearchDaysToComplete(researchManager, rd));
            text += workMonths + " (" + time + ") " + rd.GetText();
        }

        return "<font=\"Oxanium-Medium SDF\">" + text + "</font>";
    }

    private static void ApplyResearchCategoryTint(ResearchTreeElement element, ResearchDefinition rd)
    {
        var category = ResearchLabMath.GetResearchCategory(rd);
        if (category == null)
            return;

        var lockImage = TreeElementLockImageField?.GetValue(element) as UnityEngine.UI.Image;
        if (lockImage != null && lockImage.gameObject.activeSelf)
            return;

        var title = TreeElementTitleField?.GetValue(element) as TextMeshProUGUI;
        if (title != null)
            title.color = category.Color;

        var pointText = TreeElementPointTextField?.GetValue(element) as TextMeshProUGUI;
        if (pointText != null)
            pointText.color = category.Color;

        var iconWithProgressBar = TreeElementIconProgressField?.GetValue(element) as IconWithProgressBar;
        if (iconWithProgressBar != null)
        {
            if (iconWithProgressBar.iconDarkGray != null)
                iconWithProgressBar.iconDarkGray.color = category.Color;
            if (iconWithProgressBar.icon != null)
                iconWithProgressBar.icon.color = Color.Lerp(Color.white, category.Color, 0.35f);
        }

        if (element.titleFrame != null)
        {
            var frameImage = element.titleFrame.GetComponent<UnityEngine.UI.Image>();
            if (frameImage != null)
            {
                var color = category.Color;
                color.a = 180;
                frameImage.color = color;
            }
        }
    }

    private static bool IsResearchLabDescriptor(GroundFacilityDescriptor groundFacility, Facility facility)
    {
        if (facility is Game.ObjectInfoDataScripts.CustomFacilitiesAndModules.LabFacility)
            return true;

        if (groundFacility.FacilityItemClass == typeof(Game.ObjectInfoDataScripts.CustomFacilitiesAndModules.LabFacility))
            return true;

        return groundFacility.specialAbilityFacilityNew.HasFlag(ESpecialAbilityFacilityNew.Lab);
    }

    private static void RemoveLabStats(List<ValueTuple<string, string>> stats)
    {
        stats.RemoveAll(item =>
            item.Item1 == "Research bonus"
            || item.Item1 == "Research output"
            || item.Item1 == "Research scope"
            || item.Item1 == "Research category");
    }

    private static void PluginLogWarning(string message)
    {
        BepInEx.Logging.Logger.CreateLogSource("ResearchLabMod").LogWarning(message);
    }
}
