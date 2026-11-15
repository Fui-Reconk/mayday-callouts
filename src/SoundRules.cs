using System;
using System.Collections.Generic;
using UnityEngine;

namespace MaydayCallouts
{
    /// <summary>
    /// 告警规则引擎：把游戏状态映射到自制音效包的播放。
    ///
    /// 覆盖图依据（v0.1.0 探测版结论）——游戏在这些告警上只有灯/计时器/通知，
    /// 没有驾驶舱声音，所以本层直接叠加，不会和游戏自带声音打架：
    ///
    ///   失速      WarningPanel.stallLoop > 0        → stall_horn 循环
    ///   起落架    gearHornTimer > 0                 → gear_horn 循环
    ///   超速      overspeedTimer > 0                → overspeed 循环
    ///   发动机火警 任一 EngineUnit.fire > 0.01       → fire_bell 循环
    ///   主警告    任一 faults 位图从 0 变非 0        → master_caution 单次
    ///   低油量    任一 tank 跨过 20%                 → low_fuel 单次
    ///   客舱释压  HullSystem.holes 数量增加          → depress_horn 单次
    ///
    /// 每个告警独立配置开关；循环类在条件解除时自动 Stop。
    /// 高度呼话链（calloutHeights）需要离地高度标定，v0.3 再上。
    /// </summary>
    internal static class SoundRules
    {
        static float _nextPoll;

        // 循环告警的当前状态
        static bool _stallPlaying, _gearPlaying, _overPlaying, _firePlaying;

        // 主警告：faults 从 0 变非 0 才触发一次
        static bool _lastHadFault;

        // 燃油：跨 20% 触发一次，回油后复位
        static bool[] _fuelLatched = new bool[2] { true, true };

        // 释压：破洞数增加触发一次
        static int _lastHoles = -1;

        public static void Tick()
        {
            if (!Plugin.Alarms.Value) return;
            if (!AudioPack.Loaded) return;
            if (global::Mayday.GameState.Instance == null) { StopAll(); return; }

            float t = UnityEngine.Time.unscaledTime;
            if (t < _nextPoll) return;
            _nextPoll = t + 0.25f;

            try { PollPanel(); } catch { }
            try { PollEngines(); } catch { }
            try { PollFuel(); } catch { }
            try { PollHull(); } catch { }
        }

        public static void StopAll()
        {
            if (_stallPlaying) { AudioPack.StopLoop("stall_horn"); _stallPlaying = false; }
            if (_gearPlaying) { AudioPack.StopLoop("gear_horn"); _gearPlaying = false; }
            if (_overPlaying) { AudioPack.StopLoop("overspeed"); _overPlaying = false; }
            if (_firePlaying) { AudioPack.StopLoop("fire_bell"); _firePlaying = false; }
            _lastHadFault = false;
            _fuelLatched = new bool[2] { true, true };
            _lastHoles = -1;
        }

        static void PollPanel()
        {
            var panel = UnityEngine.Object.FindObjectOfType<global::Mayday.Flight.WarningPanel>();
            if (panel == null) { StopLoopsExcept(new HashSet<string>()); return; }

            bool stall = false, gear = false, over = false;
            try { stall = panel.stallLoop > 0; } catch { }
            try { gear = panel.gearHornTimer > 0; } catch { }
            try { over = panel.overspeedTimer > 0; } catch { }

            if (Plugin.AlarmStall.Value)
            {
                if (stall && !_stallPlaying) { AudioPack.PlayLoop("stall_horn"); Plugin.LogSource.LogInfo("[MaydayCallouts] 告警音: 失速喇叭"); _stallPlaying = true; }
                if (!stall && _stallPlaying) { AudioPack.StopLoop("stall_horn"); _stallPlaying = false; }
            }
            if (Plugin.AlarmGear.Value)
            {
                if (gear && !_gearPlaying) { AudioPack.PlayLoop("gear_horn"); Plugin.LogSource.LogInfo("[MaydayCallouts] 告警音: 起落架警告"); _gearPlaying = true; }
                if (!gear && _gearPlaying) { AudioPack.StopLoop("gear_horn"); _gearPlaying = false; }
            }
            if (Plugin.AlarmOverspeed.Value)
            {
                if (over && !_overPlaying) { AudioPack.PlayLoop("overspeed"); Plugin.LogSource.LogInfo("[MaydayCallouts] 告警音: 超速抖杆"); _overPlaying = true; }
                if (!over && _overPlaying) { AudioPack.StopLoop("overspeed"); _overPlaying = false; }
            }
        }

        static HashSet<string> StopLoopsExcept(HashSet<string> keep)
        {
            if (_stallPlaying && !keep.Contains("stall_horn")) { AudioPack.StopLoop("stall_horn"); _stallPlaying = false; }
            if (_gearPlaying && !keep.Contains("gear_horn")) { AudioPack.StopLoop("gear_horn"); _gearPlaying = false; }
            if (_overPlaying && !keep.Contains("overspeed")) { AudioPack.StopLoop("overspeed"); _overPlaying = false; }
            if (_firePlaying && !keep.Contains("fire_bell")) { AudioPack.StopLoop("fire_bell"); _firePlaying = false; }
            return keep;
        }

        static void PollEngines()
        {
            var arr = UnityEngine.Object.FindObjectsOfType<global::Mayday.Systems.EngineUnit>();
            if (arr == null || arr.Length == 0) { StopLoopsExcept(new HashSet<string>()); return; }

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

            if (Plugin.AlarmFire.Value)
            {
                if (anyFire && !_firePlaying) { AudioPack.PlayLoop("fire_bell"); Plugin.LogSource.LogInfo("[MaydayCallouts] 告警音: 发动机火警"); _firePlaying = true; }
                if (!anyFire && _firePlaying) { AudioPack.StopLoop("fire_bell"); _firePlaying = false; }
            }

            if (Plugin.AlarmMasterCaution.Value)
            {
                if (anyFault && !_lastHadFault)
                {
                    AudioPack.PlayOnce("master_caution");
                    Plugin.LogSource.LogInfo("[MaydayCallouts] 告警音: 主警告（新故障）");
                }
                _lastHadFault = anyFault;
            }

            if (!anyFire) StopLoopsExcept(new HashSet<string>());
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
                        if (Plugin.AlarmFuel.Value)
                        {
                            AudioPack.PlayOnce("low_fuel");
                            Plugin.LogSource.LogInfo("[MaydayCallouts] 告警音: 低油量（" + (i == 0 ? "左" : "右") + "箱 " + pct.ToString("F0") + "%）");
                        }
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
                if (_lastHoles >= 0 && holes > _lastHoles && Plugin.AlarmHull.Value)
                {
                    AudioPack.PlayOnce("depress_horn");
                    Plugin.LogSource.LogInfo("[MaydayCallouts] 告警音: 客舱释压（破洞 " + _lastHoles + " -> " + holes + "）");
                }
                if (holes != _lastHoles) _lastHoles = holes;
            }
            catch { }
        }
    }
}
