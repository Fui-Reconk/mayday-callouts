using System;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using UnityEngine;
using UnityEngine.Networking;

namespace MaydayCallouts
{
    /// <summary>
    /// 外置音频包加载器 + 播放管理。
    ///
    /// 音效文件放 BepInEx\plugins\MaydayCallouts\Audio\*.wav / *.ogg，
    /// 文件名（不含扩展名）即音效名：stall_horn / gear_horn / overspeed /
    /// fire_bell / master_caution / low_fuel / depress_horn /
    /// chime_seatbelt / chime_notify / callout_100 / callout_80 / ...
    ///
    /// 加载：BepInEx 6 的 BasePlugin 不是 MonoBehaviour（没有协程），
    /// 所以 UnityWebRequest 异步操作由 Tick 轮询完成状态驱动 —— 无需协程。
    /// 播放用每个音效专属的 2D AudioSource（循环类告警 = loop 播放，
    /// 条件解除即 Stop）。
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
        static bool _started;

        // 轮询式加载队列
        static readonly Queue<KeyValuePair<string, UnityWebRequest>> _queue = new Queue<KeyValuePair<string, UnityWebRequest>>();
        static string _loadingName;
        static UnityWebRequest _loadingReq;

        static string AudioDir => Path.Combine(Paths.PluginPath, Folder, "Audio");

        public static bool Loaded => _loaded;

        public static void StartLoad()
        {
            if (_started) return;
            _started = true;

            string dir = AudioDir;
            if (!Directory.Exists(dir))
            {
                Plugin.LogSource.LogInfo("[MaydayCallouts] 音效目录不存在（" + dir + "），跳过音频加载");
                _loaded = true;
                return;
            }

            foreach (var path in Directory.GetFiles(dir, "*.wav")) Enqueue(path, AudioType.WAV);
            foreach (var path in Directory.GetFiles(dir, "*.ogg")) Enqueue(path, AudioType.OGGVORBIS);

            if (_queue.Count == 0)
            {
                Plugin.LogSource.LogWarning("[MaydayCallouts] 音效目录里没有 wav/ogg 文件");
                _loaded = true;
                return;
            }
            Plugin.LogSource.LogInfo("[MaydayCallouts] 开始加载音频包: " + _queue.Count + " 个文件");
            StartNext();
        }

        static void Enqueue(string path, AudioType type)
        {
            string name = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
            // file:// URL（UnityWebRequest 需要绝对 URI 字符串）
            string url = new Uri(path).AbsoluteUri;
            var req = UnityWebRequestMultimedia.GetAudioClip(url, type);
            _queue.Enqueue(new KeyValuePair<string, UnityWebRequest>(name, req));
        }

        static void StartNext()
        {
            _loadingName = null;
            _loadingReq = null;
            if (_queue.Count == 0) { Finish(); return; }
            var kv = _queue.Dequeue();
            _loadingName = kv.Key;
            _loadingReq = kv.Value;
            try { _loadingReq.SendWebRequest(); }
            catch (Exception e)
            {
                Plugin.LogSource.LogWarning("[MaydayCallouts] 音频请求启动失败 " + _loadingName + ": " + e.Message);
                StartNext(); // 跳过这个，继续下一个
            }
        }

        static void Finish()
        {
            if (_loaded) return;
            _loaded = true;
            if (_clips.Count > 0)
                Plugin.LogSource.LogInfo("[MaydayCallouts] 音频包加载完成: " + _clips.Count + " 个音效（" + string.Join(", ", new List<string>(_clips.Keys).ToArray()) + "）");
            else
                Plugin.LogSource.LogWarning("[MaydayCallouts] 音频包加载完成，但没有成功加载任何音效");
        }

        /// <summary>每帧驱动加载（Tick 调用）。全部完成后变成空操作。</summary>
        public static void Tick()
        {
            if (_loaded) return;
            if (_loadingReq == null) { StartNext(); return; }
            try
            {
                if (!_loadingReq.isDone) return;
                if (_loadingReq.result != UnityWebRequest.Result.Success)
                    Plugin.LogSource.LogWarning("[MaydayCallouts] 音频加载失败 " + _loadingName + ": " + _loadingReq.error);
                else
                {
                    var clip = DownloadHandlerAudioClip.GetContent(_loadingReq);
                    if (clip != null) _clips[_loadingName] = clip;
                    else Plugin.LogSource.LogWarning("[MaydayCallouts] 音频加载为空 " + _loadingName);
                }
            }
            catch (Exception e)
            {
                Plugin.LogSource.LogWarning("[MaydayCallouts] 音频加载异常 " + _loadingName + ": " + e.Message);
            }
            StartNext();
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
