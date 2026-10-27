using UnityEngine;
using UnityEngine.InputSystem;

namespace KingOfNothing.CameraControl
{
    /// <summary>
    /// Free RTS camera: WASD / arrow keys to move, Shift to move faster, mouse wheel to zoom,
    /// optional screen edge panning and middle mouse drag. Stays inside the map bounds.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class RTSCameraController : MonoBehaviour
    {
        [Header("Movement")]
        [Min(0f)] [SerializeField] float moveSpeed = 12f;
        [Min(1f)] [SerializeField] float fastMultiplier = 2.5f;
        [Tooltip("Movement speed grows with the zoom level so far zoom still feels fast.")]
        [SerializeField] bool scaleSpeedWithZoom = true;
        [Tooltip("Zoom level at which Move Speed applies unchanged.")]
        [Min(0.1f)] [SerializeField] float referenceZoom = 8f;

        [Header("Screen edge panning")]
        [SerializeField] bool edgePan;
        [Min(1f)] [SerializeField] float edgeSize = 12f;

        [Header("Mouse drag")]
        [Tooltip("Drag the map with the middle mouse button.")]
        [SerializeField] bool middleMouseDrag = true;

        [Header("Zoom")]
        [Min(0.1f)] [SerializeField] float zoomStep = 1.5f;
        [Min(0.5f)] [SerializeField] float minZoom = 4f;
        [Min(1f)] [SerializeField] float maxZoom = 80f;
        [Min(0f)] [SerializeField] float zoomSmoothing = 10f;
        [Tooltip("Zoom towards the mouse cursor instead of the screen centre.")]
        [SerializeField] bool zoomToCursor = true;

        [Header("Map bounds")]
        [SerializeField] bool clampToBounds = true;
        [SerializeField] Rect bounds = new Rect(0, 0, 1800, 800);

        Camera cam;
        float targetZoom;
        Vector3 dragOrigin;
        bool dragging;

        public Camera AttachedCamera => cam != null ? cam : cam = GetComponent<Camera>();
        public float MaxZoom => maxZoom;

        void Awake()
        {
            cam = GetComponent<Camera>();
            cam.orthographic = true;
            targetZoom = Mathf.Clamp(cam.orthographicSize, minZoom, maxZoom);
        }

        public void SetBounds(Rect newBounds)
        {
            bounds = newBounds;
            transform.position = Clamp(transform.position);
        }

        public void FocusOn(Vector2 worldPoint, bool instant)
        {
            var pos = new Vector3(worldPoint.x, worldPoint.y, transform.position.z);
            transform.position = clampToBounds ? Clamp(pos) : pos;
        }

        void Update()
        {
            Vector2 dir = Vector2.zero;
            bool fast = false;

            var keyboard = Keyboard.current;
            if (keyboard != null)
            {
                if (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed) dir.y += 1;
                if (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed) dir.y -= 1;
                if (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed) dir.x += 1;
                if (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed) dir.x -= 1;
                fast = keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed;
            }

            var mouse = Mouse.current;
            if (mouse != null)
            {
                Vector2 mousePosition = mouse.position.ReadValue();
                if (edgePan && Application.isFocused)
                {
                    if (mousePosition.x <= edgeSize) dir.x -= 1;
                    else if (mousePosition.x >= Screen.width - edgeSize) dir.x += 1;
                    if (mousePosition.y <= edgeSize) dir.y -= 1;
                    else if (mousePosition.y >= Screen.height - edgeSize) dir.y += 1;
                }

                float scroll = mouse.scroll.ReadValue().y;
                // No zoom while the mouse is over a panel (developer tools, action menu...)
                if (Mathf.Abs(scroll) > 0.01f && !KingOfNothing.Utilities.ScreenUi.IsPointerOver(mouse.position.ReadValue()))
                    targetZoom = Mathf.Clamp(targetZoom - Mathf.Sign(scroll) * zoomStep * Mathf.Max(1f, targetZoom / referenceZoom), minZoom, maxZoom);

                HandleDrag(mouse, mousePosition);
            }

            ApplyZoom(mouse);

            float speed = moveSpeed * (scaleSpeedWithZoom ? cam.orthographicSize / referenceZoom : 1f) * (fast ? fastMultiplier : 1f);
            Vector3 pos = transform.position + (Vector3)(dir.normalized * speed * Time.unscaledDeltaTime);
            transform.position = clampToBounds ? Clamp(pos) : pos;
        }

        void HandleDrag(Mouse mouse, Vector2 mousePosition)
        {
            if (!middleMouseDrag) return;
            if (mouse.middleButton.wasPressedThisFrame)
            {
                dragging = true;
                dragOrigin = cam.ScreenToWorldPoint(mousePosition);
            }
            else if (mouse.middleButton.wasReleasedThisFrame) dragging = false;

            if (!dragging) return;
            Vector3 current = cam.ScreenToWorldPoint(mousePosition);
            Vector3 delta = dragOrigin - current;
            delta.z = 0f;
            transform.position += delta;
        }

        void ApplyZoom(Mouse mouse)
        {
            float before = cam.orthographicSize;
            float after = Mathf.Lerp(before, targetZoom, 1f - Mathf.Exp(-zoomSmoothing * Time.unscaledDeltaTime));
            if (Mathf.Approximately(before, after)) return;

            if (zoomToCursor && mouse != null && !dragging)
            {
                Vector2 screen = mouse.position.ReadValue();
                Vector3 worldBefore = cam.ScreenToWorldPoint(screen);
                cam.orthographicSize = after;
                Vector3 worldAfter = cam.ScreenToWorldPoint(screen);
                Vector3 shift = worldBefore - worldAfter;
                shift.z = 0f;
                transform.position += shift;
            }
            else cam.orthographicSize = after;
        }

        Vector3 Clamp(Vector3 pos)
        {
            var c = AttachedCamera;
            float halfH = c.orthographicSize;
            float halfW = halfH * c.aspect;

            pos.x = bounds.width > halfW * 2 ? Mathf.Clamp(pos.x, bounds.xMin + halfW, bounds.xMax - halfW) : bounds.center.x;
            pos.y = bounds.height > halfH * 2 ? Mathf.Clamp(pos.y, bounds.yMin + halfH, bounds.yMax - halfH) : bounds.center.y;
            return pos;
        }
    }
}
