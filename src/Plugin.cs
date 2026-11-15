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
        public const string Version = "0.2.0";

        internal static ManualLogSource LogSource;
        internal static ConfigFile ConfigRef;

        internal static ConfigEntry<bool> Probe;
        internal static ConfigEntry<bool> DumpSoundBank;
        internal static ConfigEntry<float> Volume;
        internal static ConfigEntry<string> TestSoundId;
        internal static ConfigEntry<bool> Alarms;
        internal static ConfigEntry<bool> AlarmStall;
        internal static ConfigEntry<bool> AlarmGear;
        internal static ConfigEntry<bool> AlarmOverspeed;
        internal static ConfigEntry<bool> AlarmFire;
        internal static ConfigEntry<bool> AlarmMasterCaution;
        internal static ConfigEntry<bool> AlarmFuel;
        internal static ConfigEntry<bool> AlarmHull;
        internal static ConfigEntry<bool> Callouts;
        internal static ConfigEntry<bool> GpwsReplace;
        internal static ConfigEntry<bool> TestAural;

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

            Alarms = Config.Bind(
                "Alarms",
                "Enabled",
                true,
                "告警音总开关（需要音效包：BepInEx\\plugins\\MaydayCallouts\\Audio\\*.wav，\n" +
                "可用 tools\\gen_sounds.py 生成自制包）。");

            AlarmStall = Config.Bind("Alarms", "Stall", true, "失速喇叭（stallLoop 状态驱动，循环直到解除）");
            AlarmGear = Config.Bind("Alarms", "Gear", true, "起落架警告喇叭（gearHornTimer 状态驱动）");
            AlarmOverspeed = Config.Bind("Alarms", "Overspeed", true, "超速抖杆声（overspeedTimer 状态驱动）");
            AlarmFire = Config.Bind("Alarms", "EngineFire", true, "发动机火警铃（任一 fire > 0）");
            AlarmMasterCaution = Config.Bind("Alarms", "MasterCaution", true, "主警告（任一发动机 faults 位图新增故障时单次）");
            AlarmFuel = Config.Bind("Alarms", "LowFuel", true, "低油量（任一油箱跨过 20%，回油复位）");
            AlarmHull = Config.Bind("Alarms", "Depressurize", true, "客舱释压（破洞数增加时单次）");

            Callouts = Config.Bind(
                "Callouts",
                "Enabled",
                true,
                "高度呼话引擎：下降穿越 100/80/60/40/30/20/10 时播放 callout_XXX 音效，\n" +
                "60 米（MinimumsHeight）报 callout_minimums。音源缺失的自动跳过。\n" +
                "高度判定用游戏自己的 GroundProbeAgl，单位与游戏内部一致。");

            GpwsReplace = Config.Bind(
                "Callouts",
                "ReplaceGameGpws",
                false,
                "用 mod 的 gpws_sink_rate / gpws_pull_up 音源替换游戏自带的 gpws_pullup。\n" +
                "开启后游戏原声被屏蔽（Play 前缀拦截），需在 Audio 目录提供这两个文件。");

            TestAural = Config.Bind(
                "Debug",
                "TestAural",
                false,
                "诊断实验：失速触发时直调游戏自己的 WarningPanel.Aural(pf, true, true)，\n" +
                "用于验证游戏的声音路径是否出声（听感判定）。平时关闭。");

            var harmony = new Harmony(Guid);
            harmony.PatchAll(typeof(Plugin).Assembly);

            LogSource.LogInfo("[MaydayCallouts] 已加载 v" + Version);
            LogSource.LogInfo("[MaydayCallouts] 补丁数 = " + harmony.GetPatchedMethods().Count());
            ConsoleGuard.DisableQuickEdit();
            AudioLink.Init();
            global::MaydayCallouts.Probe.Init();
            AudioPack.StartLoad(); // 轮询式加载自制音效包（Audio/*.wav，Tick 驱动）
        }
    }
}
