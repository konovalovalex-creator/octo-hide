#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace OctoEvo.EditorTools
{
    /// <summary>
    /// Собирает SoundBank из Assets/OctoEvo/Audio. Клипы раскладываются по имени файла
    /// (Jet_1.wav, Jet_2.wav… → Sfx.Jet). Громкость и прочие настройки, если ассет уже есть, не трогаем.
    /// </summary>
    public static class AudioSetup
    {
        const string Dir = "Assets/OctoEvo/Audio";
        const string BankPath = "Assets/OctoEvo/Data/SoundBank.asset";

        // id, громкость, разброс тона, мин. интервал, объёмность
        static readonly (Sfx id, float vol, float pitch, float interval, float spatial)[] Defaults =
        {
            (Sfx.Jet, 0.8f, 0.08f, 0.1f, 0.6f),
            (Sfx.Crawl, 0.35f, 0.2f, 0.07f, 1f),
            (Sfx.TentacleShot, 0.7f, 0.1f, 0.05f, 0.7f),
            (Sfx.Grab, 0.8f, 0.1f, 0.05f, 0.8f),
            (Sfx.Eat, 0.8f, 0.1f, 0.1f, 0.6f),
            (Sfx.Ink, 0.9f, 0.05f, 0.5f, 0.6f),
            (Sfx.Miss, 0.6f, 0.08f, 0.2f, 0.2f),
            (Sfx.NewPrey, 0.7f, 0f, 1f, 0f),
            (Sfx.Growth, 0.8f, 0.03f, 1f, 0f),
            (Sfx.Bubble, 0.5f, 0.15f, 0.06f, 0f),
            (Sfx.FishScare, 0.6f, 0.12f, 0.4f, 1f),
            (Sfx.BigFish, 0.8f, 0.08f, 2f, 1f),
            (Sfx.Crab, 0.6f, 0.12f, 0.5f, 1f),
            (Sfx.Land, 0.5f, 0.15f, 0.25f, 0.8f),
        };

        public static SoundBank EnsureBank()
        {
            ProceduralAssets.EnsureFolder("Assets/OctoEvo/Data");
            ConfigureImporters();
            var bank = AssetDatabase.LoadAssetAtPath<SoundBank>(BankPath);
            bool fresh = bank == null;
            if (fresh)
            {
                bank = ScriptableObject.CreateInstance<SoundBank>();
                AssetDatabase.CreateAsset(bank, BankPath);
            }

            var entries = new List<SoundBank.Entry>(bank.effects ?? new SoundBank.Entry[0]);
            foreach (var d in Defaults)
            {
                var e = entries.Find(x => x != null && x.id == d.id);
                if (e == null)
                {
                    e = new SoundBank.Entry { id = d.id, volume = d.vol, pitchJitter = d.pitch, minInterval = d.interval, spatial = d.spatial };
                    entries.Add(e);
                }
                var clips = new List<AudioClip>();
                for (int i = 1; i <= 8; i++)
                {
                    var c = AssetDatabase.LoadAssetAtPath<AudioClip>($"{Dir}/Sfx/{d.id}_{i}.wav");
                    if (c != null) clips.Add(c);
                }
                if (clips.Count > 0 || e.clips == null) e.clips = clips.ToArray();
            }
            bank.effects = entries.ToArray();

            bank.hum = Clip("Ambient/Hum.ogg", bank.hum);
            bank.waterA = Clip("Ambient/WaterA.ogg", bank.waterA);
            bank.waterB = Clip("Ambient/WaterB.ogg", bank.waterB);
            bank.reef = Clip("Ambient/Reef.ogg", bank.reef);
            bank.swimLoop = Clip("Ambient/SwimLoop.ogg", bank.swimLoop);
            bank.musicReef = Clip("Music/Reef.ogg", bank.musicReef);
            bank.musicDeep = Clip("Music/Deep.ogg", bank.musicDeep);
            bank.musicKraken = Clip("Music/Kraken.ogg", bank.musicKraken);
            EditorUtility.SetDirty(bank);
            AssetDatabase.SaveAssets();
            return bank;
        }

        static AudioClip Clip(string rel, AudioClip current)
        {
            var c = AssetDatabase.LoadAssetAtPath<AudioClip>($"{Dir}/{rel}");
            return c != null ? c : current;
        }

        /// <summary>Эффекты — в память целиком, фон и музыка — потоком (экономит память телефона).</summary>
        static void ConfigureImporters()
        {
            if (!Directory.Exists(Dir)) return;
            foreach (var guid in AssetDatabase.FindAssets("t:AudioClip", new[] { Dir }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var ai = AssetImporter.GetAtPath(path) as AudioImporter;
                if (ai == null) continue;
                bool stream = path.Contains("/Music/") || path.Contains("/Ambient/");
                var s = ai.defaultSampleSettings;
                s.loadType = stream ? AudioClipLoadType.Streaming : AudioClipLoadType.DecompressOnLoad;
                s.compressionFormat = AudioCompressionFormat.Vorbis;
                s.quality = stream ? 0.6f : 0.8f;
                if (ai.forceToMono != !stream || !ai.defaultSampleSettings.Equals(s))
                {
                    ai.forceToMono = !stream;
                    ai.defaultSampleSettings = s;
                    ai.SaveAndReimport();
                }
            }
        }
    }
}
#endif
