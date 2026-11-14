using System.Collections.Generic;
using UnityEngine;

namespace OctoEvo
{
    /// <summary>
    /// Чернила: облако за спиной + рывок вперёд. Существа рядом теряют ориентацию,
    /// а осьминог внутри облака невидим для них.
    /// </summary>
    public class InkAbility : MonoBehaviour
    {
        public OctopusMotor motor;
        public GrowthSystem growth;
        public Material inkMaterial;
        public float cooldown = 8f;
        public float cloudLifetime = 6f;
        [Tooltip("Радиус облака в длинах осьминога")]
        public float radiusPerLength = 2.5f;
        public float jetBoost = 1.4f;
        public float confuseSeconds = 3f;

        public float CooldownLeft => Mathf.Max(0f, nextUse - Time.time);
        public string LastMessage { get; private set; } = "";
        public float LastMessageTime { get; private set; } = -10f;

        float nextUse;

        void Awake()
        {
            if (motor == null) motor = GetComponent<OctopusMotor>();
            if (growth == null) growth = GetComponent<GrowthSystem>();
        }

        void Update()
        {
            if (motor != null && !motor.isLocalPlayer) return;
            if (!OctoInput.InkPressed) return;
            if (Time.time < nextUse)
            {
                LastMessage = $"Чернила будут через {CooldownLeft:0} с";
                LastMessageTime = Time.time;
                return;
            }
            Release();
        }

        public void Release()
        {
            nextUse = Time.time + cooldown;
            float L = growth != null ? growth.Length : transform.lossyScale.x * 1.4f;
            float radius = Mathf.Max(0.12f, L * radiusPerLength);
            Vector3 at = transform.position - transform.forward * L * 0.3f;

            InkCloud.Spawn(at, radius, cloudLifetime, inkMaterial, motor != null ? -motor.Velocity * 0.2f : Vector3.zero);

            for (int i = 0; i < Creature.All.Count; i++)
            {
                var c = Creature.All[i];
                if ((c.transform.position - at).sqrMagnitude < radius * radius * 2.5f) c.Confuse(confuseSeconds);
            }

            if (motor != null) motor.RequestJet(jetBoost, true);
            LastMessage = "Чернила!";
            LastMessageTime = Time.time;
        }
    }

    /// <summary>Облако чернил: тёмные мягкие частицы, расплываются и бледнеют.</summary>
    public class InkCloud : MonoBehaviour
    {
        static readonly List<InkCloud> active = new List<InkCloud>();

        float radius, born, life;

        /// <summary>Точка внутри плотной части какого-нибудь облака.</summary>
        public static bool Contains(Vector3 p)
        {
            for (int i = 0; i < active.Count; i++)
            {
                var c = active[i];
                float age = (Time.time - c.born) / c.life;
                float r = c.radius * Mathf.Lerp(0.8f, 1.3f, age) * (1f - age * 0.6f);
                if ((p - c.transform.position).sqrMagnitude < r * r) return true;
            }
            return false;
        }

        public static InkCloud Spawn(Vector3 position, float radius, float lifetime, Material material, Vector3 drift)
        {
            var go = new GameObject("InkCloud");
            go.transform.position = position;
            var cloud = go.AddComponent<InkCloud>();
            cloud.radius = radius;
            cloud.born = Time.time;
            cloud.life = lifetime;

            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            bool low = UnderwaterEnvironment.IsLowQuality;

            var main = ps.main;
            main.duration = 0.3f;
            main.loop = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(lifetime * 0.6f, lifetime);
            main.startSpeed = new ParticleSystem.MinMaxCurve(radius * 0.3f, radius * 1.2f);
            main.startSize = new ParticleSystem.MinMaxCurve(radius * 0.7f, radius * 1.4f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.05f, 0.04f, 0.035f, 0.9f), new Color(0.12f, 0.08f, 0.06f, 0.8f));
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = low ? 24 : 48;
            main.gravityModifier = -0.002f;

            var emission = ps.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)(low ? 18 : 36)), new ParticleSystem.Burst(0.15f, (short)(low ? 6 : 12)) });

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = radius * 0.25f;

            var vel = ps.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(drift.x);
            vel.y = new ParticleSystem.MinMaxCurve(drift.y + radius * 0.02f);
            vel.z = new ParticleSystem.MinMaxCurve(drift.z);

            var limit = ps.limitVelocityOverLifetime;
            limit.enabled = true;
            limit.limit = radius * 0.05f;
            limit.dampen = 0.15f;

            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.7f, 1f, 1.8f));

            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(0.7f, 0.75f, 0.8f), 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.05f), new GradientAlphaKey(0.7f, 0.5f), new GradientAlphaKey(0f, 1f) });
            col.color = g;

            var rot = ps.rotationOverLifetime;
            rot.enabled = true;
            rot.z = new ParticleSystem.MinMaxCurve(-0.3f, 0.3f);

            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = material;
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.sortMode = ParticleSystemSortMode.Distance;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;

            ps.Play();
            Destroy(go, lifetime + 0.5f);
            return cloud;
        }

        void OnEnable() => active.Add(this);
        void OnDisable() => active.Remove(this);
    }
}
