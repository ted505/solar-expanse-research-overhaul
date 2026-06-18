using System.IO;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;

namespace ResearchLabMod;

[BepInPlugin("com.parft.solarexpanse.researchlabmod", "Research Lab Mod", "0.1.0")]
public class Plugin : BaseUnityPlugin
{
    internal static ManualLogSource Log;
    internal static string PluginDir;
    internal static ResearchConfig ResearchOverhaulConfig;

    private void Awake()
    {
        Log = Logger;
        PluginDir = Path.GetDirectoryName(Info.Location);
        ResearchOverhaulConfig = ResearchConfig.Load(Path.Combine(PluginDir, "research-overhaul.yaml"), Log);
        Harmony.CreateAndPatchAll(typeof(Plugin).Assembly, "com.parft.solarexpanse.researchlabmod");
        Logger.LogInfo("Research Lab Mod loaded.");
    }
}
