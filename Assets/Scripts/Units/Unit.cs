using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Seçilebilen ve hareket ettirilebilen birim. Objenin pozisyonu birimin ayaklarının değdiği noktadır;
/// görsel (body) bunun biraz yukarısında duran bir child'dır.
/// </summary>
[RequireComponent(typeof(Collider2D))]
public class Unit : MonoBehaviour
{
    public static readonly List<Unit> All = new List<Unit>();

    [Header("Hareket")]
    public float moveSpeed = 3f;

    [Header("Görsel")]
    public SpriteRenderer body;
    public Sprite[] idleFrames;
    public Sprite[] runFrames;
    public float fps = 10f;

    [Header("Seçim halkası")]
    public Color selectionColor = new Color(0.45f, 1f, 0.45f, 0.95f);
    public Vector2 selectionSize = new Vector2(0.9f, 0.45f);

    public bool IsSelected { get; private set; }
    public bool IsMoving { get; private set; }

    SpriteRenderer ring;
    Vector2 target;
    Sprite[] currentFrames;
    float animTime;

    void OnEnable() => All.Add(this);
    void OnDisable() => All.Remove(this);

    void Awake()
    {
        if (body == null) body = GetComponentInChildren<SpriteRenderer>();
        target = transform.position;
        CreateSelectionRing();
        SetSelected(false);
        // Birimler aynı karede başlamasın
        animTime = Random.Range(0f, 1f);
    }

    public void SetSelected(bool selected)
    {
        IsSelected = selected;
        ring.enabled = selected;
    }

    public void MoveTo(Vector2 destination)
    {
        target = destination;
        IsMoving = true;
    }

    public void Stop()
    {
        target = transform.position;
        IsMoving = false;
    }

    void Update()
    {
        if (IsMoving)
        {
            Vector2 pos = transform.position;
            Vector2 toTarget = target - pos;
            float step = moveSpeed * Time.deltaTime;

            if (toTarget.magnitude <= step)
            {
                transform.position = target;
                IsMoving = false;
            }
            else
            {
                transform.position = pos + toTarget.normalized * step;
                if (Mathf.Abs(toTarget.x) > 0.01f)
                    body.flipX = toTarget.x < 0;
            }
        }

        Animate();
        UpdateSorting();
    }

    void Animate()
    {
        var frames = IsMoving && runFrames != null && runFrames.Length > 0 ? runFrames : idleFrames;
        if (frames == null || frames.Length == 0) return;

        if (frames != currentFrames)
        {
            currentFrames = frames;
            animTime = 0f;
        }

        animTime += Time.deltaTime;
        body.sprite = frames[(int)(animTime * fps) % frames.Length];
    }

    void UpdateSorting()
    {
        int order = MapSorting.Order(transform.position.x, transform.position.y);
        body.sortingOrder = order;
        ring.sortingOrder = order - 1;
    }

    // ---------------------------------------------------------------- Seçim halkası

    static Sprite ringSprite;

    void CreateSelectionRing()
    {
        if (ringSprite == null)
            ringSprite = BuildEllipseRingSprite(64, 32, 3);

        var go = new GameObject("Selection Ring");
        go.transform.SetParent(transform, false);
        go.transform.localScale = new Vector3(selectionSize.x, selectionSize.y * 2f, 1f);
        ring = go.AddComponent<SpriteRenderer>();
        ring.sprite = ringSprite;
        ring.color = selectionColor;
    }

    /// <summary>Beyaz elips halka; 64 piksel = 1 birim genişlik.</summary>
    static Sprite BuildEllipseRingSprite(int width, int height, float thickness)
    {
        var tex = new Texture2D(width, height, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp
        };

        float rx = width / 2f - 1f, ry = height / 2f - 1f;
        var pixels = new Color[width * height];
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            float dx = (x + 0.5f - width / 2f) / rx;
            float dy = (y + 0.5f - height / 2f) / ry;
            float d = Mathf.Sqrt(dx * dx + dy * dy);
            // Kenara olan yaklaşık piksel uzaklığı
            float distPx = Mathf.Abs(d - 1f) * Mathf.Min(rx, ry) * 1.6f;
            float alpha = Mathf.Clamp01(thickness / 2f + 0.5f - distPx);
            pixels[y * width + x] = new Color(1f, 1f, 1f, alpha);
        }
        tex.SetPixels(pixels);
        tex.Apply();

        return Sprite.Create(tex, new Rect(0, 0, width, height), new Vector2(0.5f, 0.5f), width);
    }
}
