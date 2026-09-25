using UnityEngine;
using UnityEngine.UIElements;

namespace DroneStar.App
{
    public enum Icon
    {
        Play,
        Pause,
        SkipBack,
        SkipForward,
        Undo,
        Redo,
        Plus,
        Trash,
        Up,
        Down,
        Copy,
        Star,
        Film,
        Save,
        Folder,
        Download,
        Upload,
        Close,
        Check,
        Warning,
        Sound,
        Mute,
        Help,
        Loop,
        Target,
    }

    /// <summary>
    /// Vector icons drawn with Painter2D, so the UI needs no icon textures and no symbol font.
    /// Icons are drawn in a 16 × 16 box scaled to the element's size and tinted with its text colour.
    /// </summary>
    public sealed class IconElement : VisualElement
    {
        Icon icon;

        public IconElement(Icon icon)
        {
            this.icon = icon;
            AddToClassList("ds-icon");
            pickingMode = PickingMode.Ignore;
            generateVisualContent += Draw;
        }

        public Icon Icon
        {
            get => icon;
            set
            {
                if (icon == value) return;
                icon = value;
                MarkDirtyRepaint();
            }
        }

        void Draw(MeshGenerationContext ctx)
        {
            Rect r = contentRect;
            if (r.width <= 0f || r.height <= 0f) return;
            float s = Mathf.Min(r.width, r.height) / 16f;
            Vector2 o = new Vector2(r.x + (r.width - 16f * s) * 0.5f, r.y + (r.height - 16f * s) * 0.5f);
            Vector2 P(float x, float y) => o + new Vector2(x * s, y * s);

            Painter2D p = ctx.painter2D;
            Color c = resolvedStyle.color;
            p.fillColor = c;
            p.strokeColor = c;
            p.lineWidth = 1.7f * s;
            p.lineCap = LineCap.Round;
            p.lineJoin = LineJoin.Round;

            switch (icon)
            {
                case Icon.Play:
                    Fill(p, P(4, 2.5f), P(13.5f, 8), P(4, 13.5f));
                    break;
                case Icon.Pause:
                    Rect(p, P(3.5f, 2.5f), P(6.5f, 13.5f));
                    Rect(p, P(9.5f, 2.5f), P(12.5f, 13.5f));
                    break;
                case Icon.SkipBack:
                    Rect(p, P(2.5f, 3), P(4.3f, 13));
                    Fill(p, P(13.5f, 3), P(5, 8), P(13.5f, 13));
                    break;
                case Icon.SkipForward:
                    Rect(p, P(11.7f, 3), P(13.5f, 13));
                    Fill(p, P(2.5f, 3), P(11, 8), P(2.5f, 13));
                    break;
                case Icon.Undo:
                    p.BeginPath();
                    p.Arc(P(8.5f, 9), 4.8f * s, 180f, 400f);
                    p.Stroke();
                    Fill(p, P(1.2f, 9.5f), P(6.4f, 9.5f), P(3.7f, 5.2f));
                    break;
                case Icon.Redo:
                    p.BeginPath();
                    p.Arc(P(7.5f, 9), 4.8f * s, 140f, 360f);
                    p.Stroke();
                    Fill(p, P(9.6f, 9.5f), P(14.8f, 9.5f), P(12.3f, 5.2f));
                    break;
                case Icon.Plus:
                    Line(p, P(8, 3), P(8, 13));
                    Line(p, P(3, 8), P(13, 8));
                    break;
                case Icon.Trash:
                    Line(p, P(3, 4.5f), P(13, 4.5f));
                    Line(p, P(6.5f, 2.5f), P(9.5f, 2.5f));
                    Poly(p, false, P(4.5f, 5), P(5.3f, 13.5f), P(10.7f, 13.5f), P(11.5f, 5));
                    break;
                case Icon.Up:
                    Poly(p, false, P(3.5f, 10.5f), P(8, 5.5f), P(12.5f, 10.5f));
                    break;
                case Icon.Down:
                    Poly(p, false, P(3.5f, 5.5f), P(8, 10.5f), P(12.5f, 5.5f));
                    break;
                case Icon.Copy:
                    Poly(p, true, P(5.5f, 5.5f), P(13, 5.5f), P(13, 13.5f), P(5.5f, 13.5f));
                    Poly(p, false, P(3, 10.5f), P(3, 2.5f), P(10.5f, 2.5f));
                    break;
                case Icon.Star:
                    Star(p, P(8, 8.4f), 7f * s, 3f * s);
                    break;
                case Icon.Film:
                    Poly(p, true, P(2, 3.5f), P(14, 3.5f), P(14, 12.5f), P(2, 12.5f));
                    Fill(p, P(6.5f, 5.8f), P(10.8f, 8), P(6.5f, 10.2f));
                    break;
                case Icon.Save:
                    Poly(p, true, P(2.5f, 2.5f), P(11.5f, 2.5f), P(13.5f, 4.5f), P(13.5f, 13.5f), P(2.5f, 13.5f));
                    Rect(p, P(5, 9), P(11, 13.5f));
                    break;
                case Icon.Folder:
                    Poly(p, true, P(2, 4), P(6.5f, 4), P(8, 5.5f), P(14, 5.5f), P(14, 13), P(2, 13));
                    break;
                case Icon.Download:
                    Line(p, P(8, 2.5f), P(8, 10));
                    Poly(p, false, P(4.5f, 7), P(8, 10.5f), P(11.5f, 7));
                    Line(p, P(3, 13.5f), P(13, 13.5f));
                    break;
                case Icon.Upload:
                    Line(p, P(8, 11), P(8, 3));
                    Poly(p, false, P(4.5f, 6.5f), P(8, 3), P(11.5f, 6.5f));
                    Line(p, P(3, 13.5f), P(13, 13.5f));
                    break;
                case Icon.Close:
                    Line(p, P(4, 4), P(12, 12));
                    Line(p, P(12, 4), P(4, 12));
                    break;
                case Icon.Check:
                    Poly(p, false, P(3, 8.5f), P(6.5f, 12), P(13, 4.5f));
                    break;
                case Icon.Warning:
                    Poly(p, true, P(8, 2), P(14.5f, 13.5f), P(1.5f, 13.5f));
                    Line(p, P(8, 6.5f), P(8, 9.5f));
                    Line(p, P(8, 11.8f), P(8, 11.9f));
                    break;
                case Icon.Sound:
                case Icon.Mute:
                    Fill(p, P(2, 6), P(5, 6), P(9, 2.5f), P(9, 13.5f), P(5, 10), P(2, 10));
                    if (icon == Icon.Sound)
                    {
                        p.BeginPath();
                        p.Arc(P(9, 8), 3.2f * s, -50f, 50f);
                        p.Stroke();
                        p.BeginPath();
                        p.Arc(P(9, 8), 5.6f * s, -50f, 50f);
                        p.Stroke();
                    }
                    else
                    {
                        Line(p, P(11, 5.5f), P(15, 10.5f));
                        Line(p, P(15, 5.5f), P(11, 10.5f));
                    }
                    break;
                case Icon.Help:
                    p.BeginPath();
                    p.Arc(P(8, 5.8f), 3.2f * s, 180f, 405f);
                    p.LineTo(P(8, 10));
                    p.Stroke();
                    Line(p, P(8, 13.2f), P(8, 13.3f));
                    break;
                case Icon.Loop:
                    p.BeginPath();
                    p.Arc(P(8, 8), 5f * s, 20f, 330f);
                    p.Stroke();
                    Fill(p, P(10.5f, 1.8f), P(14.2f, 4.2f), P(10.4f, 6.4f));
                    break;
                case Icon.Target:
                    p.BeginPath();
                    p.Arc(P(8, 8), 5f * s, 0f, 360f);
                    p.Stroke();
                    Line(p, P(8, 1), P(8, 4));
                    Line(p, P(8, 12), P(8, 15));
                    Line(p, P(1, 8), P(4, 8));
                    Line(p, P(12, 8), P(15, 8));
                    break;
            }
        }

        static void Line(Painter2D p, Vector2 a, Vector2 b)
        {
            p.BeginPath();
            p.MoveTo(a);
            p.LineTo(b);
            p.Stroke();
        }

        static void Poly(Painter2D p, bool closed, params Vector2[] pts)
        {
            p.BeginPath();
            p.MoveTo(pts[0]);
            for (int i = 1; i < pts.Length; i++) p.LineTo(pts[i]);
            if (closed) p.ClosePath();
            p.Stroke();
        }

        static void Fill(Painter2D p, params Vector2[] pts)
        {
            p.BeginPath();
            p.MoveTo(pts[0]);
            for (int i = 1; i < pts.Length; i++) p.LineTo(pts[i]);
            p.ClosePath();
            p.Fill();
        }

        static void Rect(Painter2D p, Vector2 min, Vector2 max)
        {
            Fill(p, min, new Vector2(max.x, min.y), max, new Vector2(min.x, max.y));
        }

        static void Star(Painter2D p, Vector2 c, float outer, float inner)
        {
            p.BeginPath();
            for (int i = 0; i < 10; i++)
            {
                float r = (i & 1) == 0 ? outer : inner;
                float a = -Mathf.PI * 0.5f + Mathf.PI * i / 5f;
                Vector2 v = c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
                if (i == 0) p.MoveTo(v);
                else p.LineTo(v);
            }
            p.ClosePath();
            p.Fill();
        }
    }
}
