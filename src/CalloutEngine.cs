using System;

namespace MaydayCallouts
{
    /// <summary>
    /// 高度呼话引擎：按游戏的 calloutHeights 表（100/80/60/40/30/20/10 + Minimums 60）
    /// 在下降穿越各高度时播放 callout_XXX 音效。
    ///
    /// 离地高度 AGL 的来源（实测教训）：
    ///   PlaneFlight.GroundProbeAgl 不可用 —— 探针实测恒定 22.5 不随飞行变化
    ///  （只在游戏内部特定时机刷新），导致呼话在同一瞬间全部引爆。
    ///   正确来源：plane.transform.position.y − initialParkPos.y
    ///  （飞机在机场停放时的原点高度，PlaneFlight 自带字段）——
    ///   停在地面时为 0，飞行即离地高度，单位与世界坐标一致。
    ///
    /// 一次性门闩：每个高度只报一次，重新爬升到高度表最大值以上后整组复位。
    /// 呼话最小间隔 1.2s：防止异常跳变时多条同帧齐爆（真机呼话一次只报一个）。
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
        static float _parkY = float.MinValue; // 机场地面高度（initialParkPos.y）

        static bool[] _latched;                 // 每个呼话高度的门闩
        static bool _minimumsLatched = true;    // Minimums 默认已报（起飞状态）
        static bool _gpwsSinkLatched;
        static bool _gpwsPullupLatched;
        static float _gpwsPullupNext;
        static float _nextCalloutAllowed;       // 呼话最小间隔（1.2s）
        static float _lastPullUpPlay;           // 最近一次 Pull Up 播放（优先级仲裁用）

        /// <summary>最近一次 Pull Up 语音的时间戳（给低优先级告警让位用）。</summary>
        public static float LastGpwsPullUp => _lastPullUpPlay;

        static int[] _heightsCache;             // 呼话高度表快照

        public static void Tick()
        {
            if (!Plugin.Callouts.Value) return;
            if (!AudioPack.Loaded) return;
            if (global::Mayday.GameState.Instance == null) return;

            try
            {
                if (_pf == null)
                {
                    if (_pfMissing) return;
                    _pf = UnityEngine.Object.FindObjectOfType<global::Mayday.Flight.PlaneFlight>();
                    if (_pf == null) { _pfMissing = true; return; }
                }

                // AGL 来源：SoundRules 维护的 FlightSim 离地高度
                //（GroundProbeAgl 恒定 22.5 不可用；transform 不随飞机移动不可用）
                float agl = SoundRules.FsimAgl;
                if (agl == float.MinValue) return; // 还没标定机场地面

                float velY = SoundRules.FsimVy;
                bool descending = velY < -0.5f;

                var names = global::Mayday.Flight.WarningPanel.calloutNames;
                var heights = global::Mayday.Flight.WarningPanel.calloutHeights;
                if (names == null || heights == null) return;

                int n = Math.Min(names.Length, heights.Length);
                if (_latched == null || _latched.Length != n || _heightsCache == null || !HeightsEqual(heights, n))
                {
                    _latched = new bool[n];
                    _heightsCache = new int[n];
                    for (int i = 0; i < n; i++) { try { _heightsCache[i] = heights[i]; } catch { _heightsCache[i] = 0; } }
                }

                // 爬回最高呼话高度以上 → 整组复位
                if (agl > heights[0] + 40f)
                {
                    ResetAll();
                    return;
                }

                if (!descending) return;

                // MINIMUMS（决断高，60）：现实规则——决断高优先于同值的高度呼话
                //（A320 在 DH 只报 "MINIMUMS"，不再报 "60"），且每次进近必响。
                // 之前它和 callout_60 同帧触发、被 1.2s 间隔保护拦掉且被永久
                // 闩住 —— 永远哑火。现在独立门闩 + 独立播放，并跳过同值呼话。
                bool minimumsJustPlayed = false;
                if (!_minimumsLatched && agl <= global::Mayday.Flight.WarningPanel.MinimumsHeight)
                {
                    _minimumsLatched = true;
                    _nextCalloutAllowed = UnityEngine.Time.unscaledTime + 1.2f;
                    AudioPack.Play("callout_minimums");
                    minimumsJustPlayed = true;
                    Plugin.LogSource.LogInfo("[MaydayCallouts] 呼话: callout_minimums（AGL=" + agl.ToString("F0") + "）");
                }

                for (int i = 0; i < n; i++)
                {
                    if (_latched[i]) continue;
                    if (agl <= heights[i])
                    {
                        _latched[i] = true;
                        // 决断高与该呼话同值且 MINIMUMS 刚播过 → 跳过同值呼话（现实规则）
                        bool isMinimumsHeight = Math.Abs(heights[i] - global::Mayday.Flight.WarningPanel.MinimumsHeight) < 1;
                        if (minimumsJustPlayed && isMinimumsHeight) continue;
                        if (UnityEngine.Time.unscaledTime < _nextCalloutAllowed) continue; // 间隔保护（门闩已置位）
                        _nextCalloutAllowed = UnityEngine.Time.unscaledTime + 1.2f;
                        string soundName = "callout_" + SafeName(names, i);
                        AudioPack.Play(soundName);
                        Plugin.LogSource.LogInfo("[MaydayCallouts] 呼话: " + soundName + "（AGL=" + agl.ToString("F0") + "）");
                    }
                }

                // GPWS 替换（可选）：大下降率，带升级链（sink_rate → pull_up）。
                // 拉平（vy 回升）后自动复位，可再次触发。触发时带 AGL/vy 日志供标定。
                if (Plugin.GpwsReplace.Value)
                {
                    if (velY > -2f) _gpwsSinkLatched = false;
                    if (velY < -5f && !_gpwsSinkLatched)
                    {
                        _gpwsSinkLatched = true; // sink_rate 每次俯冲事件报一次
                        AudioPack.Play("gpws_sink_rate");
                        Plugin.LogSource.LogInfo("[MaydayCallouts] GPWS: sink_rate（AGL=" + agl.ToString("F0") + " vy=" + velY.ToString("F1") + "）");
                    }
                    // PULL UP：真机在俯冲持续期间每 ~2.5s 重报，直到改出
                    if (velY < -9f && UnityEngine.Time.unscaledTime >= _gpwsPullupNext)
                    {
                        _gpwsPullupNext = UnityEngine.Time.unscaledTime + 2.5f;
                        _lastPullUpPlay = UnityEngine.Time.unscaledTime;
                        AudioPack.Play("gpws_pull_up");
                        Plugin.LogSource.LogInfo("[MaydayCallouts] GPWS: pull_up（AGL=" + agl.ToString("F0") + " vy=" + velY.ToString("F1") + "）");
                    }
                }
            }
            catch { }
        }

        static void ResetAll()
        {
            if (_latched != null) for (int i = 0; i < _latched.Length; i++) _latched[i] = false;
            _minimumsLatched = false;
            _gpwsSinkLatched = false;
        }

        static bool HeightsEqual(Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<int> current, int n)
        {
            try
            {
                if (_heightsCache == null) return false;
                for (int i = 0; i < n; i++) if (_heightsCache[i] != current[i]) return false;
                return true;
            }
            catch { return false; }
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
