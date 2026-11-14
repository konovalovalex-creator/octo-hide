#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace OctoEvo.EditorTools
{
    /// <summary>
    /// Стартовый набор видов для рифа. Ассеты создаются один раз в Assets/OctoEvo/Data/Creatures;
    /// если ассет уже есть, его настройки не трогаем — можно править руками в инспекторе.
    /// </summary>
    public static class CreatureSpecies
    {
        public const string Dir = "Assets/OctoEvo/Data/Creatures";

        public static CreatureDefinition[] EnsureAll()
        {
            ProceduralAssets.EnsureFolder("Assets/OctoEvo/Data");
            ProceduralAssets.EnsureFolder(Dir);
            var list = new List<CreatureDefinition>
            {
                Get("Shrimp", d =>
                {
                    d.displayName = "Креветка"; d.body = CreatureBody.Shrimp; d.habitat = CreatureHabitat.NearBottom;
                    d.minLength = 0.03f; d.maxLength = 0.06f; d.halfHeight = 0.09f; d.halfWidth = 0.07f;
                    d.backColor = new Color(0.78f, 0.48f, 0.4f); d.bellyColor = new Color(0.9f, 0.7f, 0.6f);
                    d.patternColor = new Color(0.55f, 0.2f, 0.15f); d.pattern = 4; d.patternScale = 8f;
                    d.smoothness = 0.7f; d.translucency = 0.55f;
                    d.cruiseSpeed = 1.2f; d.fleeSpeed = 14f; d.maxSpeed = 1f; d.turnRate = 4f;
                    d.schooling = true; d.groupSize = new Vector2Int(3, 6);
                    d.noticeDistance = 0.5f; d.wariness = 0.6f; d.spawnWeight = 1.2f;
                }),
                Get("Goby", d =>
                {
                    d.displayName = "Бычок"; d.body = CreatureBody.Fish; d.habitat = CreatureHabitat.NearBottom;
                    d.minLength = 0.05f; d.maxLength = 0.09f; d.halfHeight = 0.1f; d.halfWidth = 0.07f;
                    d.backColor = new Color(0.62f, 0.56f, 0.42f); d.bellyColor = new Color(0.86f, 0.83f, 0.72f);
                    d.patternColor = new Color(0.3f, 0.25f, 0.18f); d.pattern = 2; d.patternScale = 7f;
                    d.cruiseSpeed = 1f; d.fleeSpeed = 10f; d.maxSpeed = 1.2f; d.turnRate = 4f;
                    d.noticeDistance = 0.8f; d.wariness = 0.5f; d.spawnWeight = 1f;
                }),
                Get("Chromis", d =>
                {
                    d.displayName = "Хромис"; d.body = CreatureBody.Fish; d.habitat = CreatureHabitat.MidWater;
                    d.minLength = 0.06f; d.maxLength = 0.1f; d.halfHeight = 0.17f; d.halfWidth = 0.06f;
                    d.backColor = new Color(0.22f, 0.45f, 0.68f); d.bellyColor = new Color(0.62f, 0.78f, 0.88f);
                    d.pattern = 0; d.smoothness = 0.65f;
                    d.cruiseSpeed = 2f; d.fleeSpeed = 12f; d.maxSpeed = 1.6f; d.turnRate = 4f;
                    d.schooling = true; d.groupSize = new Vector2Int(6, 12);
                    d.noticeDistance = 1.2f; d.wariness = 0.7f; d.spawnWeight = 1.2f;
                }),
                Get("Crab", d =>
                {
                    d.displayName = "Краб"; d.body = CreatureBody.Crab; d.habitat = CreatureHabitat.Bottom;
                    d.minLength = 0.08f; d.maxLength = 0.2f; d.halfHeight = 0.08f; d.halfWidth = 0.25f;
                    d.backColor = new Color(0.62f, 0.3f, 0.18f); d.bellyColor = new Color(0.86f, 0.62f, 0.46f);
                    d.patternColor = new Color(0.35f, 0.15f, 0.1f); d.pattern = 4; d.patternScale = 5f; d.smoothness = 0.45f;
                    d.cruiseSpeed = 0.8f; d.fleeSpeed = 4f; d.maxSpeed = 0.8f; d.turnRate = 3f;
                    d.noticeDistance = 0.9f; d.wariness = 0.6f; d.spawnWeight = 0.9f;
                }),
                Get("SergeantMajor", d =>
                {
                    d.displayName = "Рыба-сержант"; d.body = CreatureBody.Fish; d.habitat = CreatureHabitat.MidWater;
                    d.minLength = 0.12f; d.maxLength = 0.2f; d.halfHeight = 0.2f; d.halfWidth = 0.06f;
                    d.backColor = new Color(0.66f, 0.66f, 0.42f); d.bellyColor = new Color(0.9f, 0.9f, 0.85f);
                    d.patternColor = new Color(0.08f, 0.08f, 0.1f); d.pattern = 1; d.patternScale = 5f;
                    d.cruiseSpeed = 1.6f; d.fleeSpeed = 8f; d.maxSpeed = 2.5f; d.turnRate = 3f;
                    d.schooling = true; d.groupSize = new Vector2Int(4, 8);
                    d.noticeDistance = 2f; d.wariness = 0.6f; d.spawnWeight = 0.9f;
                }),
                Get("Wrasse", d =>
                {
                    d.displayName = "Губан"; d.body = CreatureBody.Fish; d.habitat = CreatureHabitat.NearBottom;
                    d.minLength = 0.25f; d.maxLength = 0.4f; d.halfHeight = 0.14f; d.halfWidth = 0.06f;
                    d.backColor = new Color(0.35f, 0.55f, 0.4f); d.bellyColor = new Color(0.8f, 0.75f, 0.55f);
                    d.patternColor = new Color(0.55f, 0.25f, 0.4f); d.pattern = 3; d.patternScale = 4f;
                    d.cruiseSpeed = 1.2f; d.fleeSpeed = 6f; d.maxSpeed = 3f; d.turnRate = 2.5f;
                    d.noticeDistance = 3f; d.wariness = 0.5f; d.spawnWeight = 0.7f;
                }),
                Get("Grouper", d =>
                {
                    d.displayName = "Групер"; d.body = CreatureBody.Fish; d.habitat = CreatureHabitat.NearBottom;
                    d.minLength = 0.6f; d.maxLength = 1f; d.halfHeight = 0.15f; d.halfWidth = 0.09f;
                    d.backColor = new Color(0.4f, 0.33f, 0.26f); d.bellyColor = new Color(0.65f, 0.58f, 0.48f);
                    d.patternColor = new Color(0.2f, 0.15f, 0.1f); d.pattern = 2; d.patternScale = 9f; d.smoothness = 0.4f;
                    d.cruiseSpeed = 0.6f; d.fleeSpeed = 4f; d.maxSpeed = 3f; d.turnRate = 1.5f;
                    d.noticeDistance = 5f; d.wariness = 0.3f; d.spawnWeight = 0.4f; d.minDepth = 1.5f;
                }),
            };
            AssetDatabase.SaveAssets();
            list.Sort((a, b) => a.minLength.CompareTo(b.minLength));
            return list.ToArray();
        }

        static CreatureDefinition Get(string file, Action<CreatureDefinition> setup)
        {
            string path = $"{Dir}/{file}.asset";
            var existing = AssetDatabase.LoadAssetAtPath<CreatureDefinition>(path);
            if (existing != null) return existing;
            var d = ScriptableObject.CreateInstance<CreatureDefinition>();
            setup(d);
            AssetDatabase.CreateAsset(d, path);
            return d;
        }
    }
}
#endif
