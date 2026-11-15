using System;
using System.Collections.Generic;
using UnityEngine;

namespace MaydayCallouts
{
    /// <summary>
    /// 告警规则引擎：把游戏状态映射到自制音效包的播放。
    ///
    /// 覆盖图依据（v0.1.0/v0.1.1 探测结论）——游戏在这些告警上只有灯/计时器/通知，
    /// 没有驾驶舱声音，所以本层直接叠加，不会和游戏自带声音打架：
    ///
    ///   失速      WarningPanel.stallLoop > 0        → stall_horn（循环=250ms 重触发）
    ///   起落架    gearHornTimer > 0                 → gear_horn
    ///   超速      overspeedTimer > 0                → overspeed
    ///   发动机火警 任一 EngineUnit.fire > 0.01       → fire_bell
    ///   主警告    任一 faults 位图从 0 变非 0        → master_caution（单次）
    ///   低油量    任一 tank 跨过 20%                 → low_fuel（单次）
    ///   客舱释压  HullSystem.holes 数量增加          → depress_horn（单次）
    ///
    /// 播放走 AudioPack.Play（SoundBank 注册 + Play2D 管线，探测证实出声）。
    /// 循环类 = 条件持续期间每 250ms 重触发，def.cooldown 吞掉多余触发；
    /// 条件解除后最后一声自然收尾。
    ///
    /// 同时维护 FlightSim 离地高度（呼话引擎共用）：
    ///   GroundY 在地面阶段（飞机停放、vy≈0）捕获，AGL = Pos.y − GroundY。
    ///  （实测教训：PlaneFlight.GroundProbeAgl 恒定 22.5 不可用；
    ///   PlaneFlight.transform 不随飞机移动也不可用。）
    /// </summary>
    internal static class SoundRules
    {
        static float _nextPoll;
        static global::Mayday.Flight.WarningPanel _panel;
        static global::Mayday.Flight.FlightSim _fs;

        static bool _lastHadFault;
        static bool[] _fuelLatched = new bool[2] { true, true };
        static int _lastHoles = -1;

        // 呼话引擎共用的高度状态
        public static string CurrentStage = "";
        public static float GroundY = float.MinValue;
        public static float FsimAgl = float.MinValue;
        public static float FsimVy;

        static readonly HashSet<string> GroundStages = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "PreShift", "Boarding", "Turnaround", "Deplaning", "Arrived" };

        public static void Tick()
        {
            try { PollFlightSim(); } catch { }

            if (!Plugin.Alarms.Value) return;
            if (!AudioPack.Loaded) return;
            if (global::Mayday.GameState.Instance == null) return;

            float t = UnityEngine.Time.unscaledTime;
            if (t < _nextPoll) return;
            _nextPoll = t + 0.25f;

            try { PollPanel(); } catch { }
            try { PollTakeoffLanding(); } catch { }
            try { PollEngines(); } catch { }
            try { PollFuel(); } catch { }
            try { PollHull(); } catch { }
        }

        static global::Mayday.Flight.PlaneFlight _pf;

        static void PollFlightSim()
        {
            if (!Plugin.Callouts.Value) return; // 只有呼话引擎需要
            if (_pf == null)
                _pf = UnityEngine.Object.FindObjectOfType<global::Mayday.Flight.PlaneFlight>();
            if (_pf == null) return;
            if (_fs == null)
            {
                try { _fs = _pf.sim; } catch { }
                if (_fs == null) return;
            }

            float pos = 0f, vy = 0f;
            try { pos = _fs.Pos.y; } catch { return; }
            try { vy = _pf.lastRenderVel.y; } catch { }
            FsimVy = vy;

            bool groundedStage = GroundStages.Contains(CurrentStage);
            if (GroundY == float.MinValue && groundedStage && Math.Abs(vy) < 0.5f)
                GroundY = pos; // 停在地面时标定机场地面高度
            if (groundedStage && Math.Abs(vy) < 0.3f)
                GroundY = pos; // 地面阶段持续校准

            FsimAgl = GroundY == float.MinValue ? float.MinValue : pos - GroundY;
        }

        // ---------- 起飞/着陆提示（V1 + RETARD，现实机组标准呼话） ----------
        //
        //   V1：地面加速滑跑、速度达到 V1Speed 配置阈值时报一次（起飞决断速度）。
        //       音源 = Audio\v1.wav（缺失则静默，等用户放文件）。
        //   RETARD：离地武装 → 进近下降穿越 15 高度 → 报一次（A320 落地收油门
        //       提醒，音源 = callout_retard.wav，用户已提供）。
        //       重新起飞后自动复位，每次着陆只报一次。
        //
        // 速度标定：V1Speed 默认 0（关闭）。日志在滑跑阶段每秒打 SPD=，
        // 用一轮起飞实测的滑跑速度范围来定 V1Speed。

        static bool _airborne;
        static bool _v1Latched;
        static bool _retardArmed;   // 离地后武装，着陆报一次
        static bool _retardLatched; // 本次着陆已报
        static float _nextSpdLog;

        public static void PollTakeoffLanding()
        {
            if (!Plugin.Callouts.Value) return;
            if (!AudioPack.Loaded) return;
            if (_fs == null || FsimAgl == float.MinValue) return;

            float agl = FsimAgl;
            float vy = FsimVy;
            float spd = 0f;
            try { if (_pf != null) spd = _pf.lastRenderVel.magnitude; } catch { }

            bool airborne = agl > 5f;
            bool onGround = agl < 1.5f;

            // 离地瞬间：复位 V1 门闩 + 武装 RETARD
            if (airborne && !_airborne)
            {
                _v1Latched = false;
                _retardArmed = true;
                Plugin.LogSource.LogInfo("[MaydayCallouts] 离地（AGL=" + agl.ToString("F0") + "）—— RETARD 已武装");
            }
            _airborne = airborne;

            // V1 呼话（滑跑阶段）
            if (onGround)
            {
                float now = UnityEngine.Time.unscaledTime;
                if (now >= _nextSpdLog && spd > 1f)
                {
                    _nextSpdLog = now + 1f;
                    Plugin.LogSource.LogInfo("[MaydayCallouts] 滑跑 SPD=" + spd.ToString("F1") + "（V1Speed=" + Plugin.V1Speed.Value.ToString("F0") + "）");
                }
                if (spd < 3f) _v1Latched = false; // 滑跑重新开始
                if (Plugin.V1Speed.Value > 0f && !_v1Latched && spd >= Plugin.V1Speed.Value)
                {
                    _v1Latched = true;
                    AudioPack.Play("v1");
                    Plugin.LogSource.LogInfo("[MaydayCallouts] 起飞提示: V1（spd=" + spd.ToString("F0") + "）");
                }
            }

            // RETARD 呼话（进近末段，A320 落地收油门提醒）
            if (_retardArmed && agl < 15f && vy < -0.5f && !_retardLatched)
            {
                _retardLatched = true;
                AudioPack.Play("callout_retard");
                Plugin.LogSource.LogInfo("[MaydayCallouts] 着陆提示: RETARD（AGL=" + agl.ToString("F0") + " vy=" + vy.ToString("F1") + "）");
            }
        }

        static void PollPanel()
        {
            if (_panel == null)
                _panel = UnityEngine.Object.FindObjectOfType<global::Mayday.Flight.WarningPanel>();
            if (_panel == null) return;

            bool stall = false, gear = false, over = false;
            try { stall = _panel.stallLoop > 0; } catch { }
            try { gear = _panel.gearHornTimer > 0; } catch { }
            try { over = _panel.overspeedTimer > 0; } catch { }

            // 失速：连续喇叭（现实失速警告就是连续音响，独立于 GPWS 语音）
            if (stall) AudioPack.Play("stall_horn");

            // 超速：连续抖杆（现实 overspeed clacker 同为连续）
            if (over) AudioPack.Play("overspeed");

            // 起落架：现实 GPWS Mode 4 —— "TOO LOW - GEAR" 语音，
            // 条件持续期间每 4.5s 重报一次（不是连续循环），
            // 且让位给更急的 Pull Up（GPWS 同时只播一条最急语音）。
            if (Plugin.AlarmGear.Value)
            {
                bool pullUpBusy = CalloutEngine.LastGpwsPullUp >= UnityEngine.Time.unscaledTime - 4f;
                if (gear && !pullUpBusy)
                {
                    if (!_gearVoiceLatched && UnityEngine.Time.unscaledTime >= _nextGearVoice)
                    {
                        _nextGearVoice = UnityEngine.Time.unscaledTime + 4.5f;
                        if (AudioPack.Has("gpws_too_low_gear"))
                        {
                            AudioPack.Play("gpws_too_low_gear");
                            Plugin.LogSource.LogInfo("[MaydayCallouts] 告警音: TOO LOW - GEAR（Mode 4 语音）");
                        }
                        else
                            AudioPack.Play("gear_horn"); // 没有语音文件时回退合成喇叭
                    }
                }
                else if (!gear)
                {
                    _gearVoiceLatched = false; // 改出（放起落架/爬升）后复位，下次再报
                }
            }
        }

        static bool _gearVoiceLatched;
        static float _nextGearVoice;

        static void PollEngines()
        {
            var arr = UnityEngine.Object.FindObjectsOfType<global::Mayday.Systems.EngineUnit>();
            if (arr == null || arr.Length == 0) return;

            bool anyFire = false, anyFault = false;
            for (int i = 0; i < arr.Length; i++)
            {
                var e = arr[i];
                if (e == null) continue;
                try
                {
                    if (e.fire > 0.01f) anyFire = true;
                    if (e.faults != 0) anyFault = true;
                }
                catch { }
            }

            if (anyFire) AudioPack.Play("fire_bell"); // 条件期间持续响

            if (anyFault && !_lastHadFault)
            {
                AudioPack.Play("master_caution");
                Plugin.LogSource.LogInfo("[MaydayCallouts] 告警音: 主警告（新故障）");
            }
            _lastHadFault = anyFault;
        }

        static void PollFuel()
        {
            var fs = UnityEngine.Object.FindObjectOfType<global::Mayday.Systems.FuelSystem>();
            if (fs == null) return;
            try
            {
                float cap = Math.Max(1f, fs.capacity);
                for (int i = 0; i < 2 && fs.tank != null && i < fs.tank.Length; i++)
                {
                    float pct = fs.tank[i] / cap * 100f;
                    if (pct <= 20f && !_fuelLatched[i])
                    {
                        _fuelLatched[i] = true;
                        AudioPack.Play("low_fuel");
                        Plugin.LogSource.LogInfo("[MaydayCallouts] 告警音: 低油量（" + (i == 0 ? "左" : "右") + "箱 " + pct.ToString("F0") + "%）");
                    }
                    if (pct > 25f) _fuelLatched[i] = false; // 回油复位
                }
            }
            catch { }
        }

        static void PollHull()
        {
            var hs = UnityEngine.Object.FindObjectOfType<global::Mayday.Systems.HullSystem>();
            if (hs == null) return;
            try
            {
                int holes = hs.holes != null ? hs.holes.Count : 0;
                if (_lastHoles >= 0 && holes > _lastHoles)
                {
                    AudioPack.Play("depress_horn");
                    Plugin.LogSource.LogInfo("[MaydayCallouts] 告警音: 客舱释压（破洞 " + _lastHoles + " -> " + holes + "）");
                }
                if (holes != _lastHoles) _lastHoles = holes;
            }
            catch { }
        }
    }
}
