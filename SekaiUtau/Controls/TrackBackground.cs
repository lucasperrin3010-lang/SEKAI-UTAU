using System;
using System.Linq;
using System.Reactive.Linq;
using Avalonia;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using SekaiUtau.Core;
using SekaiUtau.Core.Util;
using ReactiveUI;
using ReactiveUI.Primitives;

namespace SekaiUtau.App.Controls {
    class TrackBackground : TemplatedControl {
        // Key colours come from Preferences > Appearance (Piano roll keys), with the
        // classic piano look as default.
        static readonly Color DarkKeyName = Color.Parse("#26282C");
        static readonly Color LightKeyName = Color.Parse("#DCDEE2");
        static readonly System.Collections.Generic.Dictionary<Color, IBrush> KeyBrushCache = new();

        static IBrush KeyBrush(Color color) {
            if (!KeyBrushCache.TryGetValue(color, out var brush)) {
                brush = new SolidColorBrush(color);
                KeyBrushCache[color] = brush;
            }
            return brush;
        }

        // Key labels stay readable whatever colour the user picked for the keys.
        static IBrush KeyNameBrush(Color key) {
            double luminance = (0.299 * key.R + 0.587 * key.G + 0.114 * key.B) / 255.0;
            return KeyBrush(luminance > 0.55 ? DarkKeyName : LightKeyName);
        }
        public static readonly DirectProperty<TrackBackground, double> TrackHeightProperty =
            AvaloniaProperty.RegisterDirect<TrackBackground, double>(
                nameof(TrackHeight),
                o => o.TrackHeight,
                (o, v) => o.TrackHeight = v);
        public static readonly DirectProperty<TrackBackground, double> TrackOffsetProperty =
            AvaloniaProperty.RegisterDirect<TrackBackground, double>(
                nameof(TrackOffset),
                o => o.TrackOffset,
                (o, v) => o.TrackOffset = v);
        public static readonly DirectProperty<TrackBackground, bool> IsPianoRollProperty =
            AvaloniaProperty.RegisterDirect<TrackBackground, bool>(
                nameof(IsPianoRoll),
                o => o.IsPianoRoll,
                (o, v) => o.IsPianoRoll = v);
        public static readonly DirectProperty<TrackBackground, bool> IsKeyboardProperty =
            AvaloniaProperty.RegisterDirect<TrackBackground, bool>(
                nameof(IsKeyboard),
                o => o.IsKeyboard,
                (o, v) => o.IsKeyboard = v);
        public static readonly DirectProperty<TrackBackground, bool> ShowKeyNamesProperty =
            AvaloniaProperty.RegisterDirect<TrackBackground, bool>(
                nameof(ShowKeyNames),
                o => o.ShowKeyNames,
                (o, v) => o.ShowKeyNames = v);
        public static readonly DirectProperty<TrackBackground, int> KeyProperty =
            AvaloniaProperty.RegisterDirect<TrackBackground, int>(
                nameof(Key),
                o => o.Key,
                (o, v) => o.Key = v);

        public double TrackHeight {
            get => _trackHeight;
            private set => SetAndRaise(TrackHeightProperty, ref _trackHeight, value);
        }
        public double TrackOffset {
            get => _trackOffset;
            private set => SetAndRaise(TrackOffsetProperty, ref _trackOffset, value);
        }
        public bool IsPianoRoll {
            get => _isPianoRoll;
            set => SetAndRaise(IsPianoRollProperty, ref _isPianoRoll, value);
        }
        public bool IsKeyboard {
            get => _isKeyboard;
            set => SetAndRaise(IsKeyboardProperty, ref _isKeyboard, value);
        }
        /// <summary>
        /// Draws the note names and scale degrees without drawing a keyboard, for the
        /// pitch ruler that is left behind when the keyboard moves to the bottom.
        /// </summary>
        public bool ShowKeyNames {
            get => _showKeyNames;
            set => SetAndRaise(ShowKeyNamesProperty, ref _showKeyNames, value);
        }
        public int Key {
            get => _key;
            set => SetAndRaise(KeyProperty, ref _key, value);
        }

        private double _trackHeight;
        private double _trackOffset;
        private bool _isPianoRoll;
        private bool _isKeyboard;
        private bool _showKeyNames;
        private int _key;

        public TrackBackground() {
            MessageBus.Current.Listen<ThemeChangedEvent>()
                .Subscribe(e => InvalidateVisual());
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change) {
            base.OnPropertyChanged(change);
            if (change.Property == TrackHeightProperty ||
                change.Property == TrackOffsetProperty ||
                change.Property == ForegroundProperty ||
                change.Property == ShowKeyNamesProperty ||
                change.Property == KeyProperty) {
                InvalidateVisual();
            }
        }

        int mod(int a, int b){
            return (a % b + b) % b;
        }

        public override void Render(DrawingContext context) {
            if (TrackHeight == 0) {
                return;
            }
            int track = (int)TrackOffset;
            double top = TrackHeight * (track - TrackOffset);
            string[] degreeNames;
            switch(Preferences.Default.DegreeStyle){
                case 1:
                    degreeNames = MusicMath.Solfeges;
                    break;
                case 2:
                    degreeNames = MusicMath.NumberedNotations;
                    break;
                default:
                    degreeNames = Enumerable.Repeat("", 12).ToArray();
                    break;
            }
            while (top < Bounds.Height) {
                bool isAltTrack = IsAltTrack(track) ^ (ThemeManager.IsDarkMode && !IsKeyboard);
                bool isCenterKey = IsKeyboard && IsCenterKey(track);
                int rowTone = ViewConstants.MaxTone - 1 - track;
                // The piano roll draws a real keyboard on its left edge: white keys across
                // the whole strip and shorter black keys, the way a keyboard looks sideways.
                bool pianoKeys = ShowKeyNames && TrackHeight >= 12;
                bool blackKey = pianoKeys && rowTone >= 0 && MusicMath.IsBlackKey(rowTone);
                Color pianoKeyColor = blackKey ? UiColors.PianoBlackKey
                    : MusicMath.IsCenterKey(rowTone) ? UiColors.PianoCenterKey : UiColors.PianoWhiteKey;
                var brush = isCenterKey ? ThemeManager.CenterKeyBrush
                    : IsKeyboard ? (isAltTrack ? ThemeManager.BlackKeyBrush : ThemeManager.WhiteKeyBrush)
                    : pianoKeys ? KeyBrush(pianoKeyColor)
                    : isAltTrack ? Foreground : Background;
                double keyWidth = blackKey ? Math.Max(0, Bounds.Width * 0.62) : Bounds.Width;
                context.DrawRectangle(
                    brush,
                    null,
                    new Rect(0, (int)top, keyWidth, TrackHeight));
                if ((IsKeyboard || ShowKeyNames) && TrackHeight >= 12) {
                    brush = IsKeyboard || pianoKeys
                        ? isCenterKey ? ThemeManager.CenterKeyNameBrush
                            : KeyNameBrush(pianoKeyColor)
                        : ThemeManager.ForegroundBrush;
                    int tone = ViewConstants.MaxTone - 1 - track;
                    string toneName = MusicMath.GetToneName(tone);
                    var toneTextLayout = TextLayoutCache.Get(toneName, brush, 12);
                    var toneTextPosition = new Point(Bounds.Width - 4 - (int)toneTextLayout.Width, (int)(top + (TrackHeight - toneTextLayout.Height) / 2));
                    using (var state = context.PushTransform(Matrix.CreateTranslation(toneTextPosition))) {
                        toneTextLayout.Draw(context, new Point());
                    }
                    //scale degree display
                    int degree = mod(tone - Key, 12);
                    string degreeName = degreeNames[degree];
                    var degreeTextLayout = TextLayoutCache.Get(degreeName, brush, 12);
                    var degreeTextPosition = new Point(4, (int)(top + (TrackHeight - degreeTextLayout.Height) / 2));
                    using (var state = context.PushTransform(Matrix.CreateTranslation(degreeTextPosition))) {
                        degreeTextLayout.Draw(context, new Point());
                    }
                }
                track++;
                top += TrackHeight;
            }
        }

        private bool IsAltTrack(int track) {
            if (!IsPianoRoll) {
                return track % 2 == 1;
            }
            int tone = ViewConstants.MaxTone - 1 - track;
            if (tone < 0) {
                return false;
            }
            return MusicMath.IsBlackKey(tone);
        }

        private bool IsCenterKey(int track) {
            int tone = ViewConstants.MaxTone - 1 - track;
            return MusicMath.IsCenterKey(tone);
        }
    }
}
