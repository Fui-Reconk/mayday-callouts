using HarmonyLib;

namespace MaydayCallouts
{
    /// <summary>
    /// Harmony 补丁集合。全部是 Postfix（只观察不干预），包括 ESC 拦截之外
    /// 本插件不改变游戏任何行为 —— 探测版纯观察。
    ///
    ///   - TickHook: 每帧轮询（探测节流门 + 试听键）
    ///   - StageHook: 导演阶段切换（PreShift/Boarding/Flight/Arrived/Deplaning/Turnaround）
    ///   - ChaosHook: 随机事件开始/结束（ChaosEvents.TryBegin/End）
    ///   - NotifyHook: 游戏弹出的每条通知 / 驾驶舱警示（ServerBroadcastNotify / ServerSetAlert）
    /// </summary>
    [HarmonyPatch]
    internal static class Hooks
    {
        [HarmonyPostfix]
        [HarmonyPatch(typeof(global::Mirror.NetworkManager), "Update")]
        private static void Postfix()
        {
            AudioPack.Tick(); // SoundBank 注册驱动
            AudioLink.Tick();
            Probe.Tick(); // 内部有 250ms 节流门
            SoundRules.Tick(); // 告警音规则引擎（同样节流）
            CalloutEngine.Tick(); // 高度呼话引擎
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(global::Mayday.Airport.GameDirector), "SetStage")]
        private static void StagePostfix(global::Mayday.Airport.GameDirector __instance)
        {
            Probe.OnStage(__instance.Stage);
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(global::Mayday.ChaosEvents), "TryBegin")]
        private static void ChaosBeginPostfix(string id, bool __result)
        {
            Probe.OnChaos(id, began: __result);
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(global::Mayday.ChaosEvents), "End")]
        private static void ChaosEndPostfix(string id)
        {
            Probe.OnChaos(id, began: false);
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(global::Mayday.GameState), "ServerBroadcastNotify")]
        private static void NotifyPostfix(string key, global::Mayday.NotifyKind kind, Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStringArray args)
        {
            Probe.OnNotify(key, kind, args);
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(global::Mayday.GameState), "ServerSetAlert")]
        private static void AlertPostfix(string key, double deadline, byte level)
        {
            Probe.OnAlert(key, deadline, level);
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(global::Mayday.Audio.AudioService), "Play2D")]
        private static bool Play2DPrefix(string id)
        {
            if (CalloutEngine.ShouldBlockSound(id) || Plugin.ShouldBlock(id))
            {
                AudioPack.TryReplace(id); // 屏蔽原声的同时，mod 有同名音源则接替播放
                return false;
            }
            return true;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(global::Mayday.Audio.AudioService), "Play")]
        private static bool PlayPrefix(string id)
        {
            if (CalloutEngine.ShouldBlockSound(id) || Plugin.ShouldBlock(id))
            {
                AudioPack.TryReplace(id);
                return false;
            }
            return true;
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(global::Mayday.Audio.AudioService), "Play2D")]
        private static void Play2DPostfix(string id)
        {
            Probe.OnGameSound(id, "2D");
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(global::Mayday.Audio.AudioService), "Play")]
        private static void PlayPostfix(string id)
        {
            Probe.OnGameSound(id, "3D");
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(global::Mayday.Audio.SoundBank), "Get")]
        private static void BankGetPostfix(string id, int variation, UnityEngine.AudioClip __result)
        {
            Probe.OnBankGet(id, variation, __result);
        }
    }
}
