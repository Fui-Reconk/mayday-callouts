using System;

namespace MaydayCallouts
{
    /// <summary>
    /// 高度呼话引擎：按游戏的 calloutHeights 表（100/80/60/40/30/20/10 + Minimums 60）
    /// 在下降穿越各高度时播放 callout_XXX 音效。
    ///
    /// 判据全部来自游戏自己的数据：
    ///   - 离地高度 AGL = PlaneFlight.GroundProbeAgl（游戏每帧自算的静态字段）
    ///   - 下降方向 = PlaneFlight.lastRenderVel.y < 0
    ///   - 高度表 = WarningPanel.calloutHeights（静态，游戏自己的配置）
    ///   - 音效名 = WarningPanel.calloutNames（"100"/"80"/"60"/"40"/"30"/"20"/"10"）
    /// 所以单位/阈值与游戏内部完全一致，无需标定。
    ///
    /// 一次性门闩：每个高度只报一次，重新爬升到高度表最大值以上后整组复位。
    ///
    /// 音源文件（Audio 目录，缺失自动跳过）：
    ///   callout_100 / callout_80 / callout_60 / callout_40 / callout_30 /
    ///   callout_20 / callout_10 / callout_minimums
    ///
    /// GPWS 替换（GpwsReplace=true 时启用）：
    ///   - 屏蔽游戏自带的 gpws_pullup 音效（Play 前缀拦截）
    ///   - 大下降率 → gpws_sink_rate（5s 冷却）
    ///   - 更急 → gpws_pull_up（3s 冷却）
    /// </summary>
    internal static class CalloutEngine
    {
        static global::Mayday.Flight.PlaneFlight _pf;
        static bool _pfMissing;

        static bool[] _latched;                 // 每个呼话高度的门闩
        static bool _minimumsLatched = true;    // Minimums 默认已报（起飞状态）
        static bool _gpwsSinkLatched;
        static float _gpwsPullupNext;

        public static void Tick()
        {
            if (!Plugin.Callouts.Value) return;
            if (AudioPack.Loaded != true) return;
            if (global::Mayday.GameState.Instance == null) return;

            try
            {
                if (_pf == null)
                {
                    if (_pfMissing) return;
                    _pf = UnityEngine.Object.FindObjectOfType<global::Mayday.Flight.PlaneFlight>();
                    if (_pf == null) { _pfMissing = true; return; }
                }

                float agl = global::Mayday.Flight.PlaneFlight.GroundProbeAgl;
                float velY = 0f;
                try { velY = _pf.lastRenderVel.y; } catch { }
                bool descending = velY < -0.5f;

                var names = global::Mayday.Flight.WarningPanel.calloutNames;
                var heights = global::Mayday.Flight.WarningPanel.calloutHeights;
                if (names == null || heights == null) return;

                int n = Math.Min(names.Length, heights.Length);
                if (_latched == null || _latched.Length != n) _latched = new bool[n];

                // 爬回最高呼话高度以上 → 整组复位
                if (agl > heights[0] + 40f)
                {
                    for (int i = 0; i < n; i++) _latched[i] = false;
                    _minimumsLatched = false;
                    _gpwsSinkLatched = false;
                    return;
                }

                if (!descending) return;

                for (int i = 0; i < n; i++)
                {
                    if (_latched[i]) continue;
                    if (agl <= heights[i])
                    {
                        _latched[i] = true;
                        string soundName = "callout_" + SafeName(names, i);
                        AudioPack.PlayOnce(soundName);
                        Plugin.LogSource.LogInfo("[MaydayCallouts] 呼话: " + soundName + "（AGL=" + agl.ToString("F0") + "）");
                    }
                }

                // Minimums（决断高，60）：独立门闩
                if (!_minimumsLatched && agl <= global::Mayday.Flight.WarningPanel.MinimumsHeight)
                {
                    _minimumsLatched = true;
                    AudioPack.PlayOnce("callout_minimums");
                    Plugin.LogSource.LogInfo("[MaydayCallouts] 呼话: callout_minimums（AGL=" + agl.ToString("F0") + "）");
                }

                // GPWS 替换（可选）：大下降率
                if (Plugin.GpwsReplace.Value)
                {
                    if (agl < 150f && velY < -10f && !_gpwsSinkLatched)
                    {
                        _gpwsSinkLatched = true;
                        AudioPack.PlayOnce("gpws_sink_rate");
                        Plugin.LogSource.LogInfo("[MaydayCallouts] GPWS: sink_rate（AGL=" + agl.ToString("F0") + " vy=" + velY.ToString("F1") + "）");
                    }
                    if (agl < 100f && velY < -16f && UnityEngine.Time.unscaledTime >= _gpwsPullupNext)
                    {
                        _gpwsPullupNext = UnityEngine.Time.unscaledTime + 3f;
                        AudioPack.PlayOnce("gpws_pull_up");
                        Plugin.LogSource.LogInfo("[MaydayCallouts] GPWS: pull_up（AGL=" + agl.ToString("F0") + " vy=" + velY.ToString("F1") + "）");
                    }
                }
            }
            catch { }
        }

        /// <summary>GpwsReplace=true 时屏蔽游戏自带的 gpws_pullup（由 mod 的真实音源接管）。</summary>
        public static bool ShouldBlockSound(string id)
        {
            try { return Plugin.GpwsReplace.Value && id == "gpws_pullup"; }
            catch { return false; }
        }

        static string SafeName(Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStringArray arr, int i)
        {
            try { return arr[i]?.ToString() ?? ""; } catch { return ""; }
        }
    }
}
