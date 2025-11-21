using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using LLBML.Players;
using LLBML.States;
using LLBML.Utils;
using LLGUI;
using LLHandlers;
using LLScreen;
using TMPro;
using UnityEngine;
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

    private float[] pauseTimers;
    private bool pauseAllowed;
    private int pausePlayer;

    private void Awake()
    {
        Instance = this;
        LogGlobal = Logger;

        PauseHoldTime = Instance.Config.Bind<int>("Settings", "PauseHoldTime", 3000, "The amount of time a player needs to hold the pause button to pause the game, in milliseconds (1000ms = 1s). Must be greater than 0");
        ModDependenciesUtils.RegisterToModMenu(Instance.Info, [
            "Forces the pause button to be held down to prevent accidental pausing in tournament settings. Pause hold time is specified in milliseconds (1000ms = 1s) and must be greater than 0."
        ]);

        pausePlayer = -1;
        pauseTimers = new float[4];
        Harmony harmony = Harmony.CreateAndPatchAll(typeof(Plugin));
    }

    private void Update()
    {
        //if (GameStates.GetCurrent() != GameState.GAME || !pauseAllowed) return;
        LogGlobal.LogInfo($"pausePlayer {pausePlayer}, pauseTimers {PrintArray(pauseTimers)}");
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
    private static void ScreenGameHud_OnOpen_Postfix()
    {
        Instance.ResetPauseTimers();
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
        for (int playerNr = 0; playerNr < 4; playerNr++)
        {
            if (Instance.pauseTimers[playerNr] >= PauseHoldTime.Value / 1000f)
            {
                Instance.pausePlayer = playerNr;
                shouldPause = true;
                break;
            }
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