using System.Collections.Generic;
using KingOfNothing.Utilities;
using UnityEngine;

namespace KingOfNothing.World.Objects
{
    /// <summary>
    /// Healing sparkles: small plus signs rising and fading inside an area (a stump growing back, a priest's
    /// healing). Runs until the object is destroyed, or for a fixed time.
    /// </summary>
    public class HealSparkles : MonoBehaviour
    {
        sealed class Spark
        {
            public SpriteRenderer Renderer;
            public float Age;
            public float Life;
            public Vector2 Start;
        }

        readonly List<Spark> sparks = new List<Spark>();
        Rect area;
        Color color = new Color(0.45f, 1f, 0.5f);
        float nextSpawn;
        float stopAt = -1f;
        const float Interval = 0.18f;

        public void Setup(Rect worldArea, Color tint, float seconds = -1f)
        {
            area = worldArea;
            color = tint;
            stopAt = seconds > 0f ? Time.time + seconds : -1f;
        }

        /// <summary>A short burst of sparkles on an area (a priest's healing wave).</summary>
        public static void Burst(Rect worldArea, Color tint, float seconds = 1.2f)
        {
            var go = new GameObject("Heal Sparkles");
            go.AddComponent<HealSparkles>().Setup(worldArea, tint, seconds);
        }

        void Update()
        {
            bool spawning = stopAt < 0f || Time.time < stopAt;
            if (spawning && Time.time >= nextSpawn)
            {
                nextSpawn = Time.time + Interval;
                Spawn();
            }
            for (int i = sparks.Count - 1; i >= 0; i--)
            {
                var s = sparks[i];
                s.Age += Time.deltaTime;
                float t = s.Age / s.Life;
                if (t >= 1f)
                {
                    Destroy(s.Renderer.gameObject);
                    sparks.RemoveAt(i);
                    continue;
                }
                s.Renderer.transform.position = s.Start + new Vector2(0f, t * 0.6f);
                s.Renderer.color = new Color(color.r, color.g, color.b, Mathf.Sin(t * Mathf.PI));
            }
            if (!spawning && sparks.Count == 0 && stopAt > 0f) Destroy(gameObject);
        }

        void Spawn()
        {
            var go = new GameObject("Spark");
            go.transform.SetParent(transform, true);
            var r = go.AddComponent<SpriteRenderer>();
            r.sprite = ProceduralSprites.Plus;
            r.sortingOrder = short.MaxValue - 25;
            float scale = Random.Range(0.35f, 0.6f);
            go.transform.localScale = new Vector3(scale, scale, 1f);
            var start = new Vector2(Random.Range(area.xMin, area.xMax), Random.Range(area.yMin, area.yMax));
            go.transform.position = start;
            sparks.Add(new Spark { Renderer = r, Life = Random.Range(0.8f, 1.3f), Start = start });
        }

        void OnDestroy()
        {
            foreach (var s in sparks) if (s.Renderer != null) Destroy(s.Renderer.gameObject);
        }
    }
}
