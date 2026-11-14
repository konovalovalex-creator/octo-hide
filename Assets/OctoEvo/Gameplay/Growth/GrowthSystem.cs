using System;
using System.Collections.Generic;
using UnityEngine;

namespace OctoEvo
{
    /// <summary>
    /// Рост от съеденного. Формулы из плана MVP:
    ///   длина     L = L0 · (1 + B / B0)^(1/3)
    ///   масса     добычи = (Lдобычи / 0.1)³
    ///   усвоение  E(r) = clamp01((r − 0.05) / 0.35), r = Lдобычи / Lосьминога
    ///   съедобно  если r ≤ 0.7
    /// Масштаб модели осьминога = L / modelLength, меняется плавно.
    /// </summary>
    public class GrowthSystem : MonoBehaviour
    {
        public const float EdibleRatio = 0.7f;

        [Tooltip("Начальная длина, м")]
        public float startLength = 0.1f;
        [Tooltip("B0 — биомасса, на которой масса осьминога удваивается")]
        public float baseBiomass = 1f;
        [Tooltip("Длина модели осьминога (от кончиков щупалец до мантии) при масштабе 1, м")]
        public float modelLength = 1.4f;
        [Tooltip("Какая доля массы добычи превращается в биомассу")]
        public float efficiency = 0.6f;
        [Tooltip("Предел роста в MVP, м")]
        public float maxLength = 3f;
        public float growSpeed = 1.5f;

        [Tooltip("Виды, о которых сообщать «теперь по зубам»")]
        public CreatureDefinition[] milestones;

        public float Biomass { get; private set; }
        /// <summary>Длина по формуле (цель роста).</summary>
        public float Length => Mathf.Min(maxLength, startLength * Mathf.Pow(1f + Biomass / baseBiomass, 1f / 3f));
        /// <summary>Длина, которую видно сейчас (догоняет Length плавно).</summary>
        public float VisibleLength { get; private set; }
        public int Eaten { get; private set; }

        public string LastMessage { get; private set; } = "";
        public float LastMessageTime { get; private set; } = -10f;

        public event Action<Creature, float> OnAte;

        float nextGrowthSound;
        readonly HashSet<CreatureDefinition> announced = new HashSet<CreatureDefinition>();

        public static float Yield(float ratio) => Mathf.Clamp01((ratio - 0.05f) / 0.35f);
        public bool CanEat(float preyLength) => preyLength / Mathf.Max(0.001f, Length) <= EdibleRatio;

        void Awake()
        {
            VisibleLength = Length;
            nextGrowthSound = Length * 1.25f;
            ApplyScale();
            if (milestones != null)
                foreach (var m in milestones)
                    if (m != null && CanEat(m.minLength)) announced.Add(m);
        }

        void Update()
        {
            float target = Length;
            if (!Mathf.Approximately(VisibleLength, target))
            {
                VisibleLength = Mathf.MoveTowards(VisibleLength, target, Mathf.Max(0.002f, Mathf.Abs(target - VisibleLength)) * growSpeed * Time.deltaTime);
                ApplyScale();
            }
        }

        void ApplyScale()
        {
            float s = VisibleLength / modelLength;
            transform.localScale = Vector3.one * s;
        }

        /// <summary>Съесть существо. Возвращает прибавку биомассы.</summary>
        public float Eat(Creature prey)
        {
            float r = prey.Length / Mathf.Max(0.001f, Length);
            float gain = prey.Mass * Yield(r) * efficiency;
            float before = Length;
            Biomass += gain;
            Eaten++;

            string name = prey.Def != null ? prey.Def.displayName : "добыча";
            if (gain <= 0.0001f) Say($"{name}: слишком мелко, не насыщает");
            else Say($"{name}: +{gain:0.###} биомассы");

            CheckMilestones(before);
            // Каждые +25% длины — звук роста
            if (Length >= nextGrowthSound)
            {
                nextGrowthSound = Length * 1.25f;
                AudioDirector.Play(Sfx.Growth, transform.position);
            }
            OnAte?.Invoke(prey, gain);
            return gain;
        }

        void CheckMilestones(float before)
        {
            if (milestones == null) return;
            foreach (var m in milestones)
            {
                if (m == null || announced.Contains(m)) continue;
                if (CanEat(m.minLength))
                {
                    announced.Add(m);
                    Say($"Ты вырос до {FormatLength(Length)}! Теперь по силам: {m.displayName}");
                    AudioDirector.Play(Sfx.NewPrey, transform.position);
                }
            }
        }

        /// <summary>Сколько биомассы нужно для длины L.</summary>
        public float BiomassForLength(float length) => baseBiomass * (Mathf.Pow(length / startLength, 3f) - 1f);

        /// <summary>Ближайший ещё недоступный вид и прогресс 0..1 до него.</summary>
        public bool NextMilestone(out CreatureDefinition next, out float progress)
        {
            next = null;
            progress = 1f;
            if (milestones == null) return false;
            float best = float.MaxValue;
            foreach (var m in milestones)
            {
                if (m == null || CanEat(m.minLength)) continue;
                float need = m.minLength / EdibleRatio;
                if (need < best) { best = need; next = m; }
            }
            if (next == null) return false;
            float from = 0f;
            foreach (var m in milestones)
            {
                if (m == null || !CanEat(m.minLength)) continue;
                from = Mathf.Max(from, BiomassForLength(Mathf.Max(startLength, m.minLength / EdibleRatio)));
            }
            float to = BiomassForLength(best);
            progress = Mathf.Clamp01((Biomass - from) / Mathf.Max(1e-5f, to - from));
            return true;
        }

        public static string FormatLength(float meters) => meters < 1f ? $"{meters * 100f:0.#} см" : $"{meters:0.00} м";

        void Say(string msg)
        {
            LastMessage = msg;
            LastMessageTime = Time.time;
        }
    }
}
