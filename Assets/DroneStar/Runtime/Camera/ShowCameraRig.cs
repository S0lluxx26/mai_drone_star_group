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
    /// Studio camera. Orbit: drag to orbit, right/middle-drag to pan, wheel or pinch to zoom; the orbit can
    /// follow a moving point (a drone, for close-ups) until the user pans or frames something else.
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
        Func<Vector3> follow;
        Vector3 savedFocus;
        float savedDistance, savedYaw, savedPitch;
        Vector3 showCenter = DefaultFocus;
        float showRadius = 70f;
        bool orbitTouched;

        public CameraMode Mode { get; private set; } = CameraMode.Orbit;

        /// <summary>True while the orbit camera is following a moving point.</summary>
        public bool IsFollowing => follow != null && Mode == CameraMode.Orbit;

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
            if (mode != CameraMode.Orbit) StopFollowing();
            snapNext = mode == CameraMode.Cinematic;
            ModeChanged?.Invoke(mode);
        }

        /// <summary>
        /// The show's typical formation centre and half-size (from the app after each compile). The audience and
        /// aerial views and the default orbit frame it, so bigger fleets, which fly bigger shapes, stay in view.
        /// </summary>
        public void SetShowEnvelope(Vector3 center, float radius)
        {
            showCenter = center;
            showRadius = Mathf.Max(radius, 10f);
            if (!orbitTouched && follow == null) FrameShow();
        }

        void FrameShow()
        {
            focus = showCenter;
            distance = Mathf.Clamp(showRadius * 2.7f + 30f, 60f, 1000f);
        }

        /// <summary>Frames a sphere in Orbit mode (e.g. the selected formation).</summary>
        public void Frame(Vector3 center, float radius)
        {
            orbitTouched = true;
            follow = null;
            focus = center;
            distance = Mathf.Clamp(radius * 3.2f + 25f, 25f, 700f);
            if (Mode != CameraMode.Orbit) SetMode(CameraMode.Orbit);
        }

        /// <summary>Orbits <paramref name="dist"/> metres from a moving point, seen from the audience side.</summary>
        public void Follow(Func<Vector3> target, float dist)
        {
            if (target == null) return;
            if (follow == null)
            {
                // Remember the orbit this close-up interrupts, to return to it afterwards.
                savedFocus = focus;
                savedDistance = distance;
                savedYaw = yaw;
                savedPitch = pitch;
            }
            orbitTouched = true;
            follow = target;
            focus = target();
            distance = dist;
            yaw = 22f;
            pitch = 8f;
            if (Mode != CameraMode.Orbit) SetMode(CameraMode.Orbit);
        }

        /// <summary>Ends a close-up and returns to the orbit it interrupted.</summary>
        public void StopFollowing()
        {
            if (follow == null) return;
            follow = null;
            focus = savedFocus;
            distance = savedDistance;
            yaw = savedYaw;
            pitch = savedPitch;
        }

        public void ResetView()
        {
            follow = null;
            orbitTouched = false;
            FrameShow();
            yaw = 0f;
            pitch = 9f;
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
                    // From the shore, look up to take in a typical formation and keep a sliver of the waterline;
                    // a big show gets a wider lens, as a big show fills more of a spectator's view.
                    float d = Mathf.Max(showCenter.z - targetPosition.z, 50f);
                    float high = Mathf.Atan2(showCenter.y + showRadius - targetPosition.y, d);
                    float low = Mathf.Min(Mathf.Atan2(showCenter.y - showRadius - targetPosition.y, d), -3f * Mathf.Deg2Rad);
                    float aim = 0.5f * (high + low);
                    targetRotation = Quaternion.LookRotation(new Vector3(showCenter.x - targetPosition.x, Mathf.Tan(aim) * d, d));
                    targetFov = Mathf.Clamp((high - low) * Mathf.Rad2Deg * 1.1f, 52f, 80f);
                    break;
                }
                case CameraMode.Aerial:
                {
                    float scale = Mathf.Max(1f, showRadius / 70f);
                    targetPosition = showCenter + new Vector3(-150f, 132f, -210f) * scale;
                    targetRotation = Quaternion.LookRotation(showCenter - new Vector3(0f, 20f * scale, 0f) - targetPosition);
                    targetFov = 48f;
                    break;
                }
                case CameraMode.Cinematic:
                    targetPosition = cinematicPosition;
                    targetRotation = Quaternion.LookRotation((cinematicTarget - cinematicPosition).sqrMagnitude > 1e-4f ? cinematicTarget - cinematicPosition : Vector3.forward);
                    targetFov = cinematicFov;
                    break;
                default:
                    if (follow != null) focus = follow();
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
                // While following a drone two fingers only zoom; panning would drop the close-up.
                if (follow == null) Pan((a.deltaPosition + b.deltaPosition) * 0.5f);
            }
        }

        void Orbit(Vector2 delta)
        {
            orbitTouched = true;
            yaw += delta.x * orbitSensitivity;
            pitch = Mathf.Clamp(pitch - delta.y * orbitSensitivity, -8f, 82f);
        }

        void Pan(Vector2 delta)
        {
            if (delta.sqrMagnitude > 0.01f) orbitTouched = true;
            if (delta.sqrMagnitude > 0.01f && follow != null)
            {
                // Panning away from a followed drone keeps the view where it is, at a normal orbit range.
                follow = null;
                distance = Mathf.Max(distance, 8f);
            }
            Quaternion rot = Quaternion.Euler(pitch, yaw, 0f);
            float scale = distance * panSensitivity;
            focus -= rot * new Vector3(delta.x, delta.y, 0f) * scale;
            focus.y = Mathf.Clamp(focus.y, 0f, 400f);
            focus.x = Mathf.Clamp(focus.x, -600f, 600f);
            focus.z = Mathf.Clamp(focus.z, -600f, 600f);
        }

        void Zoom(float amount)
        {
            orbitTouched = true;
            distance = Mathf.Clamp(distance * (1f + amount * zoomSensitivity), follow != null ? 1.5f : 8f, 1100f);
        }
    }
}
