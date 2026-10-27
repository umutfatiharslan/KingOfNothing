using UnityEngine;

namespace KingOfNothing.World
{
    /// <summary>
    /// Loops sprite sheet frames (trees, bushes, gold stones, water rocks...).
    /// Every object starts on a random frame with a slightly different speed so a forest does not sway in sync.
    /// Only runs while visible: the map has tens of thousands of objects and updating all of them every frame
    /// costs a lot of FPS.
    /// </summary>
    [RequireComponent(typeof(SpriteRenderer))]
    public class SpriteSheetAnimator : MonoBehaviour
    {
        [SerializeField] Sprite[] frames;
        [Min(0f)] [SerializeField] float fps = 10f;
        [SerializeField] bool randomStart = true;
        [Range(0f, 0.5f)] [SerializeField] float speedVariation = 0.15f;

        SpriteRenderer spriteRenderer;
        float time;
        float speed;
        int current = -1;

        public void Setup(Sprite[] newFrames, float newFps)
        {
            frames = newFrames;
            fps = newFps;
            Restart();
        }

        void Awake()
        {
            spriteRenderer = GetComponent<SpriteRenderer>();
            Restart();
            // OnBecameVisible switches it on when the camera sees it
            enabled = spriteRenderer.isVisible;
        }

        void Restart()
        {
            current = -1;
            speed = fps * Random.Range(1f - speedVariation, 1f + speedVariation);
            if (randomStart && frames != null && frames.Length > 0 && speed > 0f)
                time = Random.Range(0f, frames.Length / speed);
        }

        void OnBecameVisible() => enabled = true;
        void OnBecameInvisible() => enabled = false;

        void Update()
        {
            if (frames == null || frames.Length == 0 || speed <= 0f) return;
            if (spriteRenderer == null) spriteRenderer = GetComponent<SpriteRenderer>();

            time += Time.deltaTime;
            int index = (int)(time * speed) % frames.Length;
            if (index == current) return;
            current = index;
            spriteRenderer.sprite = frames[index];
        }
    }
}
