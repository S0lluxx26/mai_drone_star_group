using System;
using UnityEngine;

namespace DroneStar.App
{
    public enum CameraMode
    {
        Orbit,
        Audience,
        Aerial,
        Cinematic,
    }

    /// <summary>
    /// Studio camera. Orbit: drag to orbit, right/middle-drag to pan, wheel or pinch to zoom.
    /// Audience and Aerial are fixed vantage points; Cinematic follows poses fed by the demo director.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public sealed class ShowCameraRig : MonoBehaviour
    {
        public static readonly Vector3 DefaultFocus = new Vector3(0f, 58f, 0f);

        [SerializeField] float orbitSensitivity = 0.25f;
        [SerializeField] float panSensitivity = 0.0016f;
        [SerializeField] float zoomSensitivity = 0.12f;

        Camera cam;
        Vector3 focus = DefaultFocus;
        float yaw;
        float pitch = 9f;
        float distance = 190f;
        Vector3 lastPointer;
        bool dragging;
        float lastPinch;

        Vector3 currentPosition;
        Quaternion currentRotation;
        float currentFov = 50f;
        Vector3 cinematicPosition;
        Vector3 cinematicTarget = DefaultFocus;
        float cinematicFov = 45f;
        bool snapNext = true;

        public CameraMode Mode { get; private set; } = CameraMode.Orbit;

        /// <summary>Set by the app: returns true when a screen point (pixels, bottom-left origin) is over the UI.</summary>
        public Func<Vector2, bool> IsPointerOverUi { get; set; }

        /// <summary>Keyboard and pointer input is ignored while this returns true (e.g. typing in a field).</summary>
        public Func<bool> InputBlocked { get; set; }

        public event Action<CameraMode> ModeChanged;

        void Awake()
        {
            cam = GetComponent<Camera>();
            currentPosition = transform.position;
            currentRotation = transform.rotation;
        }

        public void SetMode(CameraMode mode)
        {
            if (mode == Mode) return;
            Mode = mode;
            snapNext = mode == CameraMode.Cinematic;
            ModeChanged?.Invoke(mode);
        }

        /// <summary>Frames a sphere in Orbit mode (e.g. the selected formation).</summary>
        public void Frame(Vector3 center, float radius)
        {
            focus = center;
            distance = Mathf.Clamp(radius * 3.2f + 25f, 25f, 700f);
            if (Mode != CameraMode.Orbit) SetMode(CameraMode.Orbit);
        }

        public void ResetView()
        {
            focus = DefaultFocus;
            yaw = 0f;
            pitch = 9f;
            distance = 190f;
        }

        public void SetCinematicPose(Vector3 position, Vector3 target, float fov, bool cut)
        {
            cinematicPosition = position;
            cinematicTarget = target;
            cinematicFov = fov;
            if (cut) snapNext = true;
        }

        void LateUpdate()
        {
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            if (Mode == CameraMode.Orbit) HandleOrbitInput();

            Vector3 targetPosition;
            Quaternion targetRotation;
            float targetFov;
            switch (Mode)
            {
                case CameraMode.Audience:
                {
                    float sway = Mathf.Sin(Time.unscaledTime * 0.21f) * 0.6f;
                    targetPosition = new Vector3(sway, 3.4f, NightEnvironment.ShoreZ - 14f);
                    targetRotation = Quaternion.LookRotation(DefaultFocus + new Vector3(0f, 2f, 0f) - targetPosition);
                    targetFov = 52f;
                    break;
                }
                case CameraMode.Aerial:
                    targetPosition = new Vector3(-150f, 190f, -210f);
                    targetRotation = Quaternion.LookRotation(DefaultFocus - new Vector3(0f, 20f, 0f) - targetPosition);
                    targetFov = 48f;
                    break;
                case CameraMode.Cinematic:
                    targetPosition = cinematicPosition;
                    targetRotation = Quaternion.LookRotation((cinematicTarget - cinematicPosition).sqrMagnitude > 1e-4f ? cinematicTarget - cinematicPosition : Vector3.forward);
                    targetFov = cinematicFov;
                    break;
                default:
                    targetRotation = Quaternion.Euler(pitch, yaw, 0f);
                    targetPosition = focus + targetRotation * new Vector3(0f, 0f, -distance);
                    targetFov = 50f;
                    break;
            }
            // Never dip below the lake surface.
            targetPosition.y = Mathf.Max(targetPosition.y, NightEnvironment.WaterLevel + 1.2f);

            float k = snapNext ? 1f : 1f - Mathf.Exp(-dt * (Mode == CameraMode.Orbit ? 14f : 3.5f));
            currentPosition = Vector3.Lerp(currentPosition, targetPosition, k);
            currentRotation = Quaternion.Slerp(currentRotation, targetRotation, k);
            currentFov = Mathf.Lerp(currentFov, targetFov, k);
            snapNext = false;

            transform.SetPositionAndRotation(currentPosition, currentRotation);
            cam.fieldOfView = currentFov;
        }

        void HandleOrbitInput()
        {
            if (InputBlocked != null && InputBlocked()) return;

            if (Input.touchSupported && Input.touchCount > 0)
            {
                HandleTouch();
                return;
            }

            Vector3 pointer = Input.mousePosition;
            bool anyDown = Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1) || Input.GetMouseButtonDown(2);
            if (anyDown)
            {
                dragging = IsPointerOverUi == null || !IsPointerOverUi(pointer);
                lastPointer = pointer;
            }
            bool held = Input.GetMouseButton(0) || Input.GetMouseButton(1) || Input.GetMouseButton(2);
            if (!held) dragging = false;

            if (dragging)
            {
                Vector3 delta = pointer - lastPointer;
                lastPointer = pointer;
                if (Input.GetMouseButton(0) && !Input.GetKey(KeyCode.LeftShift))
                {
                    Orbit(delta);
                }
                else
                {
                    Pan(delta);
                }
            }

            float scroll = Input.mouseScrollDelta.y;
            if (Mathf.Abs(scroll) > 0.01f && (IsPointerOverUi == null || !IsPointerOverUi(pointer)))
            {
                Zoom(-scroll);
            }
        }

        void HandleTouch()
        {
            if (Input.touchCount == 1)
            {
                Touch t = Input.GetTouch(0);
                if (t.phase == TouchPhase.Began) dragging = IsPointerOverUi == null || !IsPointerOverUi(t.position);
                if (dragging && t.phase == TouchPhase.Moved) Orbit(t.deltaPosition);
                if (t.phase == TouchPhase.Ended || t.phase == TouchPhase.Canceled) dragging = false;
                lastPinch = 0f;
            }
            else if (Input.touchCount >= 2)
            {
                Touch a = Input.GetTouch(0), b = Input.GetTouch(1);
                float pinch = Vector2.Distance(a.position, b.position);
                if (lastPinch > 0f && pinch > 0f) Zoom((lastPinch - pinch) / 60f);
                lastPinch = pinch;
                Pan((a.deltaPosition + b.deltaPosition) * 0.5f);
            }
        }

        void Orbit(Vector2 delta)
        {
            yaw += delta.x * orbitSensitivity;
            pitch = Mathf.Clamp(pitch - delta.y * orbitSensitivity, -8f, 82f);
        }

        void Pan(Vector2 delta)
        {
            Quaternion rot = Quaternion.Euler(pitch, yaw, 0f);
            float scale = distance * panSensitivity;
            focus -= rot * new Vector3(delta.x, delta.y, 0f) * scale;
            focus.y = Mathf.Clamp(focus.y, 0f, 220f);
            focus.x = Mathf.Clamp(focus.x, -600f, 600f);
            focus.z = Mathf.Clamp(focus.z, -600f, 600f);
        }

        void Zoom(float amount)
        {
            distance = Mathf.Clamp(distance * (1f + amount * zoomSensitivity), 12f, 900f);
        }
    }
}
