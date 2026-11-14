using System;
using UnityEngine;

namespace OctoEvo
{
    /// <summary>Набор звуков. Ассет создаёт Build Reef Scene; клипы можно переназначить в инспекторе.</summary>
    [CreateAssetMenu(menuName = "OctoEvo/Sound Bank", fileName = "SoundBank")]
    public class SoundBank : ScriptableObject
    {
        [Serializable]
        public class Entry
        {
            public Sfx id;
            public AudioClip[] clips;
            [Range(0, 1)] public float volume = 0.8f;
            [Tooltip("Случайный разброс высоты тона, ±")]
            [Range(0, 0.5f)] public float pitchJitter = 0.08f;
            [Tooltip("Не чаще, чем раз в N секунд")]
            public float minInterval = 0.05f;
            [Tooltip("0 — звук «в голове» (интерфейс), 1 — объёмный в мире")]
            [Range(0, 1)] public float spatial = 1f;
        }

        public Entry[] effects;

        [Header("Фон")]
        public AudioClip hum;
        public AudioClip waterA;
        public AudioClip waterB;
        public AudioClip reef;
        public AudioClip swimLoop;

        [Header("Музыка")]
        public AudioClip musicReef;
        public AudioClip musicDeep;
        public AudioClip musicKraken;
    }
}
