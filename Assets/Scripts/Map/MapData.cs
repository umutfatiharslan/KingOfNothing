using UnityEngine;

/// <summary>
/// Haritanın oyun sırasında gereken bilgisi: hangi hücre hangi biyom, biyomda hareket hızı.
/// Harita üreticisi tarafından doldurulur.
/// </summary>
public class MapData : MonoBehaviour
{
    public const byte Water = 255;

    public static MapData Instance { get; private set; }

    public int width;
    public int height;
    public string[] biomeNames;
    [Tooltip("Biyom başına hareket hızı çarpanı (biomeNames ile aynı sıra).")]
    public float[] biomeSpeed;

    [HideInInspector] public byte[] cells; // x + y * width; Water = su

    void Awake() => Instance = this;

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    public byte BiomeAt(Vector2 world)
    {
        int x = Mathf.FloorToInt(world.x), y = Mathf.FloorToInt(world.y);
        if (cells == null || x < 0 || y < 0 || x >= width || y >= height) return Water;
        return cells[x + y * width];
    }

    public float SpeedMultiplierAt(Vector2 world)
    {
        byte b = BiomeAt(world);
        return b != Water && biomeSpeed != null && b < biomeSpeed.Length ? biomeSpeed[b] : 1f;
    }
}
