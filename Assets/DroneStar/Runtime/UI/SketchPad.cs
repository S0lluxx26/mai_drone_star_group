using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using NVector2 = System.Numerics.Vector2;
using NVector3 = System.Numerics.Vector3;

namespace DroneStar.App
{
    /// <summary>
    /// A drawing pad for Sketch formations. Shows the cue's current points as dots and records freehand
    /// strokes (mouse, pen or finger). Coordinates are normalised to the pad's half-width, y up, which is
    /// exactly the space <see cref="Core.CustomShapes.FromStrokes"/> expects.
    /// </summary>
    public sealed class SketchPad : VisualElement, IRefreshable
    {
        readonly Func<IReadOnlyList<NVector3>> points;
        readonly List<List<NVector2>> strokes = new List<List<NVector2>>();
        List<NVector2> current;
        int activePointer = -1;

        public SketchPad(Func<IReadOnlyList<NVector3>> currentPoints)
        {
            points = currentPoints;
            AddToClassList("ds-sketch");
            focusable = false;
            generateVisualContent += Draw;
            RegisterCallback<PointerDownEvent>(OnDown);
            RegisterCallback<PointerMoveEvent>(OnMove);
            RegisterCallback<PointerUpEvent>(OnUp);
            RegisterCallback<PointerCaptureOutEvent>(_ => EndStroke());
        }

        public bool HasStrokes => strokes.Count > 0;

        /// <summary>Raised when a stroke is finished or the pad is cleared.</summary>
        public event Action StrokesChanged;

        public IReadOnlyList<IReadOnlyList<NVector2>> Strokes => strokes;

        public void ClearStrokes()
        {
            strokes.Clear();
            current = null;
            MarkDirtyRepaint();
            StrokesChanged?.Invoke();
        }

        public void Refresh() => MarkDirtyRepaint();

        void OnDown(PointerDownEvent e)
        {
            if (activePointer >= 0) return;
            activePointer = e.pointerId;
            this.CapturePointer(e.pointerId);
            current = new List<NVector2> { ToNormalised(e.localPosition) };
            strokes.Add(current);
            e.StopPropagation();
            MarkDirtyRepaint();
        }

        void OnMove(PointerMoveEvent e)
        {
            if (current == null || e.pointerId != activePointer) return;
            NVector2 p = ToNormalised(e.localPosition);
            if (NVector2.Distance(p, current[current.Count - 1]) < 0.012f) return;
            current.Add(p);
            e.StopPropagation();
            MarkDirtyRepaint();
        }

        void OnUp(PointerUpEvent e)
        {
            if (e.pointerId != activePointer) return;
            this.ReleasePointer(e.pointerId);
            e.StopPropagation();
            EndStroke();
        }

        void EndStroke()
        {
            if (current == null) return;
            current = null;
            activePointer = -1;
            StrokesChanged?.Invoke();
        }

        /// <summary>Pixels per normalised unit: the shorter side spans −1..1, so the unit shape always fits.</summary>
        float Scale
        {
            get
            {
                Rect r = contentRect;
                return Mathf.Max(Mathf.Min(r.width, r.height) * 0.5f - 6f, 1f);
            }
        }

        NVector2 ToNormalised(Vector2 local)
        {
            Rect r = contentRect;
            float x = (local.x - r.width * 0.5f) / Scale;
            float y = (r.height * 0.5f - local.y) / Scale;
            // The sanitizer keeps custom points within ±1.5, the pad's wide sides reach about that far.
            return new NVector2(Mathf.Clamp(x, -1.5f, 1.5f), Mathf.Clamp(y, -1.5f, 1.5f));
        }

        Vector2 ToLocal(float x, float y)
        {
            Rect r = contentRect;
            return new Vector2(r.width * 0.5f + x * Scale, r.height * 0.5f - y * Scale);
        }

        void Draw(MeshGenerationContext ctx)
        {
            Rect r = contentRect;
            if (r.width <= 0f || r.height <= 0f) return;
            Painter2D p = ctx.painter2D;

            // Guide cross and unit circle.
            p.strokeColor = new Color(1f, 1f, 1f, 0.07f);
            p.lineWidth = 1f;
            p.BeginPath();
            p.MoveTo(new Vector2(r.width * 0.5f, 0f));
            p.LineTo(new Vector2(r.width * 0.5f, r.height));
            p.MoveTo(new Vector2(0f, r.height * 0.5f));
            p.LineTo(new Vector2(r.width, r.height * 0.5f));
            p.Stroke();

            // The cue's current points (what the drones will fly), unless the user is redrawing.
            IReadOnlyList<NVector3> existing = strokes.Count == 0 ? points() : null;
            if (existing != null)
            {
                p.fillColor = new Color(0.09f, 0.88f, 1f, 0.85f);
                int stride = Mathf.Max(1, existing.Count / 400);
                for (int i = 0; i < existing.Count; i += stride)
                {
                    Vector2 c = ToLocal(existing[i].X, existing[i].Y);
                    p.BeginPath();
                    p.Arc(c, 1.6f, 0f, 360f);
                    p.Fill();
                }
            }

            p.strokeColor = new Color(1f, 0.76f, 0.1f, 1f);
            p.lineWidth = 2.5f;
            p.lineCap = LineCap.Round;
            p.lineJoin = LineJoin.Round;
            foreach (List<NVector2> s in strokes)
            {
                if (s.Count == 1)
                {
                    p.fillColor = p.strokeColor;
                    p.BeginPath();
                    p.Arc(ToLocal(s[0].X, s[0].Y), 2.2f, 0f, 360f);
                    p.Fill();
                    continue;
                }
                p.BeginPath();
                p.MoveTo(ToLocal(s[0].X, s[0].Y));
                for (int i = 1; i < s.Count; i++) p.LineTo(ToLocal(s[i].X, s[i].Y));
                p.Stroke();
            }
        }
    }
}
