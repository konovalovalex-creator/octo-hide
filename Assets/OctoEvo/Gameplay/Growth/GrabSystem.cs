using UnityEngine;

namespace OctoEvo
{
    /// <summary>
    /// Охота: щупальце выстреливает к добыче, хватает и подтягивает ко рту.
    /// Автозахват — мелочь прямо перед осьминогом в пределах ~¾ щупальца.
    /// Кнопка «Схватить» (ЛКМ / тап) — дальше и в любую сторону.
    /// Крупная добыча бьётся дольше и замедляет осьминога.
    /// </summary>
    public class GrabSystem : MonoBehaviour
    {
        public OctopusMotor motor;
        public TentacleRig rig;
        public GrowthSystem growth;
        [Tooltip("Точка рта в локальных координатах осьминога")]
        public Vector3 mouthLocal = new Vector3(0f, -0.2f, 0.12f);

        [Header("Захват")]
        public bool autoGrab = true;
        [Tooltip("Дальность автозахвата в долях длины щупальца")]
        public float autoReach = 1.2f;
        [Tooltip("Дальность броска по кнопке, в длинах щупальца: осьминог прыгает к добыче")]
        public float manualReach = 3.5f;
        [Tooltip("Конус автозахвата: косинус угла от «вперёд»")]
        public float autoConeDot = 0.3f;
        public float reachTimeout = 0.9f;
        public float missCooldown = 0.6f;

        [Header("Поедание")]
        public float pullTime = 0.45f;
        public float eatTimeBase = 0.4f;
        public float eatTimePerRatio = 2.2f;

        public enum Phase { Idle, Reaching, Holding }
        public Phase Current { get; private set; }
        public Creature Target { get; private set; }
        /// <summary>0..1 — сколько осталось до конца поедания.</summary>
        public float EatProgress { get; private set; }

        public string LastMessage { get; private set; } = "";
        public float LastMessageTime { get; private set; } = -10f;

        int tentacle = -1, tentacle2 = -1;
        float timer, holdTime, cooldownUntil;
        Vector3 catchLocal;

        float Length => growth != null ? growth.Length : transform.lossyScale.x * 1.4f;

        void Awake()
        {
            if (motor == null) motor = GetComponent<OctopusMotor>();
            if (growth == null) growth = GetComponent<GrowthSystem>();
            if (rig == null) rig = GetComponentInChildren<TentacleRig>();
        }

        void OnDisable() => Release(false);

        void Update()
        {
            if (rig == null) return;
            bool manual = motor == null || motor.isLocalPlayer ? OctoInput.AttackPressed : false;

            switch (Current)
            {
                case Phase.Idle: UpdateIdle(manual); break;
                case Phase.Reaching: UpdateReaching(); break;
                case Phase.Holding: UpdateHolding(); break;
            }
        }

        void UpdateIdle(bool manual)
        {
            if (Time.time < cooldownUntil) return;
            if (!manual && !autoGrab) return;

            float L = Length;
            float reach = rig.WorldLength * (manual ? manualReach : autoReach);
            Vector3 pos = transform.position;
            Vector3 fwd = transform.forward;

            Creature best = null;
            float bestScore = float.MaxValue;
            Creature tooBig = null;
            for (int i = 0; i < Creature.All.Count; i++)
            {
                var c = Creature.All[i];
                if (!c.IsAvailable) continue;
                Vector3 to = c.transform.position - pos;
                float d = to.magnitude - c.Radius;
                if (d > reach) continue;
                if (c.Length / L > GrowthSystem.EdibleRatio)
                {
                    tooBig = c;
                    continue;
                }
                float dot = d > 1e-4f ? Vector3.Dot(fwd, to.normalized) : 1f;
                if (!manual && dot < autoConeDot && d > reach * 0.35f) continue;
                float score = d - dot * reach * 0.3f;
                if (score < bestScore) { bestScore = score; best = c; }
            }

            if (best == null)
            {
                if (manual)
                {
                    Say(tooBig != null ? $"{tooBig.Def.displayName} слишком крупная для тебя" : "Никого рядом");
                    cooldownUntil = Time.time + 0.25f;
                }
                return;
            }

            Target = best;
            tentacle = rig.ReachTo(best.transform);
            if (tentacle < 0) { Target = null; return; }
            AudioDirector.Play(Sfx.TentacleShot, transform.position, 0.7f);
            // Далеко — бросок всем телом
            Vector3 toTarget = best.transform.position - pos;
            float arm = rig.WorldLength;
            if (motor != null && toTarget.magnitude > arm * 0.8f)
                motor.Lunge(toTarget.normalized * Mathf.Max(toTarget.magnitude / 0.3f, motor.swimSpeed * motor.SpeedScale) + best.Velocity * 0.5f);
            timer = 0f;
            Current = Phase.Reaching;
        }

        void UpdateReaching()
        {
            if (Target == null || !Target.IsAvailable)
            {
                Release(false);
                return;
            }
            timer += Time.deltaTime;
            Vector3 tip = rig.TipPosition(tentacle);
            float catchDist = Target.Radius + rig.WorldLength * 0.2f;
            float bodyDist = Target.Radius + rig.WorldLength * 0.6f;
            Vector3 tp = Target.transform.position;
            if ((tip - tp).sqrMagnitude < catchDist * catchDist || (transform.position - tp).sqrMagnitude < bodyDist * bodyDist)
            {
                Target.SetGrabbed(true);
                AudioDirector.Play(Sfx.Grab, Target.transform.position, 0.8f);
                catchLocal = transform.InverseTransformPoint(Target.transform.position);
                float r = Target.Length / Length;
                holdTime = Mathf.Max(pullTime, eatTimeBase + r * eatTimePerRatio);
                // Крупную добычу держим двумя щупальцами
                tentacle2 = r > 0.35f ? rig.ReachTo(Target.transform) : -1;
                timer = 0f;
                Current = Phase.Holding;
                return;
            }

            float maxRange = rig.WorldLength * manualReach * 1.6f + Target.Radius;
            if (timer > reachTimeout || (Target.transform.position - transform.position).sqrMagnitude > maxRange * maxRange)
            {
                Say("Промах!");
                AudioDirector.Play(Sfx.Miss, transform.position, 0.6f);
                Release(false);
                cooldownUntil = Time.time + missCooldown;
            }
        }

        void UpdateHolding()
        {
            if (Target == null || !Target.isActiveAndEnabled)
            {
                Release(false);
                return;
            }
            timer += Time.deltaTime;
            float r = Target.Length / Length;
            float k = Mathf.Clamp01(timer / pullTime);
            k = k * k * (3f - 2f * k);
            EatProgress = Mathf.Clamp01(timer / holdTime);

            Vector3 local = Vector3.Lerp(catchLocal, mouthLocal, k);
            float shake = (1f - EatProgress) * Mathf.Clamp01(r * 3f);
            Vector3 jitter = new Vector3(Mathf.Sin(Time.time * 37f), Mathf.Sin(Time.time * 29f + 1f), 0f) * 0.03f * shake;
            Vector3 wanted = transform.TransformPoint(local + jitter);
            Target.transform.position = Vector3.Lerp(Target.transform.position, wanted, 1f - Mathf.Exp(-14f * Time.deltaTime));
            Target.Struggle(shake);

            if (motor != null) motor.externalSpeedMultiplier = Mathf.Lerp(1f, 0.35f, Mathf.Clamp01(r / GrowthSystem.EdibleRatio));

            if (timer >= holdTime)
            {
                var prey = Target;
                if (growth != null) growth.Eat(prey);
                AudioDirector.Play(Sfx.Eat, prey.transform.position, 0.8f);
                Release(true);
                if (prey.Director != null) prey.Director.Despawn(prey);
                else prey.gameObject.SetActive(false);
            }
        }

        void Release(bool eaten)
        {
            if (rig != null)
            {
                rig.ClearReach(tentacle);
                rig.ClearReach(tentacle2);
            }
            if (!eaten && Target != null && Current == Phase.Holding) Target.SetGrabbed(false);
            tentacle = tentacle2 = -1;
            Target = null;
            EatProgress = 0f;
            Current = Phase.Idle;
            if (motor != null) motor.externalSpeedMultiplier = 1f;
        }

        void Say(string msg)
        {
            LastMessage = msg;
            LastMessageTime = Time.time;
        }
    }
}
