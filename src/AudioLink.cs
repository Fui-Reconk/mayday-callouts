using System;

namespace MaydayCallouts
{
    /// <summary>
    /// 游戏音频系统的接入层（v0.1.0 = 只读探测 + 试听）。
    ///
    /// 游戏自带一套数据驱动的音效系统：
    ///   - SoundBank.AllDefs：全部注册的音效定义（SoundDef：id/category/cooldown/...）
    ///   - AudioService.Instance.Play2D(id, volume, pitch)：按 id 播 2D 音效
    ///   - Play(id, pos, volume, pitch, transform)：按 id 播 3D 定位音效
    ///
    /// 本层做三件事：
    ///   1. 启动时转储 SoundBank 的全部音效 id（正式版挑音效用）
    ///   2. 缓存 AudioService 单例
    ///   3. 试听：配置 TestSoundId 后，进机舱按 P 用 Play2D 播一次
    ///      —— 验证 interop 下能否直接调用游戏的音频 API（正式版的基础）
    ///
    /// 正式版的"外置音频包"路线（UnityWebRequest 加载 ogg）在本层之后扩展。
    /// </summary>
    internal static class AudioLink
    {
        static global::Mayday.Audio.AudioService _service;
        static bool _bankDumped;
        static bool _serviceMissing;

        public static void Init()
        {
            try
            {
                _service = UnityEngine.Object.FindObjectOfType<global::Mayday.Audio.AudioService>();
                if (_service == null)
                    Plugin.LogSource.LogInfo("[MaydayCallouts] AudioService 未就绪（进机舱后可用）");
            }
            catch (Exception e)
            {
                _serviceMissing = true;
                Plugin.LogSource.LogWarning("[MaydayCallouts] AudioService 查找失败: " + e.Message);
            }
        }

        public static void Tick()
        {
            if (_service == null && !_serviceMissing)
            {
                try
                {
                    _service = UnityEngine.Object.FindObjectOfType<global::Mayday.Audio.AudioService>();
                }
                catch { _serviceMissing = true; }
            }

            if (Plugin.DumpSoundBank.Value && !_bankDumped && _service != null)
            {
                // SoundBank 是懒加载的：启动早期 AllDefs 里全是 null，
                // 等出现非空定义再转储（一次性，成功前每次轮询都试）
                DumpBank();
            }

            PollTestKey();
            PollSelfTest();
        }

        /// <summary>把 SoundBank 注册的全部音效 id 打进日志（定义未加载时不置完成位，等下次轮询）。</summary>
        static void DumpBank()
        {
            try
            {
                var defs = global::Mayday.Audio.SoundBank.AllDefs;
                if (defs == null) { Plugin.LogSource.LogInfo("[MaydayCallouts] SoundBank.AllDefs 为空"); _bankDumped = true; return; }
                // Il2Cpp 的 IReadOnlyList 包装器不实现枚举/Count —— 转成 List 走索引
                var list = defs.TryCast<Il2CppSystem.Collections.Generic.List<global::Mayday.Audio.SoundDef>>();
                if (list == null) { Plugin.LogSource.LogInfo("[MaydayCallouts] SoundBank.AllDefs 无法转 List"); _bankDumped = true; return; }
                var sb = new System.Text.StringBuilder("[MaydayCallouts] SoundBank 音效 id（共 ").Append(list.Count).Append(" 个）:");
                int named = 0;
                for (int i = 0; i < list.Count; i++)
                {
                    var d = list[i];
                    if (d == null) continue;
                    string id = "";
                    try { id = d.id; } catch { }
                    if (string.IsNullOrEmpty(id)) continue;
                    string cat = "";
                    try { cat = d.category.ToString(); } catch { }
                    sb.Append("\n  [").Append(i).Append("] ").Append(id).Append("（").Append(cat).Append("）");
                    named++;
                }
                if (named == 0) return; // 定义还没懒加载完，等下次轮询再试
                _bankDumped = true;
                Plugin.LogSource.LogInfo(sb.ToString());
            }
            catch (Exception e)
            {
                Plugin.LogSource.LogWarning("[MaydayCallouts] SoundBank 转储失败: " + e.Message);
                _bankDumped = true;
            }
        }

        static float _nextTestAllowed;
        static float _nextSelfTest;

        static void PollTestKey()
        {
            string id = Plugin.TestSoundId.Value;
            if (string.IsNullOrEmpty(id)) return;
            if (_service == null) return;

            try
            {
                var kb = UnityEngine.InputSystem.Keyboard.current;
                if (kb != null && kb.oKey.wasPressedThisFrame && UnityEngine.Time.unscaledTime >= _nextTestAllowed)
                {
                    _nextTestAllowed = UnityEngine.Time.unscaledTime + 0.5f;
                    _service.Play2D(id, Plugin.Volume.Value, 1f);
                    Plugin.LogSource.LogInfo("[MaydayCallouts] Play2D 试听（按 O）: " + id);
                }
            }
            catch (Exception e)
            {
                Plugin.LogSource.LogWarning("[MaydayCallouts] Play2D 试听失败: " + e.Message);
            }
        }

        /// <summary>自测键：按 P 播 mod 自己的告警音（master_caution），定位播放链是否出声。</summary>
        static void PollSelfTest()
        {
            try
            {
                var kb = UnityEngine.InputSystem.Keyboard.current;
                if (kb != null && kb.pKey.wasPressedThisFrame && UnityEngine.Time.unscaledTime >= _nextSelfTest)
                {
                    _nextSelfTest = UnityEngine.Time.unscaledTime + 0.5f;
                    AudioPack.Play("master_caution");
                    Plugin.LogSource.LogInfo("[MaydayCallouts] 自测播放: master_caution（按 P，mod 音源路径）");
                }
            }
            catch { }
        }
    }
}
