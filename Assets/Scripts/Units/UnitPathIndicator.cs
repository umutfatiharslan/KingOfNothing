using System.Collections.Generic;
using UnityEngine;

namespace KingOfNothing.Units
{
    /// <summary>
    /// RimWorld style movement feedback: a line from the unit along its remaining path and a pulsing marker
    /// where it is going. Added automatically to every Unit.
    /// </summary>
    [RequireComponent(typeof(Unit))]
    [DisallowMultipleComponent]
    public class UnitPathIndicator : MonoBehaviour
    {
        [Tooltip("Show the path only for selected units (otherwise for every moving unit).")]
        [SerializeField] bool onlyWhenSelected = true;
        [SerializeField] Color lineColor = new Color(1f, 1f, 1f, 0.55f);
        [Min(0.005f)] [SerializeField] float lineWidth = 0.07f;
        [SerializeField] Color markerColor = new Color(0.45f, 1f, 0.45f, 0.9f);
        [Min(0.1f)] [SerializeField] float markerSize = 0.9f;
        [Tooltip("Pulse speed of the destination marker.")]
        [Min(0f)] [SerializeField] float markerPulseSpeed = 3f;
        [Tooltip("Drawn above the map objects.")]
        [SerializeField] int sortingOrder = 32000;

        static Material lineMaterial;

        Unit unit;
        LineRenderer line;
        SpriteRenderer marker;
        readonly List<Vector3> points = new List<Vector3>();

        void Awake()
        {
            unit = GetComponent<Unit>();

            if (lineMaterial == null)
            {
                var shader = Shader.Find("Sprites/Default");
                if (shader != null) lineMaterial = new Material(shader) { name = "Unit Path Line" };
            }

            var lineObject = new GameObject("Path Line");
            lineObject.transform.SetParent(transform, false);
            line = lineObject.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.sharedMaterial = lineMaterial;
            line.startWidth = line.endWidth = lineWidth;
            line.startColor = new Color(lineColor.r, lineColor.g, lineColor.b, lineColor.a * 0.3f);
            line.endColor = lineColor;
            line.numCornerVertices = 2;
            line.numCapVertices = 2;
            line.sortingOrder = sortingOrder;
            line.enabled = false;

            var markerObject = new GameObject("Destination Marker");
            markerObject.transform.SetParent(transform, false);
            marker = markerObject.AddComponent<SpriteRenderer>();
            marker.sprite = Unit.RingSprite;
            marker.color = markerColor;
            marker.sortingOrder = sortingOrder;
            marker.enabled = false;
        }

        void LateUpdate()
        {
            bool visible = unit.IsMoving && unit.Path.Count > 0 && (!onlyWhenSelected || unit.IsSelected);
            line.enabled = visible;
            marker.enabled = visible;
            if (!visible) return;

            points.Clear();
            points.Add(transform.position);
            for (int i = unit.PathIndex; i < unit.Path.Count; i++) points.Add(unit.Path[i]);
            line.positionCount = points.Count;
            for (int i = 0; i < points.Count; i++) line.SetPosition(i, points[i]);

            Vector2 destination = unit.Destination;
            float pulse = 1f + 0.12f * Mathf.Sin(Time.unscaledTime * markerPulseSpeed);
            marker.transform.position = new Vector3(destination.x, destination.y, 0f);
            marker.transform.localScale = new Vector3(markerSize * pulse, markerSize * pulse, 1f);
        }
    }
}
