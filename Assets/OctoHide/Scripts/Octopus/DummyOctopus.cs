using UnityEngine;

namespace OctoHide
{
    /// <summary>
    /// Осьминог-манекен для прототипа: при старте ложится, копирует окрас поверхности,
    /// принимает «прячущую» позу и иногда закрывает глаза. Пока нет сети и ботов — цель для поиска и бросков.
    /// </summary>
    [RequireComponent(typeof(OctopusController))]
    public class DummyOctopus : MonoBehaviour
    {
        public OctopusPose[] hidingPoses = { OctopusPose.Rock, OctopusPose.Flat, OctopusPose.Starfish, OctopusPose.Coral, OctopusPose.Normal };
        [Range(0, 1)] public float closeEyesChance = 0.5f;

        OctopusController ctrl;
        OctopusSkin skin;
        float nextTry;
        bool done;

        void Start()
        {
            ctrl = GetComponent<OctopusController>();
            ctrl.isLocalPlayer = false;
            skin = GetComponent<OctopusSkin>();
            nextTry = Time.time + Random.Range(0.8f, 1.6f);

            var eyes = GetComponent<OctopusEyes>();
            if (eyes != null && Random.value < closeEyesChance) eyes.SetClosed(true);
        }

        void Update()
        {
            if (done || skin == null || Time.time < nextTry) return;
            nextTry = Time.time + 0.5f;
            if (!ctrl.IsGrounded) return;
            if (skin.TrySampleSurface())
            {
                ctrl.SetPose(hidingPoses[Random.Range(0, hidingPoses.Length)]);
                done = true;
            }
        }
    }
}
