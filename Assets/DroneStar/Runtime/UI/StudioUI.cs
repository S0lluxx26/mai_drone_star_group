using System;
using System.Collections.Generic;
using DroneStar.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace DroneStar.App
{
    /// <summary>
    /// The studio interface (UI Toolkit, built in code): top bar, cue list, inspector (Cue / Show / Safety),
    /// timeline with transport, dialogs, toasts, the loading screen and the demo-run overlay.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed partial class StudioUI : MonoBehaviour
    {
        [SerializeField] StyleSheet styleSheet;

        ShowStudioApp app;
        UIDocument document;
        VisualElement root;
        VisualElement editorLayer;
        VisualElement dialogLayer;
        VisualElement toastLayer;
        VisualElement loadingLayer;
        Label loadingStatus;
        VisualElement loadingFill;
        bool loadingDismissed;

        readonly List<IRefreshable> bindings = new List<IRefreshable>();

        public bool IsBound => app != null;

        public void Bind(ShowStudioApp owner)
        {
            app = owner;
            document = GetComponent<UIDocument>();
            root = document.rootVisualElement;
            root.pickingMode = PickingMode.Ignore;
            if (styleSheet != null) root.styleSheets.Add(styleSheet);
            root.Clear();

            var shell = Ui.El("ds-root");
            shell.pickingMode = PickingMode.Ignore;
            root.Add(shell);

            editorLayer = Ui.El("ds-root");
            editorLayer.pickingMode = PickingMode.Ignore;
            shell.Add(editorLayer);
            editorLayer.Add(BuildTopBar());
            var main = Ui.El("ds-main");
            main.pickingMode = PickingMode.Ignore;
            main.Add(BuildCuePanel());
            main.Add(BuildInspector());
            editorLayer.Add(main);
            editorLayer.Add(BuildTimeline());

            shell.Add(BuildDemoOverlay());

            toastLayer = Ui.El("ds-toasts");
            toastLayer.pickingMode = PickingMode.Ignore;
            shell.Add(toastLayer);

            dialogLayer = new VisualElement { pickingMode = PickingMode.Ignore };
            dialogLayer.style.position = Position.Absolute;
            dialogLayer.style.left = dialogLayer.style.right = dialogLayer.style.top = dialogLayer.style.bottom = 0;
            shell.Add(dialogLayer);

            shell.Add(BuildLoading());

            app.Session.Changed += OnDocumentChanged;
            app.ShowCompiled += OnShowCompiled;
            app.ValidationFinished += OnValidationFinished;
            app.SelectionChanged += OnSelectionChanged;
            app.Notified += Toast;
            app.Saved += RefreshTopBar;
            if (app.Demo != null) app.Demo.RunningChanged += OnDemoChanged;
            if (app.CameraRig != null) app.CameraRig.ModeChanged += _ => RefreshAll();

            OnDocumentChanged();

            // The editor is laid out for laptop and desktop screens; phones get a nudge toward the demo.
            float shortSide = Mathf.Min(Screen.width, Screen.height) / Mathf.Max(Screen.dpi / 96f, 1f);
            if (Screen.width < 1000 || shortSide < 560)
            {
                Toast("Drone Star Studio is designed for larger screens. Tap Demo Run to watch the show.", false);
            }
        }

        void OnDestroy()
        {
            if (app == null) return;
            app.Session.Changed -= OnDocumentChanged;
            app.ShowCompiled -= OnShowCompiled;
            app.ValidationFinished -= OnValidationFinished;
            app.SelectionChanged -= OnSelectionChanged;
            app.Notified -= Toast;
            app.Saved -= RefreshTopBar;
            if (app.Demo != null) app.Demo.RunningChanged -= OnDemoChanged;
        }

        // ------------------------------------------------------------------ queries used by the camera

        /// <summary>True when a screen point (pixels, bottom-left origin) is over an interactive UI element.</summary>
        public bool IsPointerOverUi(Vector2 screenPosition)
        {
            if (root?.panel == null) return false;
            Vector2 panelPoint = RuntimePanelUtils.ScreenToPanel(root.panel, new Vector2(screenPosition.x, Screen.height - screenPosition.y));
            return root.panel.Pick(panelPoint) != null;
        }

        /// <summary>True while a text field has keyboard focus (shortcuts and camera keys are suspended).</summary>
        public bool IsTyping
        {
            get
            {
                Focusable focused = root?.panel?.focusController?.focusedElement;
                for (var e = focused as VisualElement; e != null; e = e.parent)
                {
                    if (e is TextField) return true;
                }
                return false;
            }
        }

        // ------------------------------------------------------------------ change handling

        void OnDocumentChanged()
        {
            RefreshTopBar();
            RebuildCueList();
            RefreshInspector();
            RefreshAll();
        }

        void OnShowCompiled()
        {
            RebuildCueList();
            RebuildTimeline();
            RefreshInspector();
            RebuildSafety();
        }

        void OnValidationFinished()
        {
            RebuildSafety();
            RebuildTimelineMarkers();
            RebuildCueList();
        }

        void OnSelectionChanged()
        {
            RebuildCueList();
            RefreshInspector();
            RebuildTimeline();
        }

        void RefreshAll()
        {
            foreach (IRefreshable b in bindings) b.Refresh();
        }

        void Edit(string label, Action<ShowDocument> mutate, string mergeKey = null)
        {
            try
            {
                app.Session.Edit(label, mutate, mergeKey);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                Toast("That change could not be applied: " + e.Message, true);
            }
        }

        void EditCue(string label, Action<Cue> mutate, string mergeKey = null)
        {
            int index = app.SelectedCue;
            if (index < 0) return;
            Edit(label, d =>
            {
                if (index < d.Cues.Count) mutate(d.Cues[index]);
            }, mergeKey == null ? null : "cue" + index + "." + mergeKey);
        }

        Cue SelectedCue => app.SelectedCue >= 0 && app.SelectedCue < app.Session.Document.Cues.Count
            ? app.Session.Document.Cues[app.SelectedCue]
            : null;

        // ------------------------------------------------------------------ frame update

        void Update()
        {
            if (app == null) return;
            UpdateLoading();
            UpdateTransport();
            UpdateSafetyProgress();
            UpdateDemoOverlay();
            UpdateToasts();
            HandleShortcuts();
        }

        VisualElement BuildLoading()
        {
            loadingLayer = Ui.El("ds-loading");
            var mark = new IconElement(Icon.Star);
            mark.style.width = 46;
            mark.style.height = 46;
            mark.style.color = new Color(1f, 0.76f, 0.1f);
            mark.style.marginBottom = 10;
            loadingLayer.Add(mark);
            loadingLayer.Add(Ui.Text("DRONE STAR STUDIO", "ds-loading-title"));
            loadingStatus = Ui.Text("Preparing the night sky...", "ds-loading-status");
            loadingLayer.Add(loadingStatus);
            var bar = Ui.El("ds-progress");
            loadingFill = Ui.El("ds-progress-fill");
            bar.Add(loadingFill);
            loadingLayer.Add(bar);
            return loadingLayer;
        }

        void UpdateLoading()
        {
            if (loadingDismissed) return;
            if (app.Show != null)
            {
                loadingDismissed = true;
                loadingLayer.style.opacity = 0f;
                loadingLayer.pickingMode = PickingMode.Ignore;
                loadingLayer.schedule.Execute(() => loadingLayer.AddToClassList("hidden")).StartingIn(700);
                return;
            }
            loadingStatus.text = app.CompileError != null ? "Could not plan the show: " + app.CompileError : app.CompileStatus + "...";
            loadingFill.style.width = Length.Percent(app.CompileProgress * 100f);
        }

        // ------------------------------------------------------------------ keyboard

        /// <summary>True while a slider, dropdown, toggle or text field has keyboard focus.</summary>
        bool ControlHasFocus => root?.panel?.focusController?.focusedElement is VisualElement;

        void HandleShortcuts()
        {
            // Clicking the 3D view releases keyboard focus, so shortcuts work again after using a control.
            if ((Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1)) && !IsPointerOverUi(Input.mousePosition))
            {
                (root?.panel?.focusController?.focusedElement as VisualElement)?.Blur();
            }

            bool demoRunning = app.Demo != null && app.Demo.IsRunning;
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                if (dialogLayer.childCount > 0) CloseDialog();
                else if (demoRunning) app.Demo.End();
                return;
            }
            if (IsTyping || ControlHasFocus || dialogLayer.childCount > 0) return;

            bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl) || Input.GetKey(KeyCode.LeftCommand) || Input.GetKey(KeyCode.RightCommand);
            bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            float duration = app.Show != null ? app.Show.Duration : 0f;

            if (Input.GetKeyDown(KeyCode.Space)) app.TogglePlay();
            if (demoRunning) return;

            if (ctrl && Input.GetKeyDown(KeyCode.Z))
            {
                if (shift) app.Redo();
                else app.Undo();
            }
            if (ctrl && Input.GetKeyDown(KeyCode.Y)) app.Redo();
            if (ctrl && Input.GetKeyDown(KeyCode.S)) app.SaveToLibrary();
            if (ctrl) return;

            float step = shift ? 5f : 1f;
            if (Input.GetKeyDown(KeyCode.LeftArrow)) app.Seek(app.Clock.Time - step);
            if (Input.GetKeyDown(KeyCode.RightArrow)) app.Seek(app.Clock.Time + step);
            if (Input.GetKeyDown(KeyCode.Home)) app.Seek(0f);
            if (Input.GetKeyDown(KeyCode.End)) app.Seek(duration);
            if (Input.GetKeyDown(KeyCode.UpArrow)) app.Select(app.SelectedCue - 1);
            if (Input.GetKeyDown(KeyCode.DownArrow)) app.Select(app.SelectedCue + 1);
            if (Input.GetKeyDown(KeyCode.Delete)) app.DeleteCue(app.SelectedCue);
            if (Input.GetKeyDown(KeyCode.F)) app.FrameSelection();
            if (Input.GetKeyDown(KeyCode.Alpha1)) app.CameraRig.SetMode(CameraMode.Orbit);
            if (Input.GetKeyDown(KeyCode.Alpha2)) app.CameraRig.SetMode(CameraMode.Audience);
            if (Input.GetKeyDown(KeyCode.Alpha3)) app.CameraRig.SetMode(CameraMode.Aerial);
            if (Input.GetKeyDown(KeyCode.T))
            {
                app.Swarm.TrailsEnabled = !app.Swarm.TrailsEnabled;
                RefreshAll();
            }
            if (Input.GetKeyDown(KeyCode.D)) StartDemo();
            if (Input.GetKeyDown(KeyCode.H) || Input.GetKeyDown(KeyCode.F1)) ShowHelp();
        }

        void StartDemo()
        {
            if (app.Show == null || app.IsCompiling || app.ShowIsStale)
            {
                Toast("The show is still compiling, the demo will be ready in a moment.", false);
                return;
            }
            app.Demo.Begin();
        }

        // ------------------------------------------------------------------ toasts

        readonly List<(VisualElement element, float until)> toasts = new List<(VisualElement, float)>();

        public void Toast(string message, bool isError)
        {
            if (toastLayer == null) return;
            var t = Ui.Text(message, "ds-toast");
            if (isError) t.AddToClassList("ds-toast--error");
            toastLayer.Add(t);
            toasts.Add((t, Time.unscaledTime + (isError ? 6f : 3.5f)));
            while (toasts.Count > 4)
            {
                toasts[0].element.RemoveFromHierarchy();
                toasts.RemoveAt(0);
            }
        }

        void UpdateToasts()
        {
            for (int i = toasts.Count - 1; i >= 0; i--)
            {
                (VisualElement element, float until) = toasts[i];
                if (Time.unscaledTime < until) continue;
                if (!element.ClassListContains("ds-toast--leaving"))
                {
                    element.AddToClassList("ds-toast--leaving");
                    toasts[i] = (element, Time.unscaledTime + 0.3f);
                }
                else
                {
                    element.RemoveFromHierarchy();
                    toasts.RemoveAt(i);
                }
            }
        }
    }
}
