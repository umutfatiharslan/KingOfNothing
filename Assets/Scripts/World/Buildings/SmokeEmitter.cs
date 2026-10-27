using System.Collections.Generic;
using KingOfNothing.World.Objects;
using UnityEngine;

namespace KingOfNothing.World.Buildings
{
    /// <summary>
    /// Smoke of a working furnace (Dust_01 frames): puffs rise up to the smoke height (cells), drifting a little
    /// backwards (up the screen, the game is 2D) and sideways, grow and fade. Only an effect: the puffs are marked
    /// so selection, picking and fading ignore them, and characters cannot interact with them.
    /// </summary>
    public class SmokeEmitter : MonoBehaviour
    {
        sealed class Puff
        {
            public SpriteRenderer Renderer;
            public float Age;
            public float Drift;
        }

        BuildingDefinition info;
        readonly List<Puff> puffs = new List<Puff>();
        readonly Stack<SpriteRenderer> pool = new Stack<SpriteRenderer>();
        float timer;

        public bool Emitting { get; set; }

        public void Setup(BuildingDefinition definition) => info = definition;

        void Update()
        {
            if (info == null || info.smokeFrames == null || info.smokeFrames.Length == 0) return;
            float dt = Time.deltaTime;
            if (Emitting)
            {
                timer -= dt;
                if (timer <= 0f)
                {
                    timer = info.smokeInterval;
                    Spawn();
                }
            }
            for (int i = puffs.Count - 1; i >= 0; i--)
            {
                var p = puffs[i];
                p.Age += dt;
                float t = p.Age / info.smokeLifetime;
                if (t >= 1f)
                {
                    p.Renderer.enabled = false;
                    pool.Push(p.Renderer);
                    puffs.RemoveAt(i);
                    continue;
                }
                var frames = info.smokeFrames;
                // Loop the first frames while rising, end with the fading frames
                int frame = Mathf.Clamp((int)(t * frames.Length), 0, frames.Length - 1);
                p.Renderer.sprite = frames[frame];
                float height = info.smokeHeight * Mathf.Sqrt(t);
                var start = (Vector3)(Vector2)info.smokeOffset;
                p.Renderer.transform.localPosition = start + new Vector3(p.Drift * t + Mathf.Sin(p.Age * 2.3f) * 0.08f, height, 0f);
                float scale = info.smokeScale * Mathf.Lerp(0.6f, 1.4f, t);
                p.Renderer.transform.localScale = new Vector3(scale, scale, 1f);
                p.Renderer.color = new Color(1f, 1f, 1f, Mathf.Clamp01(1.2f - t) * 0.85f);
                // Above the furnace itself (it is "behind" it in height)
                p.Renderer.sortingOrder = MapSorting.Order(transform.position.x, transform.position.y) + 1;
            }
        }

        void Spawn()
        {
            var r = pool.Count > 0 ? pool.Pop() : Create();
            r.enabled = true;
            puffs.Add(new Puff { Renderer = r, Age = 0f, Drift = Random.Range(-0.35f, 0.15f) });
        }

        SpriteRenderer Create()
        {
            var go = new GameObject("Smoke", typeof(SpriteEffectMarker));
            go.transform.SetParent(transform, false);
            return go.AddComponent<SpriteRenderer>();
        }

        void OnDisable()
        {
            foreach (var p in puffs) if (p.Renderer != null) p.Renderer.enabled = false;
        }
    }
}
