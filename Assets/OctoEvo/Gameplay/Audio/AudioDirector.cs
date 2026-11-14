using System;
using System.Collections.Generic;
using UnityEngine;

namespace OctoEvo
{
    public enum Sfx
    {
        Jet, Crawl, TentacleShot, Grab, Eat, Ink, Miss, NewPrey, Growth, Bubble, FishScare, BigFish, Crab, Land
    }

    /// <summary>
    /// Весь звук игры: пул источников для эффектов, фоновые петли, музыка с плавной сменой по глубине.
    /// Геймплей зовёт <see cref="Play"/> и не думает об AudioSource.
    /// </summary>
    public class AudioDirector : MonoBehaviour
    {
        public SoundBank bank;
        public OctopusMotor player;
        public GrowthSystem growth;
        [Range(0, 1)] public float sfxVolume = 1f;
        [Range(0, 1)] public float ambientVolume = 0.7f;
        [Range(0, 1)] public float musicVolume = 0.45f;
        [Tooltip("Глубже этой отметки (м под поверхностью) играет «глубинная» музыка")]
        public float deepMusicDepth = 14f;

        public static AudioDirector Instance { get; private set; }

        readonly List<AudioSource> pool = new List<AudioSource>();
        readonly Dictionary<Sfx, SoundBank.Entry> map = new Dictionary<Sfx, SoundBank.Entry>();
        readonly Dictionary<Sfx, float> lastPlayed = new Dictionary<Sfx, float>();
        AudioSource hum, waterA, waterB, reef, swim, musicA, musicB;
        AudioClip currentMusic;
        float musicFade = 1f;
        int next;

        void Awake()
        {
            Instance = this;
            if (bank == null) return;
            if (bank.effects != null)
                foreach (var e in bank.effects)
                    if (e != null && e.clips != null && e.clips.Length > 0) map[e.id] = e;

            for (int i = 0; i < 24; i++)
            {
                var go = new GameObject("Sfx" + i);
                go.transform.SetParent(transform, false);
                var src = go.AddComponent<AudioSource>();
                src.playOnAwake = false;
                src.rolloffMode = AudioRolloffMode.Logarithmic;
                src.dopplerLevel = 0f;
                pool.Add(src);
            }

            hum = Loop("Hum", bank.hum, 0.55f);
            waterA = Loop("WaterA", bank.waterA, 0.35f);
            waterB = Loop("WaterB", bank.waterB, 0f);
            reef = Loop("Reef", bank.reef, 0.25f);
            swim = Loop("Swim", bank.swimLoop, 0f);
            musicA = Loop("MusicA", null, 0f);
            musicB = Loop("MusicB", null, 0f);
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        AudioSource Loop(string name, AudioClip clip, float vol)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var s = go.AddComponent<AudioSource>();
            s.loop = true;
            s.spatialBlend = 0f;
            s.playOnAwake = false;
            s.clip = clip;
            s.volume = vol;
            if (clip != null)
            {
                s.time = UnityEngine.Random.Range(0f, clip.length * 0.9f);
                s.Play();
            }
            return s;
        }

        /// <summary>Сыграть эффект. Для интерфейса позиция не важна.</summary>
        public static void Play(Sfx id, Vector3 position, float volume = 1f, float pitch = 1f)
        {
            var d = Instance;
            if (d != null) d.PlayInternal(id, position, volume, pitch);
        }

        void PlayInternal(Sfx id, Vector3 position, float volume, float pitch)
        {
            if (!map.TryGetValue(id, out var e)) return;
            if (lastPlayed.TryGetValue(id, out float t) && Time.time - t < e.minInterval) return;
            lastPlayed[id] = Time.time;

            var src = pool[next];
            next = (next + 1) % pool.Count;
            src.transform.position = position;
            src.clip = e.clips[UnityEngine.Random.Range(0, e.clips.Length)];
            src.volume = e.volume * volume * sfxVolume;
            src.pitch = pitch * (1f + UnityEngine.Random.Range(-e.pitchJitter, e.pitchJitter));
            src.spatialBlend = e.spatial;
            // Дальность слышимости растёт с размером игрока: маленький слышит только ближнее
            float L = growth != null ? growth.Length : 0.1f;
            src.minDistance = Mathf.Max(0.3f, L * 4f);
            src.maxDistance = Mathf.Max(15f, L * 120f);
            src.Play();
        }

        void Update()
        {
            if (bank == null) return;
            float dt = Time.deltaTime;

            // Плавание: шелест воды по скорости
            if (swim != null && player != null)
            {
                float target = player.IsSwimming ? Mathf.Clamp01(player.Speed / Mathf.Max(0.1f, player.swimSpeed * player.SpeedScale)) * 0.5f : 0f;
                swim.volume = Mathf.MoveTowards(swim.volume, target * ambientVolume, dt);
                if (swim.clip != null && !swim.isPlaying) swim.Play();
            }

            // Глубина: меньше рифа, больше тёмной воды
            float depth = 0f;
            if (player != null && UnderwaterEnvironment.Instance != null)
                depth = Mathf.Max(0f, UnderwaterEnvironment.Instance.surfaceY - player.transform.position.y);
            float deep = Mathf.InverseLerp(6f, deepMusicDepth + 6f, depth);
            if (hum != null) hum.volume = Mathf.MoveTowards(hum.volume, Mathf.Lerp(0.5f, 0.75f, deep) * ambientVolume, dt * 0.3f);
            if (waterA != null) waterA.volume = Mathf.MoveTowards(waterA.volume, (1f - deep) * 0.35f * ambientVolume, dt * 0.3f);
            if (waterB != null)
            {
                waterB.volume = Mathf.MoveTowards(waterB.volume, deep * 0.4f * ambientVolume, dt * 0.3f);
                if (waterB.clip != null && !waterB.isPlaying && waterB.volume > 0.01f) waterB.Play();
            }
            if (reef != null) reef.volume = Mathf.MoveTowards(reef.volume, (1f - deep) * 0.25f * ambientVolume, dt * 0.3f);

            // Музыка
            AudioClip want = depth > deepMusicDepth && bank.musicDeep != null ? bank.musicDeep : bank.musicReef;
            if (growth != null && growth.Length >= 2.5f && bank.musicKraken != null) want = bank.musicKraken;
            if (want != currentMusic)
            {
                currentMusic = want;
                var tmp = musicA; musicA = musicB; musicB = tmp; // A — новая, B — уходящая
                musicA.clip = want;
                musicA.volume = 0f;
                if (want != null) musicA.Play();
                musicFade = 0f;
            }
            musicFade = Mathf.MoveTowards(musicFade, 1f, dt / 4f);
            musicA.volume = musicFade * musicVolume;
            musicB.volume = (1f - musicFade) * musicVolume;
            if (musicFade >= 1f && musicB.isPlaying) musicB.Stop();
        }
    }
}
