using System;
using System.Collections.Generic;
using System.Reflection;
using Data.ScriptableObject;
using Extensions;
using Game;
using Game.CompanyScripts;
using Game.Info;
using Game.ObjectInfoDataScripts;
using Game.ObjectInfoDataScripts.CustomFacilitiesAndModules;
using HarmonyLib;
using Language;
using Manager;
using ScriptableObjectScripts;
using UnityEngine;

namespace ResearchLabMod.Logic;

internal sealed class ResearchCategoryInfo
{
    public readonly string Name;
    public readonly string HexColor;
    public readonly Color32 Color;
    public readonly string[] SubTypes;

    public ResearchCategoryInfo(string name, string hexColor, Color32 color, params string[] subTypes)
    {
        Name = name;
        HexColor = hexColor;
        Color = color;
        SubTypes = subTypes;
    }
}

internal static class ResearchLabMath
{
    public const float HoursPerDay = 24f;
    public const float DaysPerMonth = 30f;
    public const float HoursPerMonth = 720f;
    public const float UniversalLabResearchPointPerMonth = 100f;
    public const float ResearchBonusDiminishingStartPercent = 50f;
    public const float ResearchBonusSoftCapPercent = 100f;

    private static readonly FieldInfo ResearchPoint1PerHourField = AccessTools.Field(typeof(ResearchManager), "researchPoint1PerHour");
    private static readonly FieldInfo BonusFromObservatoryField = AccessTools.Field(typeof(ResearchManager), "bonusFromObservatory");
    public static readonly ResearchCategoryInfo[] ResearchCategories =
    {
        new ResearchCategoryInfo("Life Science", "48F0A8", new Color32(72, 240, 168, 255), "SubBranch_Terraforming", "SubBranch_Exploration", "SubBranch_Agriculture", "SubBranch_Colonization", "SubBranch_LifeSupport", "SubBranch_Biotech"),
        new ResearchCategoryInfo("Industrial", "F4B24A", new Color32(244, 178, 74, 255), "SubBranch_Mining", "SubBranch_Material", "SubBranch_Computing", "SubBranch_Chemical"),
        new ResearchCategoryInfo("Spaceflight", "6CB6FF", new Color32(108, 182, 255, 255), "SubBranch_LaunchVehicle", "SubBranch_Spacecraft", "SubBranch_LaunchFacility", "SubBranch_Electroprop"),
        new ResearchCategoryInfo("Power", "FFD84D", new Color32(255, 216, 77, 255), "SubBranch_Fusion", "SubBranch_Electromagnetism", "SubBranch_Nuclear", "SubBranch_Power"),
    };

    public static float GetBaseResearchPointPerHour(ResearchManager researchManager)
    {
        var company = GetCompany(researchManager);
        var rate = ResearchPoint1PerHourField != null ? (float)(int)ResearchPoint1PerHourField.GetValue(researchManager) : 100f;
        var bonus = company != null ? company.BonusController.GetBonus(EBonus.ResearchProduction) : 1f;
        bonus = Mathf.Clamp(bonus, 0.1f, 1f);
        rate /= bonus;
        if (GetBonusFromObservatory(researchManager) > 0f)
            rate *= 2f;
        return rate;
    }

    public static float GetLabResearchPointPerHour(ResearchManager researchManager, ResearchDefinition rd)
    {
        return GetUniversalLabResearchPointPerHour(researchManager);
    }

    public static float GetResearchPointPerHour(ResearchManager researchManager, ResearchDefinition rd)
    {
        var sharedRate = GetSharedResearchPointPerHour(researchManager);
        var rate = sharedRate * (1f + GetResearchBonusPercent(researchManager, rd) / 100f);
        if (DevConsoleCommands.speedResearchMultiplayer.HasValue)
            rate *= DevConsoleCommands.speedResearchMultiplayer.Value;
        return rate;
    }

    public static float GetSharedResearchPointPerHour(ResearchManager researchManager)
    {
        return GetBaseResearchPointPerHour(researchManager) + GetUniversalLabResearchPointPerHour(researchManager);
    }

    public static float GetUniversalLabResearchPointPerHour(ResearchManager researchManager)
    {
        var company = GetCompany(researchManager);
        if (company == null)
            return 0f;

        RepairLabFacilityCache(company);
        var total = 0.0;
        foreach (var lab in company.BonusController.labFacility)
            total += GetUniversalFlatLabOutput(lab);
        return (float)total;
    }

    public static float GetCategoryResearchPointPerHour(ResearchManager researchManager, ResearchCategoryInfo category)
    {
        var sharedRate = GetSharedResearchPointPerHour(researchManager);
        return sharedRate * (1f + GetCategoryBonusPercent(researchManager, category) / 100f);
    }

    public static float GetCategoryLabResearchPointPerHour(ResearchManager researchManager, ResearchCategoryInfo category)
    {
        return GetSharedResearchPointPerHour(researchManager) * GetCategoryBonusPercent(researchManager, category) / 100f;
    }

    public static float GetResearchBonusPercent(ResearchManager researchManager, ResearchDefinition rd)
    {
        return ApplyDiminishingResearchBonus(GetRawResearchBonusPercent(researchManager, rd));
    }

    public static float GetRawResearchBonusPercent(ResearchManager researchManager, ResearchDefinition rd)
    {
        var company = GetCompany(researchManager);
        if (company == null || rd == null)
            return 0f;

        RepairLabFacilityCache(company);
        var total = 0.0;
        foreach (var lab in company.BonusController.labFacility)
            total += GetLabBonusPercent(lab, rd);
        return (float)total;
    }

    public static float GetCategoryBonusPercent(ResearchManager researchManager, ResearchCategoryInfo category)
    {
        return ApplyDiminishingResearchBonus(GetRawCategoryBonusPercent(researchManager, category));
    }

    public static float GetRawCategoryBonusPercent(ResearchManager researchManager, ResearchCategoryInfo category)
    {
        var company = GetCompany(researchManager);
        if (company == null || category == null)
            return 0f;

        RepairLabFacilityCache(company);
        var total = 0.0;
        foreach (var lab in company.BonusController.labFacility)
            total += GetLabBonusPercentForCategory(lab, category);
        return (float)total;
    }

    public static float GetResearchDaysToComplete(ResearchManager researchManager, ResearchDefinition rd)
    {
        return rd.WorkHourToComplete / GetResearchPointPerHour(researchManager, rd) / HoursPerDay;
    }

    public static float GetResearchWorkMonths(ResearchDefinition rd)
    {
        return rd.WorkHourToComplete / HoursPerMonth;
    }

    public static float GetResearchWorkCost(ResearchManager researchManager, ResearchDefinition rd)
    {
        var company = GetCompany(researchManager);
        if (company == null || rd == null)
            return 0f;

        var allResearch = new List<ResearchDefinition>();
        researchManager.GetAllRequirementsResearch(rd, allResearch);
        allResearch.Add(rd);

        var total = 0f;
        foreach (var item in allResearch)
        {
            if (item.isLocked || item.isLockedForUI)
                total += float.PositiveInfinity;
            total += company.ResearchManager.IsComplete(item) ? 0f : GetResearchWorkMonths(item);
        }
        return total;
    }

    public static double GetLabBonusPercent(LabFacility lab, ResearchDefinition rd)
    {
        if (lab == null || rd == null || !lab.BuildingWorking)
            return 0.0;

        var groundFacility = lab.facilityDescriptor as GroundFacilityDescriptor;
        var labData = groundFacility?.labData;
        if (labData == null)
            return 0.0;

        var ids = labData.idToBonus ?? Array.Empty<string>();
        foreach (var id in ids)
        {
            if (rd.ID == id)
                return LabBonusPercentForMatchingLab(lab, labData);
        }

        if (MatchesResearchSubType(labData.idResearchSubType, rd))
            return LabBonusPercentForMatchingLab(lab, labData);

        return 0.0;
    }

    public static double GetFlatLabOutput(LabFacility lab, ResearchDefinition rd)
    {
        return GetUniversalFlatLabOutput(lab);
    }

    public static string FormatResearchTime(float days)
    {
        if (float.IsInfinity(days))
            return "inf";
        if (days >= DaysPerMonth * 2f)
            return Mathf.RoundToInt(days / DaysPerMonth) + " months";
        return Mathf.RoundToInt(days) + " " + LEManager.Get("PlanMissionWindow.Days");
    }

    public static string FormatWorkMonths(float months)
    {
        if (float.IsInfinity(months))
            return "inf";
        return months.ToPostfixString("{0}{1}", gray: false, intFormat: true);
    }

    public static string FormatRate(float workPerHour)
    {
        return FormatRpPerMonth(workPerHour);
    }

    public static string FormatPointsPerHour(float pointsPerHour)
    {
        return FormatRpPerMonth(pointsPerHour);
    }

    public static string FormatRpPerMonth(float pointsPerHour)
    {
        return pointsPerHour.ToPostfixString("{0}{1}", gray: false, intFormat: true) + " RP/month";
    }

    public static string FormatPercentBonus(float effectivePercent, float rawPercent = -1f)
    {
        var text = "+" + effectivePercent.ToString("0.#") + "%";
        if (rawPercent >= 0f && rawPercent - effectivePercent > 0.05f)
            text += " (" + rawPercent.ToString("0.#") + "% raw)";
        return text;
    }

    public static ResearchCategoryInfo GetResearchCategory(ResearchDefinition rd)
    {
        var subTypeId = rd?.ResearchSubType?.ID;
        if (string.IsNullOrWhiteSpace(subTypeId))
            return null;

        foreach (var category in ResearchCategories)
        {
            if (CategoryContainsSubType(category, subTypeId))
                return category;
        }
        return null;
    }

    public static string GetLabScopeText(LabData labData)
    {
        if (labData == null)
            return "";
        if (labData.idToBonus == null || labData.idToBonus.Length == 0 || Array.IndexOf(labData.idToBonus, "All") >= 0)
            return "All research";

        var labels = new List<string>();
        foreach (var id in labData.idToBonus)
        {
            var rd = SerializedMonoBehaviourSingleton<AllScriptableObjectManager>.Instance.AllResearchDefinition.GetByID(id);
            labels.Add(rd != null ? rd.Title : LEManager.Get(id + "_Title", id));
        }
        if (!string.IsNullOrWhiteSpace(labData.idResearchSubType))
        {
            foreach (var id in SplitResearchSubTypeIds(labData.idResearchSubType))
            {
                var subType = SerializedMonoBehaviourSingleton<AllScriptableObjectManager>.Instance.AllResearchDefinition.GetResearchSubTypeByID(id);
                labels.Add(subType != null ? LEManager.Get(subType.ID + "_Title", subType.ID) : id);
            }
        }
        return string.Join(", ", labels);
    }

    public static string GetLabCategoryText(LabData labData)
    {
        if (labData == null)
            return "";

        if ((labData.idToBonus == null || labData.idToBonus.Length == 0) && string.IsNullOrWhiteSpace(labData.idResearchSubType))
            return "All research";

        if (labData.idToBonus != null && Array.IndexOf(labData.idToBonus, "All") >= 0)
            return "All research";

        var labels = new List<string>();
        foreach (var category in ResearchCategories)
        {
            var matches = false;
            foreach (var id in SplitResearchSubTypeIds(labData.idResearchSubType))
            {
                if (CategoryContainsSubType(category, id))
                {
                    matches = true;
                    break;
                }
            }

            if (!matches && labData.idToBonus != null)
            {
                foreach (var id in labData.idToBonus)
                {
                    var rd = SerializedMonoBehaviourSingleton<AllScriptableObjectManager>.Instance.AllResearchDefinition.GetByID(id);
                    if (rd?.ResearchSubType != null && CategoryContainsSubType(category, rd.ResearchSubType.ID))
                    {
                        matches = true;
                        break;
                    }
                }
            }

            if (matches)
                labels.Add(category.Name);
        }

        return labels.Count > 0 ? string.Join(", ", labels) : GetLabScopeText(labData);
    }

    private static bool MatchesResearchSubType(string researchSubTypeIds, ResearchDefinition rd)
    {
        if (rd?.ResearchSubType == null || string.IsNullOrWhiteSpace(researchSubTypeIds))
            return false;

        foreach (var id in SplitResearchSubTypeIds(researchSubTypeIds))
        {
            if (id == rd.ResearchSubType.ID)
                return true;
        }
        return false;
    }

    private static double GetLabBonusPercentForCategory(LabFacility lab, ResearchCategoryInfo category)
    {
        if (lab == null || category == null || !lab.BuildingWorking)
            return 0.0;

        var groundFacility = lab.facilityDescriptor as GroundFacilityDescriptor;
        var labData = groundFacility?.labData;
        if (labData == null)
            return 0.0;

        var ids = labData.idToBonus ?? Array.Empty<string>();
        foreach (var id in ids)
        {
            var rd = SerializedMonoBehaviourSingleton<AllScriptableObjectManager>.Instance.AllResearchDefinition.GetByID(id);
            if (rd?.ResearchSubType != null && CategoryContainsSubType(category, rd.ResearchSubType.ID))
                return LabBonusPercentForMatchingLab(lab, labData);
        }

        foreach (var id in SplitResearchSubTypeIds(labData.idResearchSubType))
        {
            if (CategoryContainsSubType(category, id))
                return LabBonusPercentForMatchingLab(lab, labData);
        }

        return 0.0;
    }

    private static bool CategoryContainsSubType(ResearchCategoryInfo category, string subTypeId)
    {
        if (category == null || string.IsNullOrWhiteSpace(subTypeId))
            return false;

        foreach (var id in category.SubTypes)
        {
            if (id == subTypeId)
                return true;
        }
        return false;
    }

    private static IEnumerable<string> SplitResearchSubTypeIds(string researchSubTypeIds)
    {
        if (string.IsNullOrWhiteSpace(researchSubTypeIds))
            yield break;

        foreach (var id in researchSubTypeIds.Split(new[] { ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = id.Trim();
            if (!string.IsNullOrWhiteSpace(trimmed))
                yield return trimmed;
        }
    }

    private static float ApplyDiminishingResearchBonus(float rawPercent)
    {
        if (rawPercent <= ResearchBonusDiminishingStartPercent)
            return rawPercent;

        var excess = rawPercent - ResearchBonusDiminishingStartPercent;
        var remainingRoom = ResearchBonusSoftCapPercent - ResearchBonusDiminishingStartPercent;
        return ResearchBonusDiminishingStartPercent + remainingRoom * excess / (excess + remainingRoom);
    }

    private static double LabBonusPercentForMatchingLab(LabFacility lab, LabData labData)
    {
        return labData.bonusToResearchInPerHour * lab.FinalEfficiencyBasedOnPowerDeliveryAndWorkforceAllocationAndResources * lab.Enabled;
    }

    private static double GetUniversalFlatLabOutput(LabFacility lab)
    {
        if (lab == null || !lab.BuildingWorking)
            return 0.0;

        var groundFacility = lab.facilityDescriptor as GroundFacilityDescriptor;
        var labData = groundFacility?.labData;
        if (labData == null)
            return 0.0;

        return UniversalLabResearchPointPerMonth * lab.FinalEfficiencyBasedOnPowerDeliveryAndWorkforceAllocationAndResources * lab.Enabled;
    }

    public static void RepairLabFacilityCache(Company company)
    {
        if (company?.BonusController?.labFacility == null || MonoBehaviourSingleton<ObjectInfoManager>.InstanceIsNull)
            return;

        var repaired = new List<LabFacility>();
        foreach (var objectInfo in MonoBehaviourSingleton<ObjectInfoManager>.Instance.allObjectInfos)
            AddLabsForObjectInfo(company, objectInfo, repaired);

        var cache = company.BonusController.labFacility;
        cache.Clear();
        cache.AddRange(repaired);
    }

    private static void AddLabsForObjectInfo(Company company, ObjectInfo objectInfo, List<LabFacility> labs)
    {
        if (objectInfo == null)
            return;

        ObjectInfoData data = null;
        try
        {
            data = objectInfo.GetObjectInfoData(company);
        }
        catch
        {
            return;
        }

        if (data?.ListFacility == null)
            return;

        foreach (var facility in data.ListFacility)
        {
            if (facility is LabFacility lab && lab.BuildProgress >= 1f && !labs.Contains(lab))
                labs.Add(lab);
        }
    }

    private static Company GetCompany(ResearchManager researchManager)
    {
        return Traverse.Create(researchManager).Field("company").GetValue<Company>();
    }

    private static float GetBonusFromObservatory(ResearchManager researchManager)
    {
        return BonusFromObservatoryField != null ? (float)BonusFromObservatoryField.GetValue(researchManager) : 0f;
    }
}
