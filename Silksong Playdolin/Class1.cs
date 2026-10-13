using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using System;
using System.Reflection;
using HutongGames.PlayMaker;
using System.Collections.Generic;
using BepInEx.Configuration;

[BepInPlugin("com.example.playdolin", "Playdolin Plugin", "1.1.1")]
public class PlaydolinPlugin : BaseUnityPlugin
{
    private ConfigEntry<bool> diagnosticEnabled;
    private void Awake()
    {
        Logger.LogInfo("Playdolin Plugin is loaded!");

        diagnosticEnabled = Config.Bind("Diagnostics", "Enabled", false, "Enable the F8 FSM log dump for troubleshooting.");
        gameObject.AddComponent<NeedolinAudioTest>();
    }

    private void Update()
    {
        if (diagnosticEnabled.Value && Input.GetKeyDown(KeyCode.F8))
        {
            try { DumpNeedolinFsms(); }
            catch (Exception exception) { Logger.LogError(exception); }
        }
    }

    private void DumpNeedolinFsms()
    {
        Logger.LogInfo("[NeedolinDump] BEGIN");
        int matches = 0;
        // Include inactive scene objects, but exclude prefab/assets not in a scene.
        foreach (PlayMakerFSM fsm in Resources.FindObjectsOfTypeAll<PlayMakerFSM>())
        {
            if (!fsm.gameObject.scene.IsValid() || fsm.Fsm == null || fsm.Fsm.States == null)
                continue;

            bool related = ContainsNeedolin(fsm.FsmName);
            foreach (var state in fsm.Fsm.States)
            {
                related |= ContainsNeedolin(state.Name);
                if (state.Actions == null) continue;
                foreach (var action in state.Actions)
                    if (action != null) related |= ContainsNeedolin(action.GetType().Name);
            }
            if (!related) continue;

            matches++;
            Logger.LogInfo("[NeedolinDump] Object=" + ObjectPath(fsm.transform)
                + " | FSM=" + fsm.FsmName + " | Scene=" + fsm.gameObject.scene.name);
            DumpFsm(fsm.Fsm, new HashSet<Fsm>(), 0);
        }
        Logger.LogInfo("[NeedolinDump] END | Matching FSMs=" + matches);
    }

    private void DumpFsm(Fsm fsm, HashSet<Fsm> visited, int depth)
    {
        if (fsm == null || fsm.States == null || depth > 8 || !visited.Add(fsm)) return;
        Logger.LogInfo("[NeedolinDump] FSM depth=" + depth + " | Name=" + fsm.Name);
        // Parent FSMs can be huge; retain Needolin states, then dump child FSMs fully.
        foreach (var state in fsm.States)
            {
                bool related = depth > 0 || ContainsNeedolin(state.Name) || ContainsNeedolin(fsm.Name);
                if (state.Actions != null)
                    foreach (var action in state.Actions)
                        if (action != null) related |= ContainsNeedolin(action.GetType().Name);
                if (!related) continue;
                Logger.LogInfo("[NeedolinDump] State=" + state.Name);
                foreach (var transition in state.Transitions)
                    Logger.LogInfo("  Transition: " + transition.EventName + " -> " + transition.ToState);
                if (state.Actions == null) continue;
                foreach (var action in state.Actions)
                {
                    if (action == null) continue;
                    Logger.LogInfo("  Action: " + action.GetType().FullName);
                    foreach (FieldInfo field in action.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
                    {
                        try
                        {
                            object value = field.GetValue(action);
                            // FsmString/FsmObject etc. expose their configured value here.
                            PropertyInfo property = value == null ? null : value.GetType().GetProperty("Value");
                            if (property != null && property.GetIndexParameters().Length == 0)
                                value = property.GetValue(value, null);
                            Logger.LogInfo("    " + field.Name + " = " + (value == null ? "<null>" : value.ToString()));
                        }
                        catch (Exception exception)
                        {
                            Logger.LogInfo("    " + field.Name + " = <unreadable: " + exception.GetType().Name + ">");
                        }
                    }
                    if (action.GetType().Name == "RunFSM")
                    {
                        // runFsm is inherited; walk base types to find non-public fields.
                        Fsm child = null;
                        for (Type type = action.GetType(); type != null; type = type.BaseType)
                        {
                            FieldInfo field = type.GetField("runFsm", BindingFlags.Public
                                | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);
                            if (field == null) continue;
                            child = field.GetValue(action) as Fsm;
                            break;
                        }
                        var run = action as HutongGames.PlayMaker.Actions.RunFSM;
                        if (run != null && run.fsmTemplateControl != null)
                            Logger.LogInfo("[NeedolinDump] Template=" + run.fsmTemplateControl.fsmTemplate);
                        if (child == null)
                            Logger.LogInfo("[NeedolinDump] Sub-FSM unavailable. Press F8 while playing Needolin.");
                        else
                            DumpFsm(child, visited, depth + 1);
                    }
                }
            }
    }

    private static bool ContainsNeedolin(string value)
    {
        return value != null && value.IndexOf("needolin", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static string ObjectPath(Transform transform)
    {
        string path = transform.name;
        while (transform.parent != null)
        {
            transform = transform.parent;
            path = transform.name + "/" + path;
        }
        return path;
    }
}
