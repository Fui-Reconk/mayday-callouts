using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using System.Linq;

namespace MaydayCallouts
{
    /// <summary>
    /// MaydayCallouts —— 给《Mayday Airways》补一层真实航空的告警音。
    ///
    /// v0.1.0 = 探测版：本插件现在**不主动发声**，只做三件事：
    ///   1. 记录游戏自己的驾驶舱告警（WarningPanel.LastCallout / 声音状态）
    ///   2. 记录游戏弹出的每一条通知（ServerBroadcastNotify / ServerSetAlert）
    ///   3. 记录飞行状态变化（发动机火警/故障、燃油、破洞、阶段、Chaos 事件）
    /// 目的：画出"游戏声音层 vs 通知层"的精确覆盖图，决定正式版给哪些告警配音。
    ///
    /// 挂载点清单见 README 兼容性小节。
    /// </summary>
    [BepInPlugin(Guid, Name, Version)]
    public class Plugin : BasePlugin
    {
        public const string Guid = "local.mayday.callouts";
        public const string Name = "Mayday Callouts";
        public const string Version = "0.1.0";

        internal static ManualLogSource LogSource;
        internal static ConfigFile ConfigRef;

        internal static ConfigEntry<bool> Probe;
        internal static ConfigEntry<bool> DumpSoundBank;
        internal static ConfigEntry<float> Volume;
        internal static ConfigEntry<string> TestSoundId;

        public override void Load()
        {
            LogSource = Log;
            ConfigRef = Config;

            Probe = Config.Bind(
                "General",
                "Probe",
                true,
                "探测模式：把游戏自己的告警呼话、通知、状态变化打进日志。\n" +
                "这是 v0.1.0 的核心功能——跑一轮飞行（最好故意触发点故障）就能画出\n" +
                "游戏声音层 vs 通知层的覆盖图，决定正式版给哪些告警配音。");

            DumpSoundBank = Config.Bind(
                "General",
                "DumpSoundBank",
                true,
                "启动时把游戏 SoundBank 里注册的全部音效 id 打进日志（排查复用/冲突用）。");

            Volume = Config.Bind(
                "General",
                "Volume",
                1f,
                "全局音量（0-1）。");

            TestSoundId = Config.Bind(
                "General",
                "TestSoundId",
                "",
                "试听用的游戏音效 id（留空禁用）。进机舱后按 P 播放一次，\n" +
                "用于验证 AudioService.Play2D 在 interop 下是否可用。音效 id 见启动日志的 SoundBank 转储。");

            var harmony = new Harmony(Guid);
            harmony.PatchAll(typeof(Plugin).Assembly);

            LogSource.LogInfo("[MaydayCallouts] 已加载 v" + Version);
            LogSource.LogInfo("[MaydayCallouts] 补丁数 = " + harmony.GetPatchedMethods().Count());
            ConsoleGuard.DisableQuickEdit();
            AudioLink.Init();
            global::MaydayCallouts.Probe.Init();
        }
    }
}
