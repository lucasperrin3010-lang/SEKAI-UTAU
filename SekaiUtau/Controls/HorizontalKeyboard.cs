using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using SekaiUtau.Core;
using SekaiUtau.Core.Util;

namespace SekaiUtau.App.Controls {
    /// <summary>
    /// Horizontal piano keyboard placed under the notes, the way Vocaloid's piano roll
    /// has its keyboard at the bottom. Low notes are on the left, high notes on the right.
    /// Pressing or dragging over the keys plays their tones.
    /// </summary>
    class HorizontalKeyboard : Control {
        public static readonly StyledProperty<int> LowToneProperty =
            AvaloniaProperty.Register<HorizontalKeyboard, int>(nameof(LowTone), 36);
        public static readonly StyledProperty<int> HighToneProperty =
            AvaloniaProperty.Register<HorizontalKeyboard, int>(nameof(HighTone), 95);

        public int LowTone {
            get => GetValue(LowToneProperty);
            set => SetValue(LowToneProperty, value);
        }
        public int HighTone {
            get => GetValue(HighToneProperty);
            set => SetValue(HighToneProperty, value);
        }

        private static readonly IBrush whiteKeyBrush = new SolidColorBrush(Color.Parse("#FBFCFE"));
        private static readonly IPen whiteKeyPen = new Pen(new SolidColorBrush(Color.Parse("#C9D2E0")), 1);
        private static readonly IBrush centerKeyBrush = new SolidColorBrush(Color.Parse("#2E2E2E"));
        private static readonly IBrush blackKeyBrush = new SolidColorBrush(Color.Parse("#141414"));
        private static readonly IPen blackKeyPen = new Pen(new SolidColorBrush(Color.Parse("#0A0A0A")), 1);
        private static readonly IBrush activeWhiteKeyBrush = new SolidColorBrush(Color.Parse("#8CC63F"));
        private static readonly IBrush activeBlackKeyBrush = new SolidColorBrush(Color.Parse("#6FA32F"));
        private static readonly IBrush labelBrush = new SolidColorBrush(Color.Parse("#8A8A8A"));

        private const double BlackKeyWidthRatio = 0.62;
        private const double BlackKeyHeightRatio = 0.62;

        private int activeTone = -1;
        private int whiteKeyCount;
        private double whiteKeyWidth;

        static HorizontalKeyboard() {
            AffectsRender<HorizontalKeyboard>(LowToneProperty, HighToneProperty);
        }

        public HorizontalKeyboard() {
            ClipToBounds = true;
        }

        private int Low => Math.Min(LowTone, HighTone);
        private int High => Math.Max(LowTone, HighTone);

        private void UpdateMetrics() {
            whiteKeyCount = 0;
            for (int tone = Low; tone <= High; tone++) {
                if (!MusicMath.IsBlackKey(tone)) {
                    whiteKeyCount++;
                }
            }
            whiteKeyWidth = whiteKeyCount > 0 ? Bounds.Width / whiteKeyCount : 0;
        }

        public override void Render(DrawingContext context) {
            UpdateMetrics();
            if (whiteKeyCount == 0 || whiteKeyWidth <= 0 || Bounds.Height <= 0) {
                return;
            }
            double height = Bounds.Height;
            double blackKeyWidth = whiteKeyWidth * BlackKeyWidthRatio;
            double blackKeyHeight = height * BlackKeyHeightRatio;

            int whiteIndex = 0;
            for (int tone = Low; tone <= High; tone++) {
                if (MusicMath.IsBlackKey(tone)) {
                    continue;
                }
                var rect = new Rect(whiteIndex * whiteKeyWidth, 0, whiteKeyWidth, height);
                var brush = tone == activeTone ? activeWhiteKeyBrush
                    : MusicMath.IsCenterKey(tone) ? centerKeyBrush
                    : whiteKeyBrush;
                context.DrawRectangle(brush, whiteKeyPen, rect);
                if (MusicMath.IsCenterKey(tone) && whiteKeyWidth >= 16) {
                    var layout = TextLayoutCache.Get(MusicMath.GetToneName(tone), labelBrush, 10);
                    var position = new Point(
                        rect.X + (whiteKeyWidth - layout.Width) / 2,
                        height - layout.Height - 4);
                    layout.Draw(context, position);
                }
                whiteIndex++;
            }

            whiteIndex = 0;
            for (int tone = Low; tone <= High; tone++) {
                if (!MusicMath.IsBlackKey(tone)) {
                    whiteIndex++;
                    continue;
                }
                double left = whiteIndex * whiteKeyWidth - blackKeyWidth / 2;
                if (left < 0 || left + blackKeyWidth > Bounds.Width) {
                    continue;
                }
                context.DrawRectangle(
                    tone == activeTone ? activeBlackKeyBrush : blackKeyBrush,
                    blackKeyPen,
                    new Rect(left, 0, blackKeyWidth, blackKeyHeight));
            }
        }

        private int PointToTone(Point point) {
            UpdateMetrics();
            if (whiteKeyCount == 0 || whiteKeyWidth <= 0) {
                return -1;
            }
            double blackKeyWidth = whiteKeyWidth * BlackKeyWidthRatio;
            double blackKeyHeight = Bounds.Height * BlackKeyHeightRatio;
            // Black keys are drawn on top of white keys, so test them first.
            if (point.Y <= blackKeyHeight) {
                int index = 0;
                for (int tone = Low; tone <= High; tone++) {
                    if (!MusicMath.IsBlackKey(tone)) {
                        index++;
                        continue;
                    }
                    double left = index * whiteKeyWidth - blackKeyWidth / 2;
                    if (point.X >= left && point.X < left + blackKeyWidth) {
                        return tone;
                    }
                }
            }
            int whiteIndex = Math.Clamp((int)(point.X / whiteKeyWidth), 0, whiteKeyCount - 1);
            for (int tone = Low; tone <= High; tone++) {
                if (MusicMath.IsBlackKey(tone)) {
                    continue;
                }
                if (whiteIndex-- == 0) {
                    return tone;
                }
            }
            return High;
        }

        private void BeginTone(int tone) {
            if (tone < 0 || tone == activeTone) {
                return;
            }
            EndActiveTone();
            activeTone = tone;
            PlaybackManager.Inst.PlayTone(MusicMath.ToneToFreq(tone));
            InvalidateVisual();
        }

        private void EndActiveTone() {
            if (activeTone < 0) {
                return;
            }
            PlaybackManager.Inst.EndTone(MusicMath.ToneToFreq(activeTone));
            activeTone = -1;
            InvalidateVisual();
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e) {
            var point = e.GetCurrentPoint(this);
            if (!point.Properties.IsLeftButtonPressed) {
                base.OnPointerPressed(e);
                return;
            }
            e.Pointer.Capture(this);
            BeginTone(PointToTone(point.Position));
            e.Handled = true;
        }

        protected override void OnPointerMoved(PointerEventArgs e) {
            if (activeTone >= 0) {
                BeginTone(PointToTone(e.GetPosition(this)));
                e.Handled = true;
                return;
            }
            base.OnPointerMoved(e);
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e) {
            if (activeTone >= 0) {
                e.Pointer.Capture(null);
                EndActiveTone();
                e.Handled = true;
                return;
            }
            base.OnPointerReleased(e);
        }

        protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e) {
            EndActiveTone();
            base.OnPointerCaptureLost(e);
        }
    }
}
