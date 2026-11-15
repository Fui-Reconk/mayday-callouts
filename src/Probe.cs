using System;
using System.Collections.Generic;
using System.Text;

namespace MaydayCallouts
{
    /// <summary>
    /// 探测模式核心：观察游戏告警体系，画出"声音层 vs 通知层"覆盖图。
    ///
    /// 观察对象（全部只读，不干预）：
    ///   - WarningPanel：LastCallout / calloutNames / calloutHeights /
    ///     MinimumsHeight / audioSource.isPlaying / stallLoop / gearHornTimer /
    ///     overspeedTimer —— 游戏自己的 GPWS/呼话/告警系统
    ///   - 发动机：fire / faults 位图 / state（Mayday.Systems.EngineUnit）
    ///   - 燃油：FuelSystem.tank[] 百分比
    ///   - 机体：HullSystem 破洞数
    ///   - Chaos 事件：ChaosEvents.Active / LastId
    ///
    /// 输出原则：只在"变化"时打日志（快照式），每 250ms 轮询一次。
    /// </summary>
    internal static class Probe
    {
        static global::Mayday.Flight.WarningPanel _panel;
        static bool _panelDumped;

        // 上一次快照（变化检测）
        static string _lastCallout = "";     // 游戏呼话名
        static string _lastAudioState = "";  // 驾驶舱音频播放状态
        static string _lastStage = "";
        static string _lastChaos = "";       // Chaos 事件 / 借用为告警计时器状态

        static ushort[] _lastEngineFaults = new ushort[4];
        static string[] _lastEngineState = new string[4];
        static bool[] _lastEngineFire = new bool[4];
        static float[] _lastFuelPct = new float[2] { -1f, -1f };
        static int _lastHoles = -1;
        static global::UnityEngine.AudioSource _panelAudio;

        static float _nextPoll;

        public static void Init()
        {
            if (!Plugin.Probe.Value) Plugin.LogSource.LogInfo("[MaydayCallouts] 探测模式未启用（Probe=false）");
        }

        static bool Ready => Plugin.Probe.Value && global::Mayday.GameState.Instance != null;

        public static void Tick()
        {
            if (!Ready) return;
            float t = UnityEngine.Time.unscaledTime;
            if (t < _nextPoll) return;
            _nextPoll = t + 0.25f;

            try { PollPanel(); } catch { }
            try { PollAgl(); } catch { }
            try { PollEngines(); } catch { }
            try { PollFuel(); } catch { }
            try { PollHull(); } catch { }
            // Chaos 事件走 Harmony 后缀（OnChaos），这里不再轮询
        }

        // ---------- WarningPanel：游戏自己的 GPWS/呼话系统 ----------

        static void PollPanel()
        {
            if (_panel == null)
            {
                _panel = UnityEngine.Object.FindObjectOfType<global::Mayday.Flight.WarningPanel>();
                if (_panel == null) return;
            }

            if (!_panelDumped)
            {
                _panelDumped = true;
                var sb = new StringBuilder("[MaydayCallouts] WarningPanel 转储: ");
                try
                {
                    // MinimumsHeight / calloutNames / calloutHeights 是静态配置字段
                    sb.Append("MinimumsHeight=").Append(global::Mayday.Flight.WarningPanel.MinimumsHeight);
                    var names = global::Mayday.Flight.WarningPanel.calloutNames;
                    sb.Append(" calloutNames=[");
                    if (names != null)
                        for (int i = 0; i < names.Length; i++)
                            sb.Append(i > 0 ? ", " : "").Append(names[i]);
                    sb.Append("]");
                    var heights = global::Mayday.Flight.WarningPanel.calloutHeights;
                    sb.Append(" calloutHeights=[");
                    if (heights != null)
                        for (int i = 0; i < heights.Length; i++)
                            sb.Append(i > 0 ? ", " : "").Append(heights[i].ToString("F1"));
                    sb.Append("]");
                }
                catch (Exception e) { sb.Append("（部分字段读取失败: ").Append(e.Message).Append("）"); }
                Plugin.LogSource.LogInfo(sb.ToString());
            }

            try
            {
                string lc = global::Mayday.Flight.WarningPanel.LastCallout;
                if (!string.IsNullOrEmpty(lc) && lc != _lastCallout)
                {
                    _lastCallout = lc;
                    Plugin.LogSource.LogInfo("[MaydayCallouts] 游戏呼话触发: " + lc);
                }
            }
            catch { }

            try
            {
                // WarningPanel.audioSource 字段是 Transform（音频的定位点），
                // 真正的播放器从同一个 GameObject 上取
                if (_panelAudio == null)
                {
                    var src = _panel.audioSource != null
                        ? _panel.audioSource.gameObject.GetComponent<UnityEngine.AudioSource>()
                        : null;
                    _panelAudio = src;
                }
                if (_panelAudio != null)
                {
                    bool playing = _panelAudio.isPlaying;
                    string clip = "";
                    try { if (playing && _panelAudio.clip != null) clip = _panelAudio.clip.name; } catch { }
                    string st = playing ? "播放中 clip=" + clip : "静默";
                    if (st != _lastAudioState)
                    {
                        Plugin.LogSource.LogInfo("[MaydayCallouts] 驾驶舱音频: " + st);
                        _lastAudioState = st;
                    }
                }
            }
            catch { }

            try
            {
                float gearHorn = _panel.gearHornTimer;
                float over = _panel.overspeedTimer;
                int stall = _panel.stallLoop;
                bool stallActive = stall > 0;
                TryAuralTest(_panel, stallActive); // Aural 直调实验（TestAural=true 时）
                string st = "gear=" + (gearHorn > 0f) + " over=" + (over > 0f) + " stall=" + stallActive;
                if (st != _lastChaos) // 借用：状态翻转才打一条
                {
                    Plugin.LogSource.LogInfo("[MaydayCallouts] 驾驶舱告警状态: " + st);
                    _lastChaos = st;
                }
            }
            catch { }
        }

        // ---------- 发动机 ----------

        static void PollEngines()
        {
            var arr = UnityEngine.Object.FindObjectsOfType<global::Mayday.Systems.EngineUnit>();
            if (arr == null) return;
            for (int i = 0; i < arr.Length && i < 4; i++)
            {
                var e = arr[i];
                if (e == null) continue;
                try
                {
                    bool fire = e.fire > 0.01f;
                    ushort faults = e.faults;
                    string state = e.state.ToString();
                    bool changed = fire != _lastEngineFire[i] || faults != _lastEngineFaults[i] || state != _lastEngineState[i];
                    if (changed)
                    {
                        Plugin.LogSource.LogInfo(string.Format(
                            "[MaydayCallouts] 发动机#{0} 状态变化: fire={1:F2} faults=0x{2:X4} state={3}",
                            i, e.fire, faults, state));
                        _lastEngineFire[i] = fire;
                        _lastEngineFaults[i] = faults;
                        _lastEngineState[i] = state;
                    }
                }
                catch { }
            }
        }

        // ---------- 燃油（跨过 20% 阈值才记录） ----------

        static void PollFuel()
        {
            var fs = UnityEngine.Object.FindObjectOfType<global::Mayday.Systems.FuelSystem>();
            if (fs == null) return;
            try
            {
                float cap = Math.Max(1f, fs.capacity);
                float l = fs.tank != null && fs.tank.Length > 0 ? fs.tank[0] : 0f;
                float r = fs.tank != null && fs.tank.Length > 1 ? fs.tank[1] : 0f;
                float lp = l / cap * 100f, rp = r / cap * 100f;
                for (int i = 0; i < 2; i++)
                {
                    float pct = i == 0 ? lp : rp;
                    float last = _lastFuelPct[i];
                    if (last < 0f) { _lastFuelPct[i] = pct; continue; }
                    // 跨过 20% 或 5%（临界）才打
                    bool crossed = (last > 20f && pct <= 20f) || (last > 5f && pct <= 5f);
                    if (crossed)
                        Plugin.LogSource.LogInfo(string.Format(
                            "[MaydayCallouts] 燃油阈值跨越: {0}箱 {1:F0}%（剩余 {2:F1}L）",
                            i == 0 ? "左" : "右", pct, i == 0 ? l : r));
                    _lastFuelPct[i] = pct;
                }
            }
            catch { }
        }

        // ---------- 机体（破洞数变化） ----------

        static void PollHull()
        {
            var hs = UnityEngine.Object.FindObjectOfType<global::Mayday.Systems.HullSystem>();
            if (hs == null) return;
            try
            {
                int holes = hs.holes != null ? hs.holes.Count : 0;
                if (_lastHoles >= 0 && holes != _lastHoles)
                    Plugin.LogSource.LogInfo("[MaydayCallouts] 机体破洞数变化: " + _lastHoles + " -> " + holes);
                if (holes != _lastHoles) _lastHoles = holes;
            }
            catch { }
        }

        // ---------- 阶段 / Chaos / 通知 ----------

        public static void OnStage(global::Mayday.Airport.DirectorStage stage)
        {
            string s = stage.ToString();
            if (s == _lastStage) return;
            _lastStage = s;
            if (Ready) Plugin.LogSource.LogInfo("[MaydayCallouts] 阶段切换 -> " + s);
        }

        public static void OnChaos(string id, bool began)
        {
            string tag = (began ? "开始" : "结束") + ": " + id;
            if (tag == _lastChaos) return;
            _lastChaos = tag;
            Plugin.LogSource.LogInfo("[MaydayCallouts] Chaos 事件" + tag);
        }

        public static void OnNotify(string key, global::Mayday.NotifyKind kind, Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStringArray args)
        {
            // 通知原文 = 本地化后的文本（游戏自己的 UI 会渲染它）；
            // 探测只记 key 和 kind —— key 就是告警的"身份证"
            Plugin.LogSource.LogInfo("[MaydayCallouts] 通知: kind=" + kind + " key=" + key);
        }

        public static void OnAlert(string key, double deadline, byte level)
        {
            Plugin.LogSource.LogInfo("[MaydayCallouts] 驾驶舱警示: level=" + level + " key=" + key + " deadline=" + deadline.ToString("F1"));
        }

        // ---------- SoundBank.Get 终审探针：游戏到底在请求哪些音频、拿没拿到 ----------
        //
        // SoundBank.Get(id, variation) 是所有 SoundBank 音效的必经之路
        //（Play/Play2D 之外还有 PlayInternal 等直连路径，只有 Get 能全覆盖）。
        //   - 请求返回 null  = 音效定义/音频在 demo 里缺失（"哑巴告警"实锤）
        //   - 请求返回 clip  = 有音频（此时听不到就是播放层问题）
        // null 无条件记录（决定性证据）；非 null 的 id 一次性记录。

        static readonly HashSet<string> _bankSeen = new HashSet<string>();

        public static void OnBankGet(string id, int variation, UnityEngine.AudioClip result)
        {
            try
            {
                if (string.IsNullOrEmpty(id)) return;
                if (result == null)
                {
                    Plugin.LogSource.LogWarning("[MaydayCallouts] SoundBank.Get 未命中: id=" + id + " variation=" + variation + " → null");
                    return;
                }
                if (_bankSeen.Add(id))
                    Plugin.LogSource.LogInfo("[MaydayCallouts] SoundBank.Get: " + id + " → " + result.name + "（有音频）");
            }
            catch { }
        }

        // ---------- AGL 终审探针：GroundProbeAgl 的实际行为 ----------

        static global::Mayday.Flight.PlaneFlight _pfCached;
        static float _nextAglLog;
        static float _lastLoggedAgl = float.MinValue;

        static void PollAgl()
        {
            try
            {
                if (_pfCached == null)
                    _pfCached = UnityEngine.Object.FindObjectOfType<global::Mayday.Flight.PlaneFlight>();
                if (_pfCached == null) return;

                float agl = global::Mayday.Flight.PlaneFlight.GroundProbeAgl;
                float vy = 0f;
                try { vy = _pfCached.lastRenderVel.y; } catch { }

                float now = UnityEngine.Time.unscaledTime;
                if (now >= _nextAglLog || Math.Abs(agl - _lastLoggedAgl) > 3f)
                {
                    _nextAglLog = now + 1f;
                    _lastLoggedAgl = agl;
                    Plugin.LogSource.LogInfo("[MaydayCallouts] AGL=" + agl.ToString("F1") + " vy=" + vy.ToString("F1"));
                }
            }
            catch { }
        }

        // ---------- Aural 直调实验：游戏自己的声音路径到底出不出声 ----------

        static bool _auralTestedThisStall;

        static void TryAuralTest(global::Mayday.Flight.WarningPanel panel, bool stallActive)
        {
            if (!Plugin.TestAural.Value) return;
            if (!stallActive) { _auralTestedThisStall = false; return; }
            if (_auralTestedThisStall) return;
            try
            {
                if (_pfCached == null)
                    _pfCached = UnityEngine.Object.FindObjectOfType<global::Mayday.Flight.PlaneFlight>();
                if (_pfCached == null) return;
                panel.Aural(_pfCached, true, true);
                _auralTestedThisStall = true;
                Plugin.LogSource.LogWarning("[MaydayCallouts] Aural 直调实验：已调用 Aural(pf, stallWarn=true, power=true) —— 听一下有没有失速警告声");
            }
            catch (Exception e)
            {
                Plugin.LogSource.LogWarning("[MaydayCallouts] Aural 直调失败: " + e.Message);
            }
        }

        // ---------- 游戏声音播放记录（事件 → 音效 id 映射，替换式覆盖的依据） ----------        //
        // AudioService.Play2D / Play 是游戏全部声音的必经之路。记录每次播放的 id
        // 和时间点，和通知/呼话时间线对齐后，就能知道：
        //   - AP 接通/安全带叮咚这些"有声音的告警"各自用的是哪个 id（可精确屏蔽替换）
        //   - 哪些通知压根没有声音（正式版补盲区的靶子）
        // 同 id 0.5 秒内只记一次（循环音/连发防刷屏）。

        static readonly Dictionary<string, float> _lastSoundLog = new Dictionary<string, float>();
        static readonly Dictionary<string, int> _soundCounts = new Dictionary<string, int>();

        public static void OnGameSound(string id, string kind)
        {
            try
            {
                if (string.IsNullOrEmpty(id)) return;
                float now = UnityEngine.Time.unscaledTime;
                _soundCounts.TryGetValue(id, out int n);
                _soundCounts[id] = n + 1;

                _lastSoundLog.TryGetValue(id, out float last);
                if (now - last < 0.5f) return;
                _lastSoundLog[id] = now;
                Plugin.LogSource.LogInfo("[MaydayCallouts] 声音: " + id + "（" + kind + "，第 " + _soundCounts[id] + " 次，t=" + now.ToString("F1") + "）");
            }
            catch { }
        }
    }
}
