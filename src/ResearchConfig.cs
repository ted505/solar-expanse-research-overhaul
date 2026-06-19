using System;
using System.Collections.Generic;
using System.IO;
using BepInEx.Logging;
using ResearchLabMod.Logic;
using UnityEngine;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace ResearchLabMod;

internal sealed class FacilityResearchEntry
{
    public double? UniversalResearchPointPerMonth { get; set; }
    public double? BonusPercent { get; set; }
    public string[] ResearchIds { get; set; }
    public string[] ResearchSubTypes { get; set; }
    public bool BlockOnEarth { get; set; }
}

internal sealed class ResearchCategoryConfig
{
    public string Name { get; set; }
    public string HexColor { get; set; }
    public string[] SubTypes { get; set; }
}

internal sealed class ResearchConfig
{
    public double? BaseResearchPointPerMonth { get; set; }
    public double UniversalLabResearchPointPerMonth { get; set; } = 20.0;
    public double ResearchBonusDiminishingStartPercent { get; set; } = 50.0;
    public double ResearchBonusSoftCapPercent { get; set; } = 100.0;

    public List<ResearchCategoryConfig> Categories { get; set; } = new List<ResearchCategoryConfig>();
    public Dictionary<string, FacilityResearchEntry> Facilities { get; set; } = new Dictionary<string, FacilityResearchEntry>(StringComparer.Ordinal);

    [YamlIgnore]
    internal HashSet<string> EarthBlockedFacilityIds { get; private set; } = new HashSet<string>(StringComparer.Ordinal);

    [YamlIgnore]
    internal ResearchCategoryInfo[] ResearchCategories { get; private set; }

    internal bool TryGetFacility(string facilityId, out FacilityResearchEntry entry)
    {
        entry = null;
        return !string.IsNullOrWhiteSpace(facilityId) && Facilities.TryGetValue(facilityId, out entry);
    }

    internal bool IsBlockedOnEarth(string facilityId)
        => EarthBlockedFacilityIds.Contains(facilityId);

    private void BuildLookups()
    {
        if (Categories == null || Categories.Count == 0)
            Categories = DefaultCategories();
        if (Facilities == null)
            Facilities = new Dictionary<string, FacilityResearchEntry>(StringComparer.Ordinal);
        else
            Facilities = new Dictionary<string, FacilityResearchEntry>(Facilities, StringComparer.Ordinal);

        EarthBlockedFacilityIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var kvp in Facilities)
        {
            if (kvp.Value != null && kvp.Value.BlockOnEarth)
                EarthBlockedFacilityIds.Add(kvp.Key);
        }

        var categories = new List<ResearchCategoryInfo>();
        foreach (var category in Categories)
        {
            if (category == null || string.IsNullOrWhiteSpace(category.Name))
                continue;

            var hexColor = string.IsNullOrWhiteSpace(category.HexColor) ? "FFFFFF" : category.HexColor.Trim().TrimStart('#');
            if (!ColorUtility.TryParseHtmlString("#" + hexColor, out var color))
                color = Color.white;

            categories.Add(new ResearchCategoryInfo(
                category.Name,
                hexColor,
                (Color32)color,
                category.SubTypes ?? Array.Empty<string>()));
        }

        ResearchCategories = categories.Count > 0
            ? categories.ToArray()
            : DefaultCategoryInfos();
    }

    internal static ResearchConfig Load(string path, ManualLogSource log)
    {
        if (!File.Exists(path))
        {
            log.LogWarning("[ResearchConfig] Not found at " + path + " - using defaults.");
            return CreateDefault();
        }

        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();

        var yaml = File.ReadAllText(path);
        var config = deserializer.Deserialize<ResearchConfig>(yaml);
        if (config == null)
        {
            log.LogWarning("[ResearchConfig] YAML parsed to null - using defaults.");
            return CreateDefault();
        }

        config.BuildLookups();
        log.LogInfo("[ResearchConfig] Loaded: baseResearchPointPerMonth="
            + (config.BaseResearchPointPerMonth.HasValue ? config.BaseResearchPointPerMonth.Value.ToString() : "game")
            + ", universalLabResearchPointPerMonth="
            + config.UniversalLabResearchPointPerMonth
            + ", categories="
            + config.ResearchCategories.Length
            + ", facilities="
            + config.Facilities.Count
            + ".");
        return config;
    }

    internal static ResearchConfig CreateDefault()
    {
        var config = new ResearchConfig
        {
            Categories = DefaultCategories(),
            Facilities = DefaultFacilities(),
        };
        config.BuildLookups();
        return config;
    }

    private static List<ResearchCategoryConfig> DefaultCategories()
    {
        return new List<ResearchCategoryConfig>
        {
            new ResearchCategoryConfig { Name = "Life Science", HexColor = "48F0A8", SubTypes = new[] { "SubBranch_Terraforming", "SubBranch_Exploration", "SubBranch_Agriculture", "SubBranch_Colonization", "SubBranch_LifeSupport", "SubBranch_Biotech" } },
            new ResearchCategoryConfig { Name = "Industrial", HexColor = "F4B24A", SubTypes = new[] { "SubBranch_Mining", "SubBranch_Material", "SubBranch_Computing", "SubBranch_Chemical" } },
            new ResearchCategoryConfig { Name = "Spaceflight", HexColor = "6CB6FF", SubTypes = new[] { "SubBranch_LaunchVehicle", "SubBranch_Spacecraft", "SubBranch_LaunchFacility", "SubBranch_Electroprop" } },
            new ResearchCategoryConfig { Name = "Power", HexColor = "FFD84D", SubTypes = new[] { "SubBranch_Fusion", "SubBranch_Electromagnetism", "SubBranch_Nuclear", "SubBranch_Power" } },
        };
    }

    private static ResearchCategoryInfo[] DefaultCategoryInfos()
    {
        var config = new ResearchConfig { Categories = DefaultCategories() };
        config.BuildLookups();
        return config.ResearchCategories;
    }

    private static Dictionary<string, FacilityResearchEntry> DefaultFacilities()
    {
        return new Dictionary<string, FacilityResearchEntry>(StringComparer.Ordinal)
        {
            { "build_lab", new FacilityResearchEntry { UniversalResearchPointPerMonth = 20.0 } },
            { "rf_research_institute", new FacilityResearchEntry { UniversalResearchPointPerMonth = 120.0 } },
            { "rf_research_complex", new FacilityResearchEntry { UniversalResearchPointPerMonth = 1000.0 } },
            { "rf_life_science_facility", new FacilityResearchEntry { BlockOnEarth = true } },
            { "rf_industrial_research_facility", new FacilityResearchEntry { BlockOnEarth = true } },
            { "rf_spaceflight_research_facility", new FacilityResearchEntry { BlockOnEarth = true } },
            { "rf_power_research_facility", new FacilityResearchEntry { BlockOnEarth = true } },
        };
    }
}
