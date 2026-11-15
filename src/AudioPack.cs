using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using BepInEx;
using UnityEngine;

namespace MaydayCallouts
{
    /// <summary>
    /// 外置音频包加载器 + 播放管理。
    ///
    /// 音效文件放 BepInEx\plugins\MaydayCallouts\Audio\*.wav（16bit PCM），
    /// 文件名（不含扩展名）即音效名：stall_horn / gear_horn / overspeed /
    /// fire_bell / master_caution / low_fuel / depress_horn /
    /// chime_seatbelt / chime_notify / callout_100 / callout_80 / ...
    ///
    /// 加载不用 UnityWebRequest —— interop 的 GetAudioClip 引用了不存在的
    /// DownloadHandlerAudioClip 构造（实测 MissingMethodException）。
    /// 改为手动解析 RIFF/PCM（16bit）+ AudioClip.Create + SetData，
    /// 全程同步、无协程依赖。ogg 暂不支持（自行转 wav）。
    ///
    /// mod 仓库不含任何音频内容 —— 音源自备（自购/CC0/自制），
    /// 自带的合成音效包可用 tools\gen_sounds.py 重新生成。
    /// </summary>
    internal static class AudioPack
    {
        const string Folder = "MaydayCallouts";

        static readonly Dictionary<string, AudioClip> _clips = new Dictionary<string, AudioClip>();
        static readonly Dictionary<string, AudioSource> _sources = new Dictionary<string, AudioSource>();
        static GameObject _root;
        static bool _loaded;

        static string AudioDir => Path.Combine(Paths.PluginPath, Folder, "Audio");

        public static bool Loaded => _loaded;

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
                Plugin.LogSource.LogInfo("[MaydayCallouts] 音频包加载完成: " + ok + " 个音效（" + (fail > 0 ? fail + " 个失败）" : "无失败）") + "\n  " + string.Join("\n  ", new List<string>(_clips.Keys).ToArray()));
            else
                Plugin.LogSource.LogWarning("[MaydayCallouts] 没有成功加载任何音效");
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

        static AudioSource GetSource(string name, bool loop)
        {
            if (_root == null)
            {
                _root = new GameObject("MaydayCalloutsAudio");
                UnityEngine.Object.DontDestroyOnLoad(_root);
            }
            if (!_sources.TryGetValue(name, out var src) || src == null)
            {
                var go = new GameObject("MCO_" + name);
                go.transform.SetParent(_root.transform, false);
                src = go.AddComponent<AudioSource>();
                src.playOnAwake = false;
                src.spatialBlend = 0f; // 2D：告警音全机可闻
                src.loop = false;
                _sources[name] = src;
            }
            if (_clips.TryGetValue(name, out var clip) && clip != null) src.clip = clip;
            src.loop = loop;
            src.volume = Math.Min(1f, Math.Max(0f, Plugin.Volume.Value));
            return src;
        }

        /// <summary>循环播放（告警条件持续期间）。已在播同名则不动。</summary>
        public static void PlayLoop(string name)
        {
            if (!Loaded) return;
            var src = GetSource(name, loop: true);
            if (src != null && !src.isPlaying) src.Play();
        }

        public static void StopLoop(string name)
        {
            if (_sources.TryGetValue(name, out var src) && src != null && src.isPlaying) src.Stop();
        }

        /// <summary>单次播放（一次性告警）。</summary>
        public static void PlayOnce(string name)
        {
            if (!Loaded) return;
            var src = GetSource(name, loop: false);
            if (src != null && src.clip != null) src.Play();
        }
    }
}
