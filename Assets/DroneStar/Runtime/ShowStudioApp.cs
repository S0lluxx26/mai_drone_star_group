using System;
using System.Collections.Generic;
using System.Diagnostics;
using DroneStar.Core;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace DroneStar.App
{
    /// <summary>
    /// The studio's controller: owns the edit session, compiles the show incrementally in the background of
    /// each frame, runs the safety validator, drives playback and feeds the renderer, camera and UI.
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public sealed class ShowStudioApp : MonoBehaviour
    {
        const float EditSettleSeconds = 0.12f;

        /// <summary>
        /// Phones open the flagship with this many drones (the same show at its 2K fleet preset, pre-solved):
        /// sampling, colouring and uploading 8,192 drones every frame is more than a phone CPU keeps at 60 fps.
        /// The Show tab's fleet presets still offer 8K.
        /// </summary>
        public const int PhoneFleet = 2048;
        const double CompileBudgetMs = 10.0;
        const double ValidateBudgetMs = 5.0;

        [SerializeField] DroneSwarmRenderer swarm;
        [SerializeField] NightEnvironment environment;
        [SerializeField] ShowCameraRig cameraRig;
        [SerializeField] StudioUI ui;
        [SerializeField] DemoDirector demo;
        [SerializeField] AmbientScore score;
        [SerializeField] WebBridge bridge;
        [SerializeField, Range(0.5f, 3f)] float ledIntensity = 1f;
        [Tooltip("The model shape pack (Data/ShapePack.bytes, exported from Draw_in_3D by tools/export-shape-pack.mjs).")]
        [SerializeField] TextAsset shapePack;
        [Tooltip("Pre-solved flagship transitions (Data/DemoAssignments.bytes), so the demo plays at once on every device.")]
        [SerializeField] TextAsset demoAssignments;

        readonly ShowCompiler compiler = new ShowCompiler();
        readonly Stopwatch stopwatch = new Stopwatch();
        // Diagnostics for the log: wall time, work time and cache use of the current compile and safety check.
        float jobStartedAt, validationStartedAt;
        double jobWorkMs, validationWorkMs;
        int hitsAtStart, missesAtStart;
        CompileJob job;
        int jobRevision = -1;
        int compiledRevision = -1;
        float settleTimer;
        SafetyValidator validator;
        System.Numerics.Vector3[] corePositions = new System.Numerics.Vector3[0];
        LedColor[] coreColors = new LedColor[0];
        Vector3[] positions = new Vector3[0];
        Color[] colors = new Color[0];
        bool resetTrails = true;
        bool forceRender = true;
        float lastRenderedTime = -1f;

        public ShowEditSession Session { get; private set; }
        public CompiledShow Show { get; private set; }
        public ValidationReport Report { get; private set; }
        public PlaybackClock Clock { get; } = new PlaybackClock();
        public ShowLibrary Library { get; private set; }
        public int SelectedCue { get; private set; }
        public bool IsCompiling => job != null;
        public float CompileProgress => job != null ? job.Progress : 1f;
        public string CompileStatus => job != null ? job.Status : "Ready";
        public bool IsValidating => validator != null && !validator.IsDone;
        public float ValidationProgress => validator != null ? validator.Progress : 1f;
        public bool ShowIsStale => Session != null && compiledRevision != Session.Revision;

        /// <summary>Set when the latest compile failed; cleared by the next edit.</summary>
        public string CompileError { get; private set; }

        /// <summary>Brightness-weighted centre of the lit drones, used to aim cinematic shots.</summary>
        public Vector3 Focus { get; private set; } = ShowCameraRig.DefaultFocus;
        public float FocusRadius { get; private set; } = 30f;

        /// <summary>The drone the close-up camera follows, or -1.</summary>
        public int FollowedDrone { get; private set; } = -1;

        public DroneSwarmRenderer Swarm => swarm;
        public ShowCameraRig CameraRig => cameraRig;
        public DemoDirector Demo => demo;
        public AmbientScore Score => score;
        public WebBridge Bridge => bridge;

        public event Action ShowCompiled;
        public event Action ValidationFinished;
        public event Action Saved;
        public event Action SelectionChanged;
        public event Action<string, bool> Notified;

        void Awake()
        {
            Application.targetFrameRate = 60;
            Library = new ShowLibrary();
            LoadBundledData();
            if (swarm != null && (WebBridge.IsWeb || Application.isMobilePlatform)) swarm.DetailedCapacity = 128;
            ShowDocument flagship = DemoShows.StarGroupNight();
            if (Application.isMobilePlatform) ShowScaler.ResizeForDroneCount(flagship, PhoneFleet);
            Session = new ShowEditSession(flagship);
            Session.Changed += OnDocumentChanged;
            if (demo != null) demo.Initialize(this, cameraRig, score);
            if (cameraRig != null)
            {
                cameraRig.IsPointerOverUi = p => ui != null && ui.IsPointerOverUi(p);
                cameraRig.InputBlocked = () => ui != null && ui.IsTyping;
            }
            StartCompile();
        }

        void Start()
        {
            if (ui != null) ui.Bind(this);
            if (WantsAutoDemo()) StartCoroutine(BeginDemoWhenReady());
        }

        /// <summary>Registers the model shapes and the pre-solved demo transitions before the first compile.</summary>
        void LoadBundledData()
        {
            try
            {
                if (shapePack != null) ShapeLibrary.LoadPack(shapePack.bytes);
                else Debug.LogWarning("[DroneStar] No shape pack assigned: model formations will park their drones.");
            }
            catch (FormatException e)
            {
                Debug.LogError("[DroneStar] The shape pack is damaged: " + e.Message);
            }
            if (demoAssignments != null)
            {
                int entries = compiler.ImportCache(demoAssignments.bytes);
                if (entries == 0) Debug.LogWarning("[DroneStar] The baked demo assignments could not be read; the demo will be planned live.");
            }
        }

        static bool WantsAutoDemo()
        {
            foreach (string arg in Environment.GetCommandLineArgs())
            {
                if (arg == "-demo") return true;
            }
            string url = Application.absoluteURL ?? "";
            return url.Contains("demo=1") || url.Contains("#demo");
        }

        System.Collections.IEnumerator BeginDemoWhenReady()
        {
            while (Show == null || IsCompiling || ShowIsStale) yield return null;
            demo.Begin();
        }

        void OnDocumentChanged()
        {
            settleTimer = EditSettleSeconds;
            CompileError = null;
            int cues = Session.Document.Cues.Count;
            if (SelectedCue >= cues) Select(cues - 1, seek: false);
        }

        // ------------------------------------------------------------------ frame loop

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            if (settleTimer > 0f)
            {
                settleTimer -= dt;
                if (settleTimer <= 0f) StartCompile();
            }
            StepCompile();
            StepValidation();

            if (Show != null)
            {
                Clock.Tick(dt, Show.Duration);
                RenderFrame();
            }
        }

        void StartCompile()
        {
            // Restarting is cheap: finished assignments stay in the compiler's cache.
            job = compiler.Begin(Session.Document);
            jobRevision = Session.Revision;
            jobStartedAt = Time.realtimeSinceStartup;
            jobWorkMs = 0;
            hitsAtStart = compiler.CacheHits;
            missesAtStart = compiler.CacheMisses;
        }

        void StepCompile()
        {
            if (job == null) return;
            stopwatch.Restart();
            try
            {
                while (!job.Step())
                {
                    if (stopwatch.Elapsed.TotalMilliseconds > CompileBudgetMs)
                    {
                        jobWorkMs += stopwatch.Elapsed.TotalMilliseconds;
                        return;
                    }
                }
                jobWorkMs += stopwatch.Elapsed.TotalMilliseconds;
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                CompileError = e.Message;
                Notify("Could not plan this show: " + e.Message + " Undo the last change to continue.", true);
                job = null;
                return;
            }

            if (Show != null && Show.DroneCount != job.Result.DroneCount) StopCloseUp();
            Debug.Log(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "[DroneStar] Planned {0} drones: {1} transitions from the cache, {2} solved; {3:0.0} s of work over {4:0.0} s",
                job.Result.DroneCount, compiler.CacheHits - hitsAtStart, compiler.CacheMisses - missesAtStart,
                jobWorkMs / 1000.0, Time.realtimeSinceStartup - jobStartedAt));
            Show = job.Result;
            compiledRevision = jobRevision;
            job = null;
            Clock.Seek(Clock.Time, Show.Duration);
            Vector3[] pads = ToUnity(Show.PadPositions);
            if (environment != null) environment.SetPads(pads);
            if (swarm != null) swarm.SetPads(pads);
            if (cameraRig != null && Show.CueTimings.Count > 0)
            {
                // Typical formation centre and size, so the fixed views frame big fleets (big shapes) too.
                Vector3 sum = Vector3.zero;
                float half = 0f;
                foreach (CueTiming t in Show.CueTimings)
                {
                    sum += t.Formation.Center.ToUnity();
                    half += t.Formation.HalfSize;
                }
                cameraRig.SetShowEnvelope(sum / Show.CueTimings.Count, half / Show.CueTimings.Count * 1.25f);
            }
            // Any recompile can move drones, so old trail history would draw streaks to stale positions.
            resetTrails = true;
            forceRender = true;
            // 10 Hz on the web and for large fleets (closest approach is still solved between samples).
            validator = new SafetyValidator(Show, WebBridge.IsWeb || Show.DroneCount > 1000 ? 0.1f : SafetyValidator.DefaultStep);
            validationStartedAt = Time.realtimeSinceStartup;
            validationWorkMs = 0;
            Report = null;
            ShowCompiled?.Invoke();
        }

        void StepValidation()
        {
            if (validator == null || validator.IsDone || job != null) return;
            stopwatch.Restart();
            try
            {
                // One time sample per step: at thousands of drones a sample alone can take milliseconds.
                while (!validator.Step(1))
                {
                    if (stopwatch.Elapsed.TotalMilliseconds > ValidateBudgetMs)
                    {
                        validationWorkMs += stopwatch.Elapsed.TotalMilliseconds;
                        return;
                    }
                }
                validationWorkMs += stopwatch.Elapsed.TotalMilliseconds;
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                Notify("The safety check stopped unexpectedly: " + e.Message, true);
                validator = null;
                return;
            }
            Report = validator.Report;
            Debug.Log(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "[DroneStar] Safety check {0}: {1:0.0} s of work over {2:0.0} s", Report.Passed ? "passed" : "found issues",
                validationWorkMs / 1000.0, Time.realtimeSinceStartup - validationStartedAt));
            ValidationFinished?.Invoke();
        }

        void RenderFrame()
        {
            int n = Show.DroneCount;
            if (corePositions.Length != n)
            {
                corePositions = new System.Numerics.Vector3[n];
                coreColors = new LedColor[n];
                positions = new Vector3[n];
                colors = new Color[n];
            }
            float t = Clock.Time;
            // Paused on an unchanged show: the meshes already hold this frame; only re-issue the instanced draws.
            if (!forceRender && t == lastRenderedTime && !resetTrails)
            {
                if (swarm != null) swarm.DrawBodies(positions, n);
                return;
            }
            forceRender = false;
            Show.Sample(t, corePositions, coreColors);

            Vector3 weighted = Vector3.zero;
            float weightSum = 0f;
            for (int i = 0; i < n; i++)
            {
                Vector3 p = corePositions[i].ToUnity();
                LedColor c = coreColors[i];
                positions[i] = p;
                colors[i] = c.ToRender() * ledIntensity;
                float w = c.MaxComponent + 0.02f;
                weighted += p * w;
                weightSum += w;
            }
            Vector3 focus = weighted / Mathf.Max(weightSum, 1e-4f);
            float spread = 0f;
            for (int i = 0; i < n; i++)
            {
                float w = coreColors[i].MaxComponent + 0.02f;
                spread += (positions[i] - focus).sqrMagnitude * w;
            }
            Focus = focus;
            FocusRadius = Mathf.Sqrt(spread / Mathf.Max(weightSum, 1e-4f)) * 1.6f;

            bool jumped = Mathf.Abs(t - lastRenderedTime) > 1f;
            if (swarm != null) swarm.Render(positions, colors, n, t, resetTrails || jumped);
            resetTrails = false;
            lastRenderedTime = t;
        }

        static Vector3[] ToUnity(System.Numerics.Vector3[] src)
        {
            var dst = new Vector3[src.Length];
            for (int i = 0; i < src.Length; i++) dst[i] = src[i].ToUnity();
            return dst;
        }

        // ------------------------------------------------------------------ selection & playback

        public void Select(int cueIndex, bool seek = true)
        {
            int count = Session.Document.Cues.Count;
            int clamped = count == 0 ? -1 : Mathf.Clamp(cueIndex, 0, count - 1);
            bool changed = clamped != SelectedCue;
            SelectedCue = clamped;
            if (seek && clamped >= 0 && Show != null && !Clock.Playing && !ShowIsStale && clamped < Show.CueTimings.Count)
            {
                CueTiming timing = Show.CueTimings[clamped];
                Clock.Seek(timing.HoldStart + Mathf.Min(0.5f, timing.HoldSeconds * 0.5f), Show.Duration);
            }
            if (changed) SelectionChanged?.Invoke();
        }

        public void TogglePlay()
        {
            if (Show != null) Clock.Toggle(Show.Duration);
        }

        public void Seek(float t)
        {
            if (Show != null) Clock.Seek(t, Show.Duration);
        }

        public void FrameSelection()
        {
            if (Show == null || SelectedCue < 0 || SelectedCue >= Show.CueTimings.Count) return;
            FormationResult f = Show.CueTimings[SelectedCue].Formation;
            cameraRig.Frame(f.Center.ToUnity(), f.HalfSize);
        }

        // ------------------------------------------------------------------ cue editing

        /// <summary>Inserts a new cue after the selection; <paramref name="model"/> picks the shape for Model cues.</summary>
        public void AddCue(FormationKind kind, string model = null)
        {
            int at = SelectedCue < 0 ? Session.Document.Cues.Count : SelectedCue + 1;
            string label = kind == FormationKind.Model ? model ?? "Robot" : DemoShows.KindLabel(kind);
            Session.Edit("Add " + label, d => d.Cues.Insert(Mathf.Min(at, d.Cues.Count), DemoShows.NewCue(kind, d.Cues.Count, d, model ?? "Robot")));
            Select(at);
        }

        /// <summary>Grows the selected cue's shape until it lights every drone (none left parked dark).</summary>
        public void FitSelectedToFleet()
        {
            if (!ValidCue(SelectedCue)) return;
            ShowDocument doc = Session.Document;
            Cue cue = doc.Cues[SelectedCue];
            HoldMotion.Envelope(cue.Motion, out float growth, out float margin);
            float size = FormationGenerator.SizeToLight(cue.Formation, doc.DroneCount, doc.Limits.FormationSpacing, growth, margin);
            if (Mathf.Abs(size - cue.Formation.Size) < 0.05f) return;
            int index = SelectedCue;
            Session.Edit("Enlarge to light all", d => d.Cues[index].Formation.Size = size);
            if (size >= ShowBounds.MaxSize - 0.01f) Notify("Even at its largest this shape cannot hold every drone; add layers or pick another shape.", false);
        }

        /// <summary>Changes the fleet size and rescales every shape, altitude and limit to suit it.</summary>
        public void SetFleetSize(int count)
        {
            count = Mathf.Clamp(count, 1, ShowBounds.MaxDrones);
            if (count == Session.Document.DroneCount) return;
            Session.Edit("Fleet of " + count, d => ShowScaler.ResizeForDroneCount(d, count));
            Notify("Resized the show for " + count.ToString("N0", System.Globalization.CultureInfo.InvariantCulture) + " drones", false);
        }

        /// <summary>
        /// Close-up: the orbit camera follows one lit drone on the audience side of the current formation, a few
        /// metres away, so the airframe, its spinning props and LED bulb can be seen in flight. Pressing again
        /// moves to a neighbouring drone.
        /// </summary>
        public void FrameCloseUp()
        {
            int n = Mathf.Min(positions.Length, colors.Length);
            if (Show == null || n == 0 || cameraRig == null) return;
            Vector3 target = Focus + new Vector3(FocusRadius * 0.3f, -FocusRadius * 0.15f, -FocusRadius * 1.5f);
            int best = -1;
            float bestDistance = float.MaxValue;
            for (int pass = 0; pass < 2 && best < 0; pass++)
            {
                for (int i = 0; i < n; i++)
                {
                    if ((i == FollowedDrone && cameraRig.IsFollowing) || (pass == 0 && colors[i].maxColorComponent < 0.05f)) continue;
                    float d = (positions[i] - target).sqrMagnitude;
                    if (d < bestDistance)
                    {
                        bestDistance = d;
                        best = i;
                    }
                }
            }
            if (best < 0) return;
            FollowedDrone = best;
            int index = best;
            cameraRig.Follow(() => index < positions.Length ? positions[index] : Focus, 2.8f);
            Notify("Following drone " + (best + 1) + ". Drag to orbit, scroll to zoom, F to frame the formation.", false);
        }

        /// <summary>Leaves the close-up (the followed drone's index means nothing in a different show).</summary>
        void StopCloseUp()
        {
            FollowedDrone = -1;
            if (cameraRig != null) cameraRig.StopFollowing();
        }

        public void DuplicateCue(int index)
        {
            if (!ValidCue(index)) return;
            Session.Edit("Duplicate cue", d =>
            {
                Cue copy = d.Cues[index].Clone();
                copy.Name = copy.Name + " copy";
                d.Cues.Insert(index + 1, copy);
            });
            Select(index + 1);
        }

        public void DeleteCue(int index)
        {
            if (!ValidCue(index)) return;
            string name = Session.Document.Cues[index].Name;
            Session.Edit("Delete “" + name + "”", d => d.Cues.RemoveAt(index));
            Select(Mathf.Min(index, Session.Document.Cues.Count - 1), seek: false);
            Notify("Deleted “" + name + "” (Ctrl+Z to undo)", false);
        }

        public void MoveCue(int index, int delta)
        {
            int target = index + delta;
            if (!ValidCue(index) || !ValidCue(target)) return;
            Session.Edit("Reorder cues", d =>
            {
                Cue c = d.Cues[index];
                d.Cues.RemoveAt(index);
                d.Cues.Insert(target, c);
            });
            Select(target, seek: false);
        }

        bool ValidCue(int index) => index >= 0 && index < Session.Document.Cues.Count;

        public void Undo()
        {
            string label = Session.UndoLabel;
            if (Session.Undo()) Notify("Undid " + label, false);
        }

        public void Redo()
        {
            string label = Session.RedoLabel;
            if (Session.Redo()) Notify("Redid " + label, false);
        }

        // ------------------------------------------------------------------ files

        public void NewFromTemplate(DemoShows.Template template)
        {
            if (demo != null && demo.IsRunning) demo.End();
            StopCloseUp();
            Session.Load(template.Create(), markSaved: true);
            Clock.Pause();
            Clock.Seek(0f, float.MaxValue);
            Select(0, seek: false);
            resetTrails = true;
            Notify("Opened template “" + template.Name + "”", false);
        }

        /// <summary>Saves the show on this device. Returns false (after telling the user) if it failed.</summary>
        public bool SaveToLibrary()
        {
            try
            {
                string name = Library.Save(Session.Document.Title, Session.ToJson());
                Session.MarkSaved();
                Saved?.Invoke();
                Notify("Saved “" + name + "” to " + Library.Location, false);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                Notify("Save failed: " + e.Message, true);
                return false;
            }
        }

        public void OpenFromLibrary(string name)
        {
            try
            {
                LoadJson(Library.Load(name), "Opened “" + name + "”");
            }
            catch (Exception e)
            {
                Notify("Could not open “" + name + "”: " + e.Message, true);
            }
        }

        public void DeleteFromLibrary(string name)
        {
            try
            {
                Library.Delete(name);
                Notify("Deleted saved show “" + name + "”", false);
            }
            catch (Exception e)
            {
                Notify("Could not delete: " + e.Message, true);
            }
        }

        /// <summary>Loads a show file's text; returns false (and explains why) if it is not a valid show.</summary>
        public bool LoadJson(string json, string successMessage)
        {
            ShowDocument doc;
            try
            {
                doc = ShowSerializer.FromJson(json);
            }
            catch (FormatException e)
            {
                Notify("Not a valid show file: " + e.Message, true);
                return false;
            }
            if (demo != null && demo.IsRunning) demo.End();
            StopCloseUp();
            Session.Load(doc, markSaved: true);
            Clock.Pause();
            Clock.Seek(0f, float.MaxValue);
            Select(0, seek: false);
            resetTrails = true;
            Notify(successMessage, false);
            return true;
        }

        public void ImportFile()
        {
            if (bridge != null && bridge.PickTextFile(text => LoadJson(text, "Imported show"), err => Notify(err, true))) return;
            Notify("Put .dronestar.json files in " + WebBridge.ImportFolder + " and pick them from Open.", false);
        }

        public List<string> ImportableFiles()
        {
            var list = new List<string>();
            if (WebBridge.IsWeb || !System.IO.Directory.Exists(WebBridge.ImportFolder)) return list;
            foreach (string f in System.IO.Directory.GetFiles(WebBridge.ImportFolder, "*.json")) list.Add(f);
            return list;
        }

        public void ImportFromPath(string path)
        {
            try
            {
                var info = new System.IO.FileInfo(path);
                if (info.Length > WebBridge.MaxImportBytes)
                {
                    Notify("That file is larger than 2 MB.", true);
                    return;
                }
                LoadJson(System.IO.File.ReadAllText(path), "Imported " + info.Name);
            }
            catch (Exception e)
            {
                Notify("Import failed: " + e.Message, true);
            }
        }

        /// <summary>Largest trajectory table offered (rows = drones × samples); keeps web exports under ~100 MB.</summary>
        const float MaxTrajectoryRows = 2_000_000f;

        public void ExportShowFile() => Export(ShowLibrary.SafeName(Session.Document.Title) + ShowSerializer.FileExtension, Session.ToJson, "application/json");

        public void ExportTrajectories()
        {
            if (!EnsureFresh()) return;
            CompiledShow show = Show;
            float rate = Mathf.Clamp(MaxTrajectoryRows / Mathf.Max(1f, show.DroneCount * show.Duration), 0.5f, 4f);
            if (rate < 4f) Notify(string.Format("Large show: trajectories are sampled at {0:0.#} Hz.", rate), false);
            Export(ShowLibrary.SafeName(show.Document.Title) + " trajectories.csv", () => ShowExporter.ToCsv(show, rate), "text/csv");
        }

        public void ExportFlightReport()
        {
            if (!EnsureFresh()) return;
            if (Report == null)
            {
                Notify("The safety check is still running, try again in a moment.", false);
                return;
            }
            CompiledShow show = Show;
            ValidationReport report = Report;
            Export(ShowLibrary.SafeName(show.Document.Title) + " flight report.md", () => ShowExporter.ToFlightReport(show, report), "text/markdown");
        }

        bool EnsureFresh()
        {
            if (Show != null && !ShowIsStale && !IsCompiling) return true;
            Notify("The show is still compiling, try again in a moment.", false);
            return false;
        }

        void Export(string fileName, Func<string> produce, string mime)
        {
            try
            {
                Notify(bridge != null ? bridge.SaveText(fileName, produce(), mime) : "Export unavailable", bridge == null);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                Notify("Export failed: " + e.Message, true);
            }
        }

        public void Notify(string message, bool isError)
        {
            if (isError) Debug.LogWarning(message);
            Notified?.Invoke(message, isError);
        }
    }
}
