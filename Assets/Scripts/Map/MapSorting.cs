using UnityEngine;

/// <summary>
/// Haritadaki objelerin (ağaç, birim, bina...) çizim sırası. Aşağıdaki obje öndedir.
/// Harita üreticisi ve hareket eden birimler aynı formülü kullanmalı, yoksa ağaçların önüne/arkasına
/// geçerken birimler yanlış çizilir.
/// </summary>
public static class MapSorting
{
    public const int MapHeight = 261 * 3; // RTSMapGenerator.MapScale ile aynı olmalı

    // sortingOrder en fazla 32767 olabildiği için çarpan haritanın yüksekliğine göre seçiliyor
    static readonly float Scale = Mathf.Min(100f, 32000f / (MapHeight + 1));

    /// <param name="x">Objenin zemine değdiği noktanın x'i.</param>
    /// <param name="y">Objenin zemine değdiği noktanın y'si.</param>
    public static int Order(float x, float y) =>
        // Aynı sıra numarası iki objeye denk gelirse Unity'nin çizim sırası kare kare değişip titriyor;
        // x'ten küçük bir ayırıcı eklenmesinin sebebi bu.
        Mathf.RoundToInt((MapHeight - y) * Scale) + Mathf.Abs(Mathf.FloorToInt(x * 3f)) % 10;
}
