using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection.Emit;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using LLBML.Bundles;
using LLBML.Players;
using LLBML.States;
using LLBML.Utils;
using LLGUI;
using LLHandlers;
using LLScreen;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = System.Object;

namespace HoldToPause;

[BepInPlugin(GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
[BepInDependency("fr.glomzubuk.plugins.llb.llbml", BepInDependency.DependencyFlags.HardDependency)]
[BepInDependency("no.mrgentle.plugins.llb.modmenu", BepInDependency.DependencyFlags.SoftDependency)]
public class Plugin : BaseUnityPlugin
{
    public const string GUID = "avgduck.plugins.llb.holdtopause";
    internal static Plugin Instance { get; private set; }
    internal static ManualLogSource LogGlobal { get; private set; }
    
    internal static ConfigEntry<int> PauseHoldTime { get; private set; }
    internal static ConfigEntry<int> PauseDisplayMode { get; private set; }

    private static Vector2[] pauseIndicatorPositions =
    [
        Vector2.zero,
        new Vector2(-616f, -334f),
        new Vector2(616f, -334f)
    ];

    private float[] pauseTimers;
    private bool pauseAllowed;
    private int pausePlayer;
    private Sprite spritePauseProgress;
    private Image imgPauseProgress;
    private Sprite spritePauseIcon;
    private Image imgPauseIcon;

    private void Awake()
    {
        Instance = this;
        LogGlobal = Logger;

        PauseHoldTime = Instance.Config.Bind<int>("Settings", "PauseHoldTime", 3000, "The amount of time a player needs to hold the pause button to pause the game, in milliseconds (1000ms = 1s). Must be greater than 0");
        PauseDisplayMode = Instance.Config.Bind<int>("Settings", "PauseDisplayMode", 1, new ConfigDescription("Display mode for the pause progress indicator. 1 = off, 2 = bottom left, 3 = bottom right", new AcceptableValueRange<int>(0, pauseIndicatorPositions.Length - 1)));
        ModDependenciesUtils.RegisterToModMenu(Instance.Info, [
            "Forces the pause button to be held down to prevent accidental pausing in tournament settings.",
            "Pause hold time is specified in milliseconds (1000ms = 1s) and must be greater than 0.",
            "Pause display mode: 0 = off, 1 = bottom left, 2 = bottom right"
        ]);

        pausePlayer = -1;
        pauseTimers = new float[4];
        Harmony harmony = Harmony.CreateAndPatchAll(typeof(Plugin));

        spritePauseProgress = Sprite.Create(CreateCircleTexture(Color.white, new Color(0.08f, 0.08f, 0.08f), 256, 256, 128, 128, 96, 128, 4), new Rect(0, 0, 256, 256), new Vector2(0.5f, 0.5f));

        DirectoryInfo assetsDirectory = new DirectoryInfo(Path.Combine(Path.GetDirectoryName(Info.Location), "assets"));
        FileInfo pauseIconFile = assetsDirectory.GetFiles().First();
        byte[] fileData = File.ReadAllBytes(pauseIconFile.FullName);
        Texture2D texPauseIcon = new Texture2D(122, 132);
        texPauseIcon.LoadImage(fileData);
        spritePauseIcon = Sprite.Create(texPauseIcon, new Rect(0, 0, 122, 132), new Vector2(0.5f, 0.5f));
    }
    
    private static Texture2D CreateCircleTexture(Color mainColor, Color borderColor, int width, int height, int centerX, int centerY, int innerRadius, int outerRadius, int borderWidth)
    {
        Texture2D tex = new Texture2D(width, height);

        float rSquaredInner = innerRadius * innerRadius;
        float rSquaredInnerBorder = (innerRadius + borderWidth) * (innerRadius + borderWidth);
        float rSquaredOuter = outerRadius * outerRadius;
        float rSquaredOuterBorder = (outerRadius - borderWidth) * (outerRadius - borderWidth);

        for (int x = centerX - outerRadius; x <= centerX + outerRadius; x++)
        {
            for (int y = centerY - outerRadius; y <= centerY + outerRadius; y++)
            {
                float squareDist = (centerX - x) * (centerX - x) + (centerY - y) * (centerY - y);
                if ((squareDist > rSquaredInner && squareDist < rSquaredInnerBorder) || (squareDist > rSquaredOuterBorder && squareDist < rSquaredOuter))
                {
                    tex.SetPixel(x, y, borderColor);
                }
                else if (squareDist > rSquaredInnerBorder && squareDist < rSquaredOuterBorder)
                {
                    tex.SetPixel(x, y, mainColor);
                }
                else
                {
                    tex.SetPixel(x, y, Color.clear);
                }
            }
        }

        tex.Apply();
        
        return tex;
    }

    private void ResetPauseTimers()
    {
        for (int playerNr = 0; playerNr < 4; playerNr++)
        {
            pauseTimers[playerNr] = 0f;
        }
    }

    private static string PrintArray<T>(T[] arr)
    {
        string s = "[";

        for (int i = 0; i < arr.Length; i++)
        {
            if (i != 0) s += ", ";
            s += arr[i].ToString();
        }
        
        s += "]";
        return s;
    }
    
    [HarmonyPatch(typeof(ScreenGameHud), nameof(ScreenGameHud.OnOpen))]
    [HarmonyPostfix]
    private static void ScreenGameHud_OnOpen_Postfix(ScreenGameHud __instance)
    {
        Instance.ResetPauseTimers();

        Instance.imgPauseProgress = new GameObject("imgPauseProgress").AddComponent<Image>();
        Instance.imgPauseProgress.sprite = Instance.spritePauseProgress;
        Instance.imgPauseProgress.color = new Color(1f, 1f, 1f, 1f);
        Instance.imgPauseProgress.type = Image.Type.Filled;
        Instance.imgPauseProgress.fillMethod = Image.FillMethod.Radial360;
        Instance.imgPauseProgress.fillClockwise = true;
        Instance.imgPauseProgress.fillOrigin = (int)Image.Origin360.Top;
        Instance.imgPauseProgress.rectTransform.SetParent(__instance.transform, false);
        Instance.imgPauseProgress.rectTransform.localPosition = pauseIndicatorPositions[PauseDisplayMode.Value];
        Instance.imgPauseProgress.rectTransform.localScale = new Vector2(0.3f, 0.3f);

        Instance.imgPauseIcon = new GameObject("imgPauseIcon").AddComponent<Image>();
        Instance.imgPauseIcon.sprite = Instance.spritePauseIcon;
        Instance.imgPauseIcon.color = new Color(1f, 1f, 1f, 1f);
        Instance.imgPauseIcon.rectTransform.SetParent(Instance.imgPauseProgress.rectTransform, false);
        Instance.imgPauseIcon.rectTransform.localPosition = new Vector2(0f, 0f);
        Instance.imgPauseIcon.rectTransform.localScale = new Vector2(0.5f, 0.5f);
        
        Instance.imgPauseProgress.fillAmount = 0f;
        Instance.imgPauseIcon.enabled = false;
    }

    // void GameStatesGame::UpdateState
    [HarmonyPatch(typeof(OGONAGCFDPK), nameof(OGONAGCFDPK.UpdateState))]
    [HarmonyPrefix]
    private static void GameStatesGame_UpdateState_Prefix(OGONAGCFDPK __instance)
    {
        Instance.pauseAllowed = __instance.pauseAllowed;
    }

    [HarmonyPatch(typeof(ScreenGameHud), nameof(ScreenGameHud.DoUpdate))]
    [HarmonyTranspiler]
    private static IEnumerable<CodeInstruction> ScreenGameHud_DoUpdate_Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator il)
    {
        CodeMatcher cm = new CodeMatcher(instructions, il);
        cm.End(); // start at the end of the method
        /*
         * match
         *  for (int j = 0; j < 4; j++) {
         *      Player player = Player.Get(j);
         *      if (player.controller.GetButtonDown(InputAction.PAUSE)) {
         *          MATCH FROM HERE
         *          GameStates.Send(Msg.GAME_PAUSE, j, -1);
         *          break;
         *          TO HERE
         *      }
         *  }
         */
        cm.MatchBack(false,
            new CodeMatch(OpCodes.Ldloc_S), // load value of var player
            new CodeMatch(OpCodes.Ldflda), // get player.controller
            new CodeMatch(OpCodes.Ldsfld), // load InputAction.PAUSE
            new CodeMatch(OpCodes.Call), // call LLHandlers.Controller::GetButtonDown
            new CodeMatch(OpCodes.Brfalse), // break if false
            
            new CodeMatch(OpCodes.Ldc_I4_S), // push value of 102 (Msg.GAME_PAUSE)
            new CodeMatch(OpCodes.Ldloc_S), // load value of var j
            new CodeMatch(OpCodes.Ldc_I4_M1), // push value of -1
            new CodeMatch(OpCodes.Call), // call DNPFJHMAIBP::GKBNNFEAJGO (GameStates::Send)
            new CodeMatch(OpCodes.Br) // break
        );
        cm.Advance(1); // skip ldloc.s V_12, so we can use the player as an arg for our delegate
        cm.RemoveInstructions(9); // remove everything else in the for loop
        cm.Insert(
            Transpilers.EmitDelegate<Action<ALDOKEMAOMB>>((p) =>
            {
                Player player = (Player)p;
                if (Instance.pauseAllowed && player.controller.GetButton(InputAction.PAUSE))
                {
                    Instance.pauseTimers[player.nr] += Time.deltaTime;
                }
                else
                {
                    Instance.pauseTimers[player.nr] = 0f;
                }
            })
        );
        return cm.InstructionEnumeration();
    }

    [HarmonyPatch(typeof(ScreenGameHud), nameof(ScreenGameHud.DoUpdate))]
    [HarmonyPostfix]
    private static void ScreenGameHud_DoUpdate_Postfix()
    {
        if (!Instance.pauseAllowed) return;
        
        bool shouldPause = false;
        int maxPausePlayer = -1;
        float maxPauseTime = 0f;
        for (int playerNr = 0; playerNr < 4; playerNr++)
        {
            if (Instance.pauseTimers[playerNr] >= PauseHoldTime.Value / 1000f)
            {
                Instance.pausePlayer = playerNr;
                shouldPause = true;
            }

            if (Instance.pauseTimers[playerNr] > maxPauseTime)
            {
                maxPausePlayer = playerNr;
                maxPauseTime = Instance.pauseTimers[playerNr];
            }
        }

        Instance.imgPauseProgress.rectTransform.localPosition = pauseIndicatorPositions[PauseDisplayMode.Value];
        if (Instance.pausePlayer == -1 && maxPauseTime > 0f && PauseDisplayMode.Value != 0)
        {
            Instance.imgPauseProgress.fillAmount = maxPauseTime / (PauseHoldTime.Value / 1000f);
            Instance.imgPauseIcon.enabled = true;
        }
        else
        {
            Instance.imgPauseProgress.fillAmount = 0f;
            Instance.imgPauseIcon.enabled = false;
        }
        
        if (!shouldPause) return;
        
        Instance.ResetPauseTimers();
        GameStates.Send(Msg.GAME_PAUSE, Instance.pausePlayer, -1);
    }

    [HarmonyPatch(typeof(ScreenGamePause), nameof(ScreenGamePause.OnOpen))]
    [HarmonyPostfix]
    private static void ScreenGamePause_OnOpen_Postfix(ScreenGamePause __instance)
    {
        TMP_Text lbPausePlayer = GameObject.Instantiate(__instance.btResume, __instance.transform).GetComponent<TMP_Text>();
        lbPausePlayer.gameObject.name = "lbPausePlayer";
        GameObject.Destroy(lbPausePlayer.GetComponent<LLButton>());
        lbPausePlayer.transform.localPosition = new Vector2(0f, 310f);
        lbPausePlayer.transform.localScale = new Vector2(1f, 1f);
        lbPausePlayer.color = Color.white;
        lbPausePlayer.fontSize = 28;
        TextHandler.SetText(lbPausePlayer, Instance.pausePlayer != -1 ? $"P{Instance.pausePlayer+1} pause" : "");
        Instance.pausePlayer = -1;
    }
}