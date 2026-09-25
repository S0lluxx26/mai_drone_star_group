using System;
using System.Collections.Generic;
using System.Globalization;
using DroneStar.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace DroneStar.App
{
    /// <summary>Factory helpers for the studio's controls (all styled by Studio.uss).</summary>
    public static class Ui
    {
        public static VisualElement El(string className, params VisualElement[] children)
        {
            var e = new VisualElement();
            if (!string.IsNullOrEmpty(className))
            {
                foreach (string c in className.Split(' ')) e.AddToClassList(c);
            }
            foreach (VisualElement child in children) e.Add(child);
            return e;
        }

        public static Label Text(string text, string className)
        {
            var l = new Label(text);
            if (!string.IsNullOrEmpty(className))
            {
                foreach (string c in className.Split(' ')) l.AddToClassList(c);
            }
            return l;
        }

        public static Button Button(string label, Icon? icon, Action onClick, string extraClasses = null, string tooltip = null)
        {
            var b = new Button(onClick) { focusable = false };
            b.text = "";
            b.AddToClassList("ds-btn");
            if (!string.IsNullOrEmpty(extraClasses))
            {
                foreach (string c in extraClasses.Split(' ')) b.AddToClassList(c);
            }
            if (icon.HasValue) b.Add(new IconElement(icon.Value));
            if (!string.IsNullOrEmpty(label))
            {
                var text = new Label(label);
                text.AddToClassList("ds-btn-label");
                text.pickingMode = PickingMode.Ignore;
                if (!icon.HasValue) text.style.marginLeft = 0;
                b.Add(text);
            }
            else if (icon.HasValue)
            {
                b.AddToClassList("ds-btn--icon");
            }
            if (!string.IsNullOrEmpty(tooltip)) b.tooltip = tooltip;
            return b;
        }

        public static VisualElement Field(string label, VisualElement control, Label value = null)
        {
            var row = El("ds-field");
            row.Add(Text(label, "ds-field-label"));
            control.AddToClassList("ds-field-control");
            row.Add(control);
            if (value != null) row.Add(value);
            return row;
        }

        public static VisualElement Section(string title)
        {
            var s = El("ds-section");
            s.Add(Text(title.ToUpperInvariant(), "ds-section-title"));
            return s;
        }

        public static string Seconds(float s) => s.ToString("0.0", CultureInfo.InvariantCulture) + " s";

        public static string Clock(float seconds)
        {
            if (float.IsNaN(seconds) || seconds < 0f) seconds = 0f;
            int m = (int)(seconds / 60f);
            float rest = seconds - m * 60f;
            return m.ToString("00", CultureInfo.InvariantCulture) + ":" + rest.ToString("00.0", CultureInfo.InvariantCulture);
        }
    }

    /// <summary>
    /// Label + slider + value readout, bound to a getter/setter. Dragging produces one undo step:
    /// the setter receives a merge key while the pointer is down.
    /// </summary>
    public sealed class ValueSlider : VisualElement, IRefreshable
    {
        readonly Slider slider;
        readonly Label value;
        readonly Func<float> get;
        readonly Action<float, string> set;
        readonly Func<float, string> format;
        readonly float step;
        readonly string mergeKey;
        readonly Action endMerge;

        public ValueSlider(string label, float min, float max, float step, Func<float> get, Action<float, string> set,
            Func<float, string> format, string mergeKey, Action endMerge)
        {
            this.get = get;
            this.set = set;
            this.format = format ?? (v => v.ToString("0.#", CultureInfo.InvariantCulture));
            this.step = step;
            this.mergeKey = mergeKey;
            this.endMerge = endMerge;
            AddToClassList("ds-field");
            Add(Ui.Text(label, "ds-field-label"));
            slider = new Slider(min, max);
            slider.AddToClassList("ds-field-control");
            slider.RegisterValueChangedCallback(OnChanged);
            slider.RegisterCallback<PointerCaptureOutEvent>(_ => this.endMerge?.Invoke(), TrickleDown.TrickleDown);
            slider.RegisterCallback<FocusOutEvent>(_ => this.endMerge?.Invoke());
            Add(slider);
            value = Ui.Text("", "ds-field-value");
            Add(value);
            Refresh();
        }

        void OnChanged(ChangeEvent<float> e)
        {
            float v = step > 0f ? Mathf.Round(e.newValue / step) * step : e.newValue;
            set(v, mergeKey);
            value.text = format(v);
        }

        public void Refresh()
        {
            float v = get();
            slider.SetValueWithoutNotify(v);
            value.text = format(v);
        }

        public void SetEnabledState(bool enabled) => slider.SetEnabled(enabled);
    }

    /// <summary>A row of mutually exclusive buttons for an enum-like choice.</summary>
    public sealed class Segmented : VisualElement, IRefreshable
    {
        readonly List<Button> buttons = new List<Button>();
        readonly Func<int> get;

        public Segmented(IList<string> labels, Func<int> get, Action<int> set)
        {
            this.get = get;
            AddToClassList("ds-segmented");
            for (int i = 0; i < labels.Count; i++)
            {
                int index = i;
                Button b = Ui.Button(labels[i], null, () => set(index), "ds-btn--small");
                buttons.Add(b);
                Add(b);
            }
            Refresh();
        }

        public void Refresh()
        {
            int current = get();
            for (int i = 0; i < buttons.Count; i++) buttons[i].EnableInClassList("ds-btn--toggled", i == current);
        }
    }

    /// <summary>The vivid LED palette as swatches plus a hex field for exact colours.</summary>
    public sealed class SwatchPicker : VisualElement, IRefreshable
    {
        readonly List<VisualElement> swatches = new List<VisualElement>();
        readonly Func<LedColor> get;
        readonly TextField hex;

        public SwatchPicker(string label, Func<LedColor> get, Action<LedColor> set)
        {
            this.get = get;
            AddToClassList("ds-field");
            style.alignItems = Align.FlexStart;
            Add(Ui.Text(label, "ds-field-label"));
            var column = new VisualElement();
            column.style.flexGrow = 1;
            var row = Ui.El("ds-swatches");
            foreach (LedColor c in DemoShows.Palette)
            {
                LedColor color = c;
                var s = Ui.El("ds-swatch");
                s.focusable = false;
                s.focusable = false;
                s.style.backgroundColor = color.ToDisplay();
                s.tooltip = color.ToHex();
                s.RegisterCallback<ClickEvent>(_ => set(color));
                swatches.Add(s);
                row.Add(s);
            }
            column.Add(row);
            hex = new TextField { maxLength = 7 };
            hex.style.width = 90;
            hex.RegisterCallback<FocusOutEvent>(_ => Commit(set));
            hex.RegisterCallback<KeyDownEvent>(e =>
            {
                if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) Commit(set);
            });
            column.Add(hex);
            Add(column);
            Refresh();
        }

        void Commit(Action<LedColor> set)
        {
            if (LedColor.TryParseHex(hex.value, out LedColor c)) set(c);
            else Refresh();
        }

        public void Refresh()
        {
            LedColor current = get();
            string h = current.ToHex();
            for (int i = 0; i < swatches.Count; i++) swatches[i].EnableInClassList("ds-swatch--selected", DemoShows.Palette[i].ToHex() == h);
            if (hex.focusController?.focusedElement != hex) hex.SetValueWithoutNotify(h);
        }
    }

    public interface IRefreshable
    {
        void Refresh();
    }

    /// <summary>A generic refresh hook for one-off bindings.</summary>
    public sealed class Binding : IRefreshable
    {
        readonly Action refresh;

        public Binding(Action refresh)
        {
            this.refresh = refresh;
        }

        public void Refresh() => refresh();
    }
}
