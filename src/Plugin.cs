using BepInEx;
using BepInEx.Logging;

namespace HoldToPause;

[BepInPlugin(GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
public class Plugin : BaseUnityPlugin
{
    public const string GUID = "avgduck.plugins.llb.holdtopause";
    internal static Plugin Instance { get; private set; }
    internal static ManualLogSource LogGlobal { get; private set; }

    private void Awake()
    {
        Instance = this;
        LogGlobal = Logger;
    }
}