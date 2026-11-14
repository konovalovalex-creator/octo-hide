using System.Collections.Generic;
using UnityEngine;

namespace OctoEvo
{
    /// <summary>
    /// Населяет риф вокруг игрока. Держит нужное число существ, подбирая виды под его размер:
    /// больше всего — подходящей добычи, немного — мелочи и крупных соседей.
    /// Далёкие существа убираются в пул и переиспользуются.
    /// </summary>
    public class SpawnDirector : MonoBehaviour
    {
        public CreatureDefinition[] species;
        public Material creatureMaterial;

        [Header("Игрок")]
        public OctopusMotor player;
        public GrowthSystem growth;
        public ConspicuityMeter meter;

        [Header("Область рифа")]
        public Vector3 areaCenter = Vector3.zero;
        public Vector2 areaSize = new Vector2(90f, 90f);

        [Header("Численность")]
        public int maxCreaturesPC = 160;
        public int maxCreaturesMobile = 80;
        [Tooltip("Дистанции для осьминога 10 см; растут вместе с ним")]
        public float spawnMinDistance = 0.8f;
        public float spawnMaxDistance = 6f;
        public float despawnDistance = 10f;
        public float tickInterval = 0.4f;

        public OctopusMotor Player => player;
        public float PlayerLength => growth != null ? growth.Length : 0.1f;
        public float PlayerConspicuity => meter != null ? meter.Value : 1f;
        public int ActiveCount => owned.Count;

        readonly List<Creature> owned = new List<Creature>();
        readonly Dictionary<CreatureDefinition, Stack<Creature>> pools = new Dictionary<CreatureDefinition, Stack<Creature>>();
        float nextTick;
        Transform poolRoot;

        int MaxCreatures => UnderwaterEnvironment.IsLowQuality ? maxCreaturesMobile : maxCreaturesPC;
        /// <summary>Чем больше осьминог, тем шире «пузырь» живого мира вокруг него.</summary>
        float RangeScale => Mathf.Clamp(Mathf.Sqrt(PlayerLength / 0.1f), 1f, 6f);

        void Start()
        {
            poolRoot = new GameObject("Creatures").transform;
            poolRoot.SetParent(transform, false);
            // Сразу населяем риф, в том числе рядом (но не вплотную)
            for (int i = 0; i < MaxCreatures * 2 && owned.Count < MaxCreatures; i++) TrySpawnGroup(true);
        }

        void Update()
        {
            if (Time.time < nextTick || player == null) return;
            nextTick = Time.time + tickInterval;

            Vector3 p = player.transform.position;
            float despawn = despawnDistance * RangeScale;
            for (int i = owned.Count - 1; i >= 0; i--)
            {
                var c = owned[i];
                if (c == null) { owned.RemoveAt(i); continue; }
                if (c.Current == Creature.State.Grabbed) continue;
                // мелочь издалека всё равно не видна — держим её ближе
                float limit = Mathf.Min(despawn, 2f + c.Length * 90f);
                if ((c.transform.position - p).sqrMagnitude > limit * limit) Despawn(c);
            }

            for (int n = 0; n < 6 && owned.Count < MaxCreatures; n++) TrySpawnGroup(false);
        }

        public Vector3 ClampToArea(Vector3 p)
        {
            Vector3 half = new Vector3(areaSize.x * 0.5f, 0f, areaSize.y * 0.5f);
            p.x = Mathf.Clamp(p.x, areaCenter.x - half.x, areaCenter.x + half.x);
            p.z = Mathf.Clamp(p.z, areaCenter.z - half.z, areaCenter.z + half.z);
            return p;
        }

        public void Despawn(Creature c)
        {
            if (c == null) return;
            owned.Remove(c);
            c.gameObject.SetActive(false);
            if (c.Def == null) return;
            if (!pools.TryGetValue(c.Def, out var stack)) pools[c.Def] = stack = new Stack<Creature>();
            stack.Push(c);
        }

        /// <summary>Вес вида при текущем размере игрока.</summary>
        float Weight(CreatureDefinition def)
        {
            float r = def.AverageLength / Mathf.Max(0.001f, PlayerLength);
            float w;
            if (r < 0.04f) w = 0.05f;                 // пыль под ногами
            else if (r < 0.12f) w = 0.4f;             // мелочь
            else if (r <= GrowthSystem.EdibleRatio) w = 1.4f; // самое то
            else if (r < 1.5f) w = 0.7f;              // «почти по зубам» — цель на вырост
            else w = 0.3f;                            // крупные соседи
            return w * Mathf.Max(0f, def.spawnWeight);
        }

        CreatureDefinition PickSpecies()
        {
            if (species == null || species.Length == 0) return null;
            float total = 0f;
            foreach (var s in species) if (s != null) total += Weight(s);
            if (total <= 0f) return null;
            float x = Random.value * total;
            foreach (var s in species)
            {
                if (s == null) continue;
                x -= Weight(s);
                if (x <= 0f) return s;
            }
            return species[species.Length - 1];
        }

        bool TrySpawnGroup(bool initial)
        {
            var def = PickSpecies();
            if (def == null || player == null) return false;

            if (!FindSpawnPoint(def, initial, out Vector3 center)) return false;

            int count = def.schooling ? Random.Range(def.groupSize.x, def.groupSize.y + 1) : 1;
            count = Mathf.Min(count, MaxCreatures - owned.Count);
            Quaternion heading = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
            for (int i = 0; i < count; i++)
            {
                float len = def.RandomLength();
                Vector3 pos = center;
                if (i > 0)
                {
                    Vector3 off = Random.insideUnitSphere * len * 3f;
                    if (def.habitat == CreatureHabitat.Bottom) off.y = 0f;
                    pos += off;
                    if (def.habitat == CreatureHabitat.Bottom) pos.y = GroundY(pos);
                }
                SpawnOne(def, len, pos, heading);
            }
            return true;
        }

        bool FindSpawnPoint(CreatureDefinition def, bool initial, out Vector3 point)
        {
            Vector3 p = player.transform.position;
            Camera cam = Camera.main;
            float surface = UnderwaterEnvironment.Instance != null ? UnderwaterEnvironment.Instance.surfaceY : 10f;
            float rs = RangeScale;

            for (int attempt = 0; attempt < 8; attempt++)
            {
                float minD = (initial ? spawnMinDistance * 0.5f : spawnMinDistance) * rs;
                float maxD = Mathf.Max(minD * 1.5f, Mathf.Min(spawnMaxDistance * rs, 1f + def.AverageLength * 50f));
                float dist = Mathf.Lerp(minD, maxD, Mathf.Pow(Random.value, 1.5f));
                Vector2 dir = Random.insideUnitCircle.normalized;
                Vector3 c = ClampToArea(p + new Vector3(dir.x, 0f, dir.y) * dist);
                float gy = GroundY(c);
                if (gy < -1000f) continue;

                switch (def.habitat)
                {
                    case CreatureHabitat.Bottom: c.y = gy; break;
                    case CreatureHabitat.NearBottom: c.y = gy + Random.Range(def.hoverHeight.x, def.hoverHeight.y); break;
                    default: c.y = Mathf.Lerp(gy + 0.4f, Mathf.Min(surface - 0.5f, gy + 2f + 4f * rs), Random.value * Random.value); break;
                }
                c.y = Mathf.Min(c.y, surface - Mathf.Max(0.3f, def.minDepth));
                if (c.y < gy) continue;

                // Не внутри скалы
                if (def.habitat != CreatureHabitat.Bottom && Physics.CheckSphere(c, def.maxLength * 0.6f, ~0, QueryTriggerInteraction.Ignore)) continue;

                // Не появляться на глазах у игрока вблизи
                if (!initial && cam != null && dist < maxD * 0.5f)
                {
                    Vector3 vp = cam.WorldToViewportPoint(c);
                    if (vp.z > 0f && vp.x > -0.1f && vp.x < 1.1f && vp.y > -0.1f && vp.y < 1.1f) continue;
                }
                point = c;
                return true;
            }
            point = default;
            return false;
        }

        static float GroundY(Vector3 p)
        {
            float top = UnderwaterEnvironment.Instance != null ? UnderwaterEnvironment.Instance.surfaceY : 10f;
            if (Physics.Raycast(new Vector3(p.x, top, p.z), Vector3.down, out RaycastHit hit, 300f, ~0, QueryTriggerInteraction.Ignore)
                && hit.collider.attachedRigidbody == null)
                return hit.point.y;
            return -10000f;
        }

        Creature SpawnOne(CreatureDefinition def, float length, Vector3 pos, Quaternion rot)
        {
            Creature c = null;
            if (pools.TryGetValue(def, out var stack))
                while (stack.Count > 0 && c == null) c = stack.Pop();
            if (c == null)
            {
                var go = new GameObject(def.name);
                go.transform.SetParent(poolRoot, false);
                c = go.AddComponent<Creature>();
            }
            c.gameObject.SetActive(true);
            c.Init(def, length, creatureMaterial, this, pos, rot * Quaternion.Euler(0f, Random.Range(-30f, 30f), 0f));
            owned.Add(c);
            return c;
        }
    }
}
