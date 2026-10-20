using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Tilemaps;

/// <summary>
/// RTS birim seçimi:
///  - Sol tık: birimi seç (boşluğa tıklamak seçimi kaldırır)
///  - Sol tık + sürükle: kutunun içindeki tüm birimleri seç
///  - Shift: mevcut seçime ekle
///  - Sağ tık: seçili birimleri oraya yürüt
/// </summary>
public class SelectionManager : MonoBehaviour
{
    [Tooltip("Birimlerin yürüyebileceği zemin. Boş bırakılırsa her yere gidilebilir.")]
    public Tilemap walkableGround;

    public float dragThreshold = 6f;
    public float formationSpacing = 0.8f;
    public Color boxFill = new Color(0.35f, 0.85f, 1f, 0.15f);
    public Color boxBorder = new Color(0.35f, 0.85f, 1f, 0.9f);

    public IReadOnlyList<Unit> Selected => selected;

    readonly List<Unit> selected = new List<Unit>();
    Camera cam;
    Vector2 dragStart;
    bool pressing, dragging;

    void Awake() => cam = Camera.main;

    void Update()
    {
        var mouse = Mouse.current;
        if (mouse == null || cam == null) return;

        Vector2 mousePos = mouse.position.ReadValue();
        var kb = Keyboard.current;
        bool additive = kb != null && (kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed);

        if (mouse.leftButton.wasPressedThisFrame)
        {
            pressing = true;
            dragging = false;
            dragStart = mousePos;
        }

        if (pressing && !dragging && (mousePos - dragStart).magnitude > dragThreshold)
            dragging = true;

        if (pressing && mouse.leftButton.wasReleasedThisFrame)
        {
            if (dragging) BoxSelect(dragStart, mousePos, additive);
            else ClickSelect(mousePos, additive);
            pressing = dragging = false;
        }

        if (mouse.rightButton.wasPressedThisFrame && selected.Count > 0)
            IssueMove(mousePos);

        selected.RemoveAll(u => u == null);
    }

    // ---------------------------------------------------------------- Seçim

    void ClickSelect(Vector2 screenPos, bool additive)
    {
        Vector2 world = cam.ScreenToWorldPoint(screenPos);

        // Üst üste binen birimlerden öndekini (en aşağıdakini) seç
        Unit clicked = null;
        foreach (var hit in Physics2D.OverlapPointAll(world))
        {
            var unit = hit.GetComponentInParent<Unit>();
            if (unit != null && (clicked == null || unit.transform.position.y < clicked.transform.position.y))
                clicked = unit;
        }

        if (!additive) ClearSelection();
        if (clicked == null) return;

        if (additive && clicked.IsSelected) Deselect(clicked);
        else Select(clicked);
    }

    void BoxSelect(Vector2 a, Vector2 b, bool additive)
    {
        var rect = Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
        if (!additive) ClearSelection();

        foreach (var unit in Unit.All)
        {
            // Birimin gövdesinin ortası (ayakların biraz üstü) kutunun içindeyse seçilir
            Vector2 screen = cam.WorldToScreenPoint(unit.transform.position + Vector3.up * 0.5f);
            if (rect.Contains(screen)) Select(unit);
        }
    }

    void Select(Unit unit)
    {
        if (selected.Contains(unit)) return;
        selected.Add(unit);
        unit.SetSelected(true);
    }

    void Deselect(Unit unit)
    {
        selected.Remove(unit);
        unit.SetSelected(false);
    }

    public void ClearSelection()
    {
        foreach (var unit in selected)
            if (unit != null) unit.SetSelected(false);
        selected.Clear();
    }

    // ---------------------------------------------------------------- Komut

    void IssueMove(Vector2 screenPos)
    {
        Vector2 target = cam.ScreenToWorldPoint(screenPos);
        if (!IsWalkable(target)) return;

        // Birden fazla birim varsa hedefin etrafında kare dizilişte dursunlar
        int count = selected.Count;
        int cols = Mathf.CeilToInt(Mathf.Sqrt(count));
        int rows = Mathf.CeilToInt(count / (float)cols);
        for (int i = 0; i < count; i++)
        {
            var offset = new Vector2(i % cols - (cols - 1) / 2f, -(i / cols) + (rows - 1) / 2f) * formationSpacing;
            var spot = target + offset;
            selected[i].MoveTo(IsWalkable(spot) ? spot : target);
        }
    }

    bool IsWalkable(Vector2 world) =>
        walkableGround == null || walkableGround.HasTile(walkableGround.WorldToCell(world));

    // ---------------------------------------------------------------- Seçim kutusu çizimi

    void OnGUI()
    {
        if (!dragging || Mouse.current == null) return;

        Vector2 a = dragStart, b = Mouse.current.position.ReadValue();
        // GUI koordinatında y yukarıdan aşağı
        var rect = Rect.MinMaxRect(Mathf.Min(a.x, b.x), Screen.height - Mathf.Max(a.y, b.y),
                                   Mathf.Max(a.x, b.x), Screen.height - Mathf.Min(a.y, b.y));

        var tex = Texture2D.whiteTexture;
        var old = GUI.color;
        GUI.color = boxFill;
        GUI.DrawTexture(rect, tex);
        GUI.color = boxBorder;
        const float t = 2f;
        GUI.DrawTexture(new Rect(rect.xMin, rect.yMin, rect.width, t), tex);
        GUI.DrawTexture(new Rect(rect.xMin, rect.yMax - t, rect.width, t), tex);
        GUI.DrawTexture(new Rect(rect.xMin, rect.yMin, t, rect.height), tex);
        GUI.DrawTexture(new Rect(rect.xMax - t, rect.yMin, t, rect.height), tex);
        GUI.color = old;
    }
}
