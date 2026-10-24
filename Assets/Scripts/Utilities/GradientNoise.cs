using System;
using UnityEngine;

namespace KingOfNothing.Utilities
{
    /// <summary>
    /// Seeded 2D Perlin (gradient) noise. Output is roughly in [0, 1].
    /// Mathf.PerlinNoise cannot be seeded, so the map generator uses this class instead.
    /// Pure C#: safe to use from background threads.
    /// </summary>
    public sealed class GradientNoise
    {
        readonly int[] permutation = new int[512];
        readonly float[] gradientX = new float[256];
        readonly float[] gradientY = new float[256];

        public GradientNoise(int seed)
        {
            var random = new DeterministicRandom(seed);
            var p = new int[256];
            for (int i = 0; i < 256; i++) p[i] = i;
            random.Shuffle(p, 256);
            for (int i = 0; i < 512; i++) permutation[i] = p[i & 255];

            for (int i = 0; i < 256; i++)
            {
                double angle = random.NextDouble() * Math.PI * 2.0;
                gradientX[i] = (float)Math.Cos(angle);
                gradientY[i] = (float)Math.Sin(angle);
            }
        }

        static int FastFloor(float v) => v >= 0f ? (int)v : (int)v - 1;

        static float Fade(float t) => t * t * t * (t * (t * 6f - 15f) + 10f);

        float Gradient(int ix, int iy, float dx, float dy)
        {
            int h = permutation[permutation[ix & 255] + (iy & 255)];
            return gradientX[h] * dx + gradientY[h] * dy;
        }

        /// <summary>Single octave noise, roughly in [0, 1].</summary>
        public float Sample(float x, float y)
        {
            int xi = FastFloor(x), yi = FastFloor(y);
            float xf = x - xi, yf = y - yi;

            float n00 = Gradient(xi, yi, xf, yf);
            float n10 = Gradient(xi + 1, yi, xf - 1f, yf);
            float n01 = Gradient(xi, yi + 1, xf, yf - 1f);
            float n11 = Gradient(xi + 1, yi + 1, xf - 1f, yf - 1f);

            float u = Fade(xf), v = Fade(yf);
            float a = n00 + u * (n10 - n00);
            float b = n01 + u * (n11 - n01);
            return (a + v * (b - a)) * 0.7071f + 0.5f;
        }

        /// <summary>Fractal (fBm) noise: several octaves of increasing frequency. Roughly in [0, 1].</summary>
        public float Fractal(float x, float y, int octaves, float lacunarity = 2f, float persistence = 0.5f)
        {
            float sum = 0f, norm = 0f, amplitude = 1f, frequency = 1f;
            for (int o = 0; o < octaves; o++)
            {
                // A different offset per octave avoids the lattice points of all octaves lining up
                sum += amplitude * Sample(x * frequency + o * 17.31f, y * frequency + o * 9.17f);
                norm += amplitude;
                amplitude *= persistence;
                frequency *= lacunarity;
            }
            return norm > 0f ? sum / norm : 0.5f;
        }

        /// <summary>Fractal noise using the given settings; x and y are in cells.</summary>
        public float Fractal(float x, float y, NoiseSettings settings)
        {
            float scale = Math.Max(0.0001f, settings.scale);
            return Fractal(x / scale, y / scale, Math.Max(1, settings.octaves), settings.lacunarity, settings.persistence);
        }
    }

    /// <summary>Inspector settings of a fractal noise layer.</summary>
    [Serializable]
    public class NoiseSettings
    {
        [Tooltip("Size of the features in cells. Bigger = larger, smoother shapes.")]
        [Min(0.01f)] public float scale = 100f;

        [Tooltip("Number of detail layers. More octaves = more ragged edges.")]
        [Range(1, 8)] public int octaves = 4;

        [Tooltip("Frequency multiplier between octaves.")]
        [Min(1f)] public float lacunarity = 2f;

        [Tooltip("Amplitude multiplier between octaves.")]
        [Range(0f, 1f)] public float persistence = 0.5f;

        public NoiseSettings() { }

        public NoiseSettings(float scale, int octaves, float lacunarity = 2f, float persistence = 0.5f)
        {
            this.scale = scale;
            this.octaves = octaves;
            this.lacunarity = lacunarity;
            this.persistence = persistence;
        }
    }
}
