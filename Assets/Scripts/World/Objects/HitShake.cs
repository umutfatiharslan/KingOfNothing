using UnityEngine;

namespace KingOfNothing.World.Objects
{
    /// <summary>
    /// Short wobble of an object that was hit. Added for the duration of the shake and removed afterwards, so the
    /// tens of thousands of idle objects cost nothing. Strength and length come from the InteractionSettings asset.
    /// </summary>
    public class HitShake : MonoBehaviour
    {
        Transform target;
        Vector3 restPosition;
        float time;
        float duration, amplitude, frequency;

        /// <summary>Shakes the object's sprite (restarts if it is already shaking).</summary>
        public static void Play(WorldObject worldObject)
        {
            if (worldObject == null || !Application.isPlaying) return;
            var settings = MapData.Instance != null ? MapData.Instance.Interaction : InteractionSettings.Fallback;
            if (settings.shakeAmplitude <= 0f) return;
            var shake = worldObject.GetComponent<HitShake>();
            if (shake == null)
            {
                shake = worldObject.gameObject.AddComponent<HitShake>();
                shake.target = worldObject.transform;
                shake.restPosition = shake.target.localPosition;
            }
            shake.time = 0f;
            shake.duration = settings.shakeDuration;
            shake.amplitude = settings.shakeAmplitude;
            shake.frequency = settings.shakeFrequency;
        }

        /// <summary>Ends a running shake at once (before the object is moved).</summary>
        public static void Stop(WorldObject worldObject)
        {
            if (worldObject == null) return;
            var shake = worldObject.GetComponent<HitShake>();
            if (shake == null) return;
            if (shake.target != null) shake.target.localPosition = shake.restPosition;
            shake.target = null;
            if (Application.isPlaying) Destroy(shake);
            else DestroyImmediate(shake);
        }

        void Update()
        {
            time += Time.deltaTime;
            if (time >= duration || target == null)
            {
                if (target != null) target.localPosition = restPosition;
                Destroy(this);
                return;
            }
            float fade = 1f - time / duration;
            float x = Mathf.Sin(time * frequency * Mathf.PI * 2f) * amplitude * fade;
            target.localPosition = restPosition + new Vector3(x, 0f, 0f);
        }

        void OnDisable()
        {
            if (target != null) target.localPosition = restPosition;
            target = null;
        }
    }
}
