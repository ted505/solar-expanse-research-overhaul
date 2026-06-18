using BepInEx;
using HarmonyLib;

namespace ResearchLabMod;

[BepInPlugin("com.parft.solarexpanse.researchlabmod", "Research Lab Mod", "0.1.0")]
public class Plugin : BaseUnityPlugin
{
    private void Awake()
    {
        Harmony.CreateAndPatchAll(typeof(Plugin).Assembly, "com.parft.solarexpanse.researchlabmod");
        Logger.LogInfo("Research Lab Mod loaded.");
    }
}
