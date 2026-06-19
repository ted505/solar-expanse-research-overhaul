using Game;
using Game.UI.Windows.Windows.ResearchTree;
using Manager;
using ResearchLabMod.Logic;
using ScriptableObjectScripts;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ResearchLabMod.UI;

internal sealed class ResearchRateOverlay : MonoBehaviour
{
    private TextMeshProUGUI _label;
    private ResearchTree _researchTree;
    private ResearchDefinition _current;
    private float _nextRefresh;

    private void Awake()
    {
        _researchTree = GetComponent<ResearchTree>();
        Build();
    }

    private void OnEnable()
    {
        if (_researchTree != null)
            _researchTree.researchDefinitionChange += OnResearchDefinitionChange;
        Refresh(force: true);
    }

    private void OnDisable()
    {
        if (_researchTree != null)
            _researchTree.researchDefinitionChange -= OnResearchDefinitionChange;
    }

    private void Update()
    {
        if (Time.unscaledTime >= _nextRefresh)
            Refresh(force: false);
    }

    private void OnResearchDefinitionChange(ResearchDefinition rd)
    {
        _current = rd;
        Refresh(force: true);
    }

    private void Build()
    {
        if (_label != null)
            return;

        var root = new GameObject("ResearchLabMod_RateOverlay", typeof(RectTransform), typeof(Image));
        root.transform.SetParent(transform, false);

        var rootRt = root.GetComponent<RectTransform>();
        rootRt.anchorMin = new Vector2(0.5f, 1f);
        rootRt.anchorMax = new Vector2(0.5f, 1f);
        rootRt.pivot = new Vector2(0.5f, 1f);
        rootRt.anchoredPosition = new Vector2(-565f, -28f);
        rootRt.sizeDelta = new Vector2(540f, 44f);

        var bg = root.GetComponent<Image>();
        bg.color = new Color(0f, 0.08f, 0.075f, 0.82f);
        bg.raycastTarget = false;

        var labelGo = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
        labelGo.transform.SetParent(root.transform, false);
        var labelRt = labelGo.GetComponent<RectTransform>();
        labelRt.anchorMin = Vector2.zero;
        labelRt.anchorMax = Vector2.one;
        labelRt.offsetMin = new Vector2(8f, 4f);
        labelRt.offsetMax = new Vector2(-8f, -4f);

        _label = labelGo.GetComponent<TextMeshProUGUI>();
        _label.raycastTarget = false;
        _label.enableWordWrapping = false;
        _label.overflowMode = TextOverflowModes.Ellipsis;
        _label.alignment = TextAlignmentOptions.MidlineLeft;
        _label.fontSize = 10.5f;
        _label.lineSpacing = -20f;
        _label.color = new Color32(0, 255, 224, 255);

        var donor = GetComponentInChildren<TextMeshProUGUI>(true);
        if (donor?.font != null)
        {
            _label.font = donor.font;
            _label.fontSharedMaterial = donor.fontSharedMaterial;
        }
    }

    private void Refresh(bool force)
    {
        if (_label == null)
            Build();

        _nextRefresh = Time.unscaledTime + 0.5f;
        var player = MonoBehaviourSingleton<GameManager>.Instance?.Player;
        var researchManager = player?.ResearchManager;
        if (researchManager == null)
        {
            _label.text = "";
            return;
        }

        var rd = _current ?? researchManager.Slot1?.ResearchDefinition;
        if (rd == null)
        {
            _label.text = BuildCategoryText(researchManager, null);
            return;
        }

        _label.text = BuildCategoryText(researchManager, rd);
    }

    private static string BuildCategoryText(ResearchManager researchManager, ResearchDefinition selected)
    {
        var currentCategory = ResearchLabMath.GetResearchCategory(selected);
        var text = "<b>RATE</b> ";
        text += "Base " + ResearchLabMath.FormatPointsPerHour(ResearchLabMath.GetBaseResearchPointPerHour(researchManager));
        text += "  |  Labs " + ResearchLabMath.FormatPointsPerHour(ResearchLabMath.GetUniversalLabResearchPointPerHour(researchManager));
        text += "  |  Total " + ResearchLabMath.FormatPointsPerHour(ResearchLabMath.GetSharedResearchPointPerHour(researchManager));
        text += "\n";

        foreach (var category in ResearchLabMath.ResearchCategories)
        {
            var isCurrentCategory = currentCategory == category;
            var effectiveBonus = isCurrentCategory && selected != null
                ? ResearchLabMath.GetResearchBonusPercent(researchManager, selected)
                : ResearchLabMath.GetCategoryBonusPercent(researchManager, category);
            var rawBonus = isCurrentCategory && selected != null
                ? ResearchLabMath.GetRawResearchBonusPercent(researchManager, selected)
                : ResearchLabMath.GetRawCategoryBonusPercent(researchManager, category);
            var marker = isCurrentCategory ? "> " : "";
            text += "  <color=#" + category.HexColor + ">" + marker + category.Name + "</color> ";
            text += "<color=#9FEFE6>" + ResearchLabMath.FormatPercentBonus(effectiveBonus, rawBonus) + "</color>";
        }

        return text;
    }
}
