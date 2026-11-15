using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using BepInEx;
using UnityEngine;
using Il2CppInterop.Runtime.InteropTypes.Arrays;

namespace MaydayCallouts
{
    /// <summary>
    /// 外置音频包加载器 + SoundBank 注册器。
    ///
    /// 音效文件放 BepInEx\plugins\MaydayCallouts\Audio\*.wav（16bit PCM），
    /// 文件名（不含扩展名）即音效名：stall_horn / gear_horn / overspeed /
    /// fire_bell / master_caution / low_fuel / depress_horn /
    /// chime_seatbelt / chime_notify / callout_100 / callout_80 / ...
    ///
    /// 加载：手动解析 RIFF/PCM（16bit）+ AudioClip.Create + SetData
    ///（interop 的 GetAudioClip 引用了不存在的 DownloadHandlerAudioClip
    /// 构造，实测 MissingMethodException，不能用）。
    ///
    /// 播放：把自制音效**注册进游戏的 SoundBank**（defs/defList/clips 三张表）
    /// 后，统一用 AudioService.Play2D 播放 —— 那是游戏自己验证过出声的管线
    ///（ui_click/chime_seatbelt 就是这么响的）。循环类告警靠 def.cooldown
    /// + 每 250ms 重触发实现"持续响"，条件解除后最后一声自然收尾，
    /// 不需要 Stop（绕开拿不到 voice id 的问题）。
    ///
    /// mod 仓库不含任何音频内容 —— 音源自备（自购/CC0/自制），
    /// 自带的合成音效包可用 tools\gen_sounds.py 重新生成。
    /// </summary>
    internal static class AudioPack
    {
        const string Folder = "MaydayCallouts";
        const string Prefix = "mco_";

        static readonly Dictionary<string, AudioClip> _clips = new Dictionary<string, AudioClip>();
        static bool _loaded;
        static bool _registered;
        static global::Mayday.Audio.SoundBank _bank;
        static global::Mayday.Audio.AudioService _service;

        static string AudioDir => Path.Combine(Paths.PluginPath, Folder, "Audio");

        public static bool Loaded => _loaded;
        public static bool Registered => _registered;

        public static void StartLoad()
        {
            if (_loaded) return;
            _loaded = true; // 同步加载，一次完成

            string dir = AudioDir;
            if (!Directory.Exists(dir))
            {
                Plugin.LogSource.LogInfo("[MaydayCallouts] 音效目录不存在（" + dir + "），跳过音频加载");
                return;
            }

            int ok = 0, fail = 0;
            foreach (var path in Directory.GetFiles(dir, "*.wav"))
            {
                string name = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
                try
                {
                    var clip = LoadWav(path, name);
                    if (clip != null) { _clips[name] = clip; ok++; }
                    else { fail++; Plugin.LogSource.LogWarning("[MaydayCallouts] WAV 解析失败（需 16bit PCM）: " + name); }
                }
                catch (Exception e)
                {
                    fail++;
                    Plugin.LogSource.LogWarning("[MaydayCallouts] 音频加载失败 " + name + ": " + e.Message);
                }
            }

            if (ok > 0)
                Plugin.LogSource.LogInfo("[MaydayCallouts] 音频包解析完成: " + ok + " 个音效待注册（" + (fail > 0 ? fail + " 个失败）" : "无失败）"));
            else
                Plugin.LogSource.LogWarning("[MaydayCallouts] 没有成功解析任何音效");
        }

        /// <summary>每帧驱动注册（等 SoundBank 就绪）。注册完成后变空操作。</summary>
        public static void Tick()
        {
            if (_registered || _clips.Count == 0) return;
            try
            {
                // SoundBank 不是 Unity 对象（Il2CppSystem.Object），只能从单例拿
                if (_service == null) _service = global::Mayday.Audio.AudioService.Instance;
                if (_service == null) return;
                if (_bank == null) _bank = _service.Bank;
                if (_bank == null) return;
                RegisterToBank(_bank);
            }
            catch (Exception e)
            {
                Plugin.LogSource.LogWarning("[MaydayCallouts] SoundBank 注册失败: " + e.Message);
                _registered = true; // 不再重试，回退到静默（不炸游戏）
            }
        }

        /// <summary>把自制音效注册进游戏的 SoundBank 三张表。</summary>
        static void RegisterToBank(global::Mayday.Audio.SoundBank bank)
        {
            var defs = global::Mayday.Audio.SoundBank.defs;       // static Dictionary<string, SoundDef>
            var defList = global::Mayday.Audio.SoundBank.defList; // static List<SoundDef>
            if (defs == null || defList == null)
            {
                Plugin.LogSource.LogWarning("[MaydayCallouts] SoundBank.defs/defList 为空，注册跳过");
                _registered = true;
                return;
            }

            int registered = 0;
            foreach (var kv in _clips)
            {
                string name = kv.Key;
                string gameId = Prefix + name;
                try
                {
                    if (defs.TryGetValue(gameId, out var existing) && existing != null) continue; // 已注册

                    var def = new global::Mayday.Audio.SoundDef();
                    def.id = gameId;
                    def.category = global::Mayday.Audio.SoundCategory.Sfx;
                    def.volume = 1f;
                    def.maxInstances = 3;

                    // 循环类（喇叭/铃/抖杆）：cooldown = 音频时长 - 0.1s
                    // —— 250ms 重触发会被冷却吞掉，直到上一声快放完才接上，
                    //    自然形成"按音频实际长度循环"的节奏（如 3s 的 STALL 语音）
                    bool loopType = name.Contains("horn") || name.Contains("bell") || name == "overspeed" || name == "depress_horn";
                    float clipLen = 1f;
                    try { clipLen = kv.Value != null ? kv.Value.length : 1f; } catch { }
                    def.cooldown = loopType ? Math.Max(1.0f, clipLen - 0.1f) : 0.2f;
                    def.loopClip = false;

                    defs[gameId] = def;
                    defList.Add(def);

                    // 实例 clips 表：id → clip 数组（Get(id, variation) 从这里取）
                    var arr = new Il2CppReferenceArray<UnityEngine.AudioClip>(1);
                    arr[0] = kv.Value;
                    bank.clips[gameId] = arr;

                    registered++;
                }
                catch (Exception e)
                {
                    Plugin.LogSource.LogWarning("[MaydayCallouts] 注册失败 " + name + ": " + e.Message);
                }
            }

            _registered = true;
            if (registered > 0)
                Plugin.LogSource.LogInfo("[MaydayCallouts] SoundBank 注册完成: " + registered + " 个音效（前缀 " + Prefix + "）");
            else
                Plugin.LogSource.LogWarning("[MaydayCallouts] 没有新注册的音效（可能已注册过）");
        }

        /// <summary>音效是否已注册（存在对应音频文件）。</summary>
        public static bool Has(string name)
        {
            return _registered && _clips.ContainsKey(name);
        }

        /// <summary>
        /// 被屏蔽的游戏原声接替播放：游戏侧同名音源存在时，屏蔽原声后由 mod 接管
        /// （如游戏 ap_disconnect → 我们注册的波音 737 AP disconnect）。
        /// </summary>
        public static void TryReplace(string gameId)
        {
            try
            {
                if (!string.IsNullOrEmpty(gameId) && _clips.ContainsKey(gameId.ToLowerInvariant()))
                    Play(gameId.ToLowerInvariant());
            }
            catch { }
        }

        /// <summary>
        /// 播放自制音效（走游戏 Play2D 管线）。循环类告警在条件持续期间
        /// 每 250ms 重触发一次，def.cooldown 会吞掉多余的触发。
        /// </summary>
        public static void Play(string name)
        {
            try
            {
                if (!_registered) return;
                if (_service == null) _service = global::Mayday.Audio.AudioService.Instance;
                if (_service == null) return;
                _service.Play2D(Prefix + name, Math.Min(1f, Math.Max(0f, Plugin.Volume.Value)), 1f);
            }
            catch { }
        }

        /// <summary>
        /// 手动解析 RIFF/WAVE（16bit PCM），返回 AudioClip。
        /// fmt 块取 channels/sampleRate/bits，data 块取采样。
        /// </summary>
        static AudioClip LoadWav(string path, string name)
        {
            byte[] all = File.ReadAllBytes(path);
            if (all.Length < 44 || Encoding.ASCII.GetString(all, 0, 4) != "RIFF") return null;

            int channels = 0, sampleRate = 0, bits = 0;
            byte[] pcm = null;
            int pos = 12; // 跳过 RIFFxxxxWAVE
            while (pos + 8 <= all.Length)
            {
                string cid = Encoding.ASCII.GetString(all, pos, 4);
                int size = BitConverter.ToInt32(all, pos + 4);
                pos += 8;
                if (cid == "fmt ")
                {
                    channels = BitConverter.ToInt16(all, pos + 2);
                    sampleRate = BitConverter.ToInt32(all, pos + 4);
                    bits = BitConverter.ToInt16(all, pos + 14);
                }
                else if (cid == "data")
                {
                    int take = Math.Min(size, all.Length - pos);
                    pcm = new byte[take];
                    Buffer.BlockCopy(all, pos, pcm, 0, take);
                }
                pos += size + (size & 1); // chunk 按字对齐
                if (channels > 0 && pcm != null) break;
            }

            if (pcm == null || channels <= 0 || sampleRate <= 0) return null;
            if (bits != 16) { Plugin.LogSource.LogWarning("[MaydayCallouts] 仅支持 16bit PCM: " + name); return null; }

            int frames = pcm.Length / 2 / channels;
            if (frames <= 0) return null;

            var floats = new float[frames * channels];
            for (int i = 0; i < floats.Length; i++)
                floats[i] = BitConverter.ToInt16(pcm, i * 2) / 32768f;

            var clip = AudioClip.Create(name, frames, channels, sampleRate, false);
            if (clip == null) return null;
            clip.SetData(floats, 0);
            return clip;
        }
    }
}
