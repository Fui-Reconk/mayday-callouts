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

        static void PollPanel()
        {
            if (_panel == null)
                _panel = UnityEngine.Object.FindObjectOfType<global::Mayday.Flight.WarningPanel>();
            if (_panel == null) return;

            bool stall = false, gear = false, over = false;
            try { stall = _panel.stallLoop > 0; } catch { }
            try { gear = _panel.gearHornTimer > 0; } catch { }
            try { over = _panel.overspeedTimer > 0; } catch { }

            // 循环类：条件持续期间每轮 Play（cooldown 吞掉多余触发）
            if (stall) AudioPack.Play("stall_horn");
            if (gear) AudioPack.Play("gear_horn");
            if (over) AudioPack.Play("overspeed");
        }

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
