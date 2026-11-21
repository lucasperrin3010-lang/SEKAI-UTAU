using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using DynamicData.Binding;
using SekaiUtau.App.Views;
using SekaiUtau.Core;
using SekaiUtau.Core.Ustx;
using SekaiUtau.Core.Util;
using ReactiveUI;
using ReactiveUI.Primitives;
using ReactiveUI.SourceGenerators;

namespace SekaiUtau.App.ViewModels {
    public class PartsContextMenuArgs {
        public UPart? Part { get; set; }
        public bool IsVoicePart => Part is UVoicePart;
        public bool IsWavePart => Part is UWavePart;
        public ReactiveCommand<UPart, RxVoid>? PartDeleteCommand { get; set; }
        public ReactiveCommand<UPart, RxVoid>? PartRenameCommand { get; set; }
        public ReactiveCommand<UPart, RxVoid>? PartGotoFileCommand { get; set; }
        public ReactiveCommand<UPart, RxVoid>? PartReplaceAudioCommand { get; set; }
        public ReactiveCommand<UPart, RxVoid>? PartTranscribeCommand { get; set; }
        public ReactiveCommand<UPart, RxVoid>? PartMergeCommand { get; set; }
        public ReactiveCommand<UPart, RxVoid>? PartSplitCommand { get; set; }
        public IEnumerable<MenuItemViewModel> PartApplyPitchMenuItems { get; set; } = new List<MenuItemViewModel>();
    }

    public class WarningItem {
        public string Key { get; }
        public string Title { get; }
        public string? Detail { get; }

        public WarningItem(WarningNotification warning) {
            Key = warning.Key;
            Title = string.Format(ThemeManager.GetString(warning.TitleKey), warning.Args);
            Detail = warning.DetailKey == null
                ? null
                : string.Format(ThemeManager.GetString(warning.DetailKey), warning.Args);
        }
    }

    /// <summary>The wallpaper preference changed; the main window should reload it.</summary>
    public class WallpaperChangedEvent { }

            public class RecentFileInfo {
        public string Name { get; }
        public string PathName { get; }
        public string Directory { get; }
        // Two deepest folders only: enough to recognize the project, without the user name.
        public string ShortDirectory { get; }
        public DateTime LastWriteTime { get; }
        public string LastWriteTimeStr { get; }

        public RecentFileInfo(string path) {
            PathName = path;
            Name = Path.GetFileName(path);
            Directory = Path.GetDirectoryName(path) ?? string.Empty;
            ShortDirectory = ShortenDirectory(Directory);
            LastWriteTime = File.GetLastWriteTime(path);
            LastWriteTimeStr = LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss");
        }

        static string ShortenDirectory(string directory) {
            if (string.IsNullOrEmpty(directory)) {
                return string.Empty;
            }
            var folder = Path.GetFileName(directory);
            var parent = Path.GetFileName(Path.GetDirectoryName(directory) ?? string.Empty);
            return string.IsNullOrEmpty(parent) ? folder : Path.Combine(parent, folder);
        }
    }

    public partial class MainWindowViewModel : ViewModelBase, ICmdSubscriber {
        /// <summary>Label of the duo account menu entry.</summary>
        public string AccountHeader => string.IsNullOrEmpty(Core.Collab.Accounts.CurrentName)
            ? ThemeManager.GetString("menu.duo.account.none")
            : string.Format(ThemeManager.GetString("menu.duo.account.signed"), Core.Collab.Accounts.CurrentName);

        public void RefreshAccount() => this.RaisePropertyChanged(nameof(AccountHeader));

        public string Title => !ProjectSaved
            ? $"{AppVersion}"
            : $"{(DocManager.Inst.ChangesSaved ? "" : "*")}{AppVersion} — {ProjectName}";

        // Only the file name, so no personal folder is shown in the window title or the taskbar.
        string ProjectName {
            get {
                var file = DocManager.Inst.Project?.FilePath;
                var name = string.IsNullOrEmpty(file) ? string.Empty : Path.GetFileName(file);
                return string.IsNullOrEmpty(name) ? ThemeManager.GetString("project.untitled") : name;
            }
        }
        public double Width => Preferences.Default.MainWindowSize.Width;
        public double Height => Preferences.Default.MainWindowSize.Height;

        [Reactive] public partial Avalonia.Media.IImage? Wallpaper { get; set; }
        [Reactive] public partial bool HasWallpaper { get; set; }
        // True while the window is being recorded, drives the REC toggle in the transport bar.
        [Reactive] public partial bool IsRecording { get; set; }
        // Elapsed recording time, shown next to the red dot.
        [Reactive] public partial string RecordingTime { get; set; }
        [Reactive] public partial double WallpaperOpacity { get; set; }

        private string loadedWallpaperPath = string.Empty;

        public void LoadWallpaper() {
            // Opacity is cheap to update and must follow the slider live, so set it first.
            WallpaperOpacity = Math.Clamp(Preferences.Default.WallpaperOpacity, 0.05, 1);
            var path = Preferences.Default.WallpaperPath;
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) {
                Wallpaper = null;
                HasWallpaper = false;
                loadedWallpaperPath = string.Empty;
                return;
            }
            if (Wallpaper != null && path == loadedWallpaperPath) {
                // Same file: keep the decoded bitmap, only opacity changed.
                HasWallpaper = true;
                return;
            }
            try {
                using var stream = File.OpenRead(path);
                Wallpaper = new Avalonia.Media.Imaging.Bitmap(stream);
                HasWallpaper = true;
                loadedWallpaperPath = path;
            } catch (Exception e) {
                Wallpaper = null;
                HasWallpaper = false;
                loadedWallpaperPath = string.Empty;
                Serilog.Log.Error(e, "Failed to load wallpaper");
            }
        }

        /// <summary>
        ///0: welcome page, 1: tracks page
        /// </summary>
        [Reactive] public partial int Page { get; set; } = 0;
        public ObservableCollectionExtended<RecentFileInfo> RecentFiles { get; } = new ObservableCollectionExtended<RecentFileInfo>();

        // Drives the empty states of the welcome screen.
        public bool HasRecentFiles => RecentFiles.Count > 0;
        public ObservableCollectionExtended<RecentFileInfo> TemplateFiles { get; } = new ObservableCollectionExtended<RecentFileInfo>();

        public bool HasTemplateFiles => TemplateFiles.Count > 0;
        [Reactive] public partial bool HasRecovery { get; set; } = false;
        [Reactive] public partial string RecoveryPath { get; set; } = String.Empty;
        [Reactive] public partial string RecoveryString { get; set; } = String.Empty;

        [Reactive] public partial PlaybackViewModel PlaybackViewModel { get; set; }
        [Reactive] public partial TracksViewModel TracksViewModel { get; set; }
        /// <summary>Track shown in the right-hand voice panel; null when nothing is selected.</summary>
        [Reactive] public partial TrackHeaderViewModel? SelectedTrack { get; set; }
        [Reactive] public partial ReactiveCommand<string, RxVoid>? OpenRecentCommand { get; private set; }
        [Reactive] public partial ReactiveCommand<string, RxVoid>? OpenTemplateCommand { get; private set; }
        public ObservableCollectionExtended<MenuItemViewModel> OpenRecentMenuItems => openRecentMenuItems;
        public ObservableCollectionExtended<MenuItemViewModel> OpenTemplatesMenuItems => openTemplatesMenuItems;
        public ObservableCollectionExtended<MenuItemViewModel> TimelineContextMenuItems { get; }
            = new ObservableCollectionExtended<MenuItemViewModel>();
        public ObservableCollectionExtended<WarningItem> Warnings { get; }
            = new ObservableCollectionExtended<WarningItem>();
        [Reactive] public partial bool HasWarnings { get; set; }

        [Reactive] public partial string ClearCacheHeader { get; set; }
        public bool ProjectSaved => !string.IsNullOrEmpty(DocManager.Inst.Project.FilePath) && DocManager.Inst.Project.Saved;
        public string AppVersion {
            get {
                Version? version = System.Reflection.Assembly.GetEntryAssembly()?.GetName().Version;
                string suffix = Core.Util.ReleaseChannel.FromVersion(version) is { } channel
                    ? $" ({channel})" : string.Empty;
                return $"SEKAI UTAU v{version}{suffix}";
            }
        }
        [Reactive] public partial double Progress { get; set; }
        [Reactive] public partial string ProgressText { get; set; }
        [Reactive] public partial bool ShowPianoRoll { get; set; }
        [Reactive] public partial double PianoRollMaxHeight { get; set; }
        [Reactive] public partial double PianoRollMinHeight { get; set; }
        public ReactiveCommand<UPart, RxVoid> PartDeleteCommand { get; set; }
        public ReactiveCommand<int, RxVoid>? AddTempoChangeCmd { get; set; }
        public ReactiveCommand<int, RxVoid>? DelTempoChangeCmd { get; set; }
        public ReactiveCommand<int, RxVoid>? AddTimeSigChangeCmd { get; set; }
        public ReactiveCommand<int, RxVoid>? DelTimeSigChangeCmd { get; set; }
        [Reactive] public partial bool CanUndo { get; set; } = false;
        [Reactive] public partial bool CanRedo { get; set; } = false;
        [Reactive] public partial string UndoText { get; set; } = ThemeManager.GetString("menu.edit.undo");
        [Reactive] public partial string RedoText { get; set; } = ThemeManager.GetString("menu.edit.redo");

        private ObservableCollectionExtended<MenuItemViewModel> openRecentMenuItems
            = new ObservableCollectionExtended<MenuItemViewModel>();
        private ObservableCollectionExtended<MenuItemViewModel> openTemplatesMenuItems
            = new ObservableCollectionExtended<MenuItemViewModel>();

        // view will set this to the real AskIfSaveAndContinue implementation
        public Func<Task<bool>>? AskIfSaveAndContinue { get; set; }

        public MainWindowViewModel() {
            RecordingTime = "00:00";
            LoadWallpaper();
            MessageBus.Current.Listen<WallpaperChangedEvent>().Subscribe(_ => LoadWallpaper());
            PlaybackViewModel = new PlaybackViewModel();
            TracksViewModel = new TracksViewModel();
            MessageBus.Current.Listen<TrackSelectionEvent>().Subscribe(e => {
                SelectedTrack = e.selectedTracks.Length > 0 ? new TrackHeaderViewModel(e.selectedTracks[0]) : null;
            });
            ClearCacheHeader = string.Empty;
            ProgressText = string.Empty;
            // The arrangement and the piano roll are shown together, like a DAW layout.
            ShowPianoRoll = true;
            RecentFiles.Clear();
            RecentFiles.AddRange(Preferences.Default.RecentFiles
                .Select(file => new RecentFileInfo(file))
                .OrderByDescending(f => f.LastWriteTime));
            TemplateFiles.Clear();
            Directory.CreateDirectory(PathManager.Inst.TemplatesPath);
            TemplateFiles.AddRange(Directory.GetFiles(PathManager.Inst.TemplatesPath, "*.ustx")
                .Select(file => new RecentFileInfo(file)));
            this.RaisePropertyChanged(nameof(HasRecentFiles));
            this.RaisePropertyChanged(nameof(HasTemplateFiles));

            // create async commands that consult the view's save prompt
            OpenRecentCommand = ReactiveCommand.CreateFromTask<string>(async file => {
                if (!DocManager.Inst.ChangesSaved && AskIfSaveAndContinue != null) {
                    if (!await AskIfSaveAndContinue()) return;
                }
                OpenRecent(file);
            });

            OpenTemplateCommand = ReactiveCommand.CreateFromTask<string>(async file => {
                if (!DocManager.Inst.ChangesSaved && AskIfSaveAndContinue != null) {
                    if (!await AskIfSaveAndContinue()) return;
                }
                OpenTemplate(file);
            });

            PartDeleteCommand = ReactiveCommand.Create<UPart>(part => {
                TracksViewModel.DeleteSelectedParts();
            });
            // Warnings raised before the window existed, e.g. by the startup singer search.
            RefreshWarnings();
            DocManager.Inst.AddSubscriber(this);

            this.WhenAnyValue(vm => vm.ShowPianoRoll)
                .Subscribe(x => {
                    PianoRollMaxHeight = x ? double.PositiveInfinity : 0.01;
                    PianoRollMinHeight = x ? ViewConstants.PianoRollMinHeight : 0.01;
                });
        }

        public void Undo() {
            DocManager.Inst.Undo();
        }
        public void Redo() {
            DocManager.Inst.Redo();
        }
        private void SetUndoState() {
            CanUndo = DocManager.Inst.GetUndoState(out string? undoNameKey);
            if (!string.IsNullOrWhiteSpace(undoNameKey)) {
                UndoText = $"{ThemeManager.GetString("menu.edit.undo")}: {ThemeManager.GetString(undoNameKey)}";
            } else {
                UndoText = ThemeManager.GetString("menu.edit.undo");
            }
            CanRedo = DocManager.Inst.GetRedoState(out string? redoNameKey);
            if (!string.IsNullOrWhiteSpace(redoNameKey)) {
                RedoText = $"{ThemeManager.GetString("menu.edit.redo")}:  {ThemeManager.GetString(redoNameKey)}";
            } else {
                RedoText = ThemeManager.GetString("menu.edit.redo");
            }
        }

        public void InitProject(MainWindow window) {
            var recPath = Preferences.Default.RecoveryPath;
            if (!string.IsNullOrWhiteSpace(recPath) && File.Exists(recPath)) {
                /*
                var result = await MessageBox.Show(
                    window,
                    $"{ThemeManager.GetString("dialogs.recovery")}\n{recPath}",
                    ThemeManager.GetString("dialogs.recovery.caption"),
                    MessageBox.MessageBoxButtons.YesNo);
                if (result == MessageBox.MessageBoxResult.Yes) {
                    DocManager.Inst.ExecuteCmd(new LoadingNotification(typeof(MainWindow), true, "project"));
                    try {
                        Core.Format.Formats.RecoveryProject(new string[] { recPath });
                        Page = 1;
                        DocManager.Inst.ExecuteCmd(new VoiceColorRemappingNotification(-1, true));
                        DocManager.Inst.Recovered = true;
                        this.RaisePropertyChanged(nameof(Title));
                    } finally {
                        DocManager.Inst.ExecuteCmd(new LoadingNotification(typeof(MainWindow), false, "project"));
                    }
                    return;
                }
                */
                RecoveryPath = recPath;
                RecoveryString = ThemeManager.GetString("dialogs.recovery");
                HasRecovery = true;
                return;
            }
          
            var args = Environment.GetCommandLineArgs();
            if (args.Length == 2 && File.Exists(args[1])) {
                try {
                    Core.Format.Formats.LoadProject(new string[] { args[1] });
                    Page = 1;
                    DocManager.Inst.ExecuteCmd(new VoiceColorRemappingNotification(-1, true));
                } catch (Exception e) {
                    var customEx = new MessageCustomizableException($"Failed to open file {args[1]}", $"<translate:errors.failed.openfile>: {args[1]}", e);
                    DocManager.Inst.ExecuteCmd(new ErrorMessageNotification(customEx));
                }
                return;
            }
        }

        public void NewProject() {
            var defaultTemplate = Path.Combine(PathManager.Inst.TemplatesPath, "default.ustx");
            if (File.Exists(defaultTemplate)) {
                try {
                    OpenProject(new[] { defaultTemplate });
                    DocManager.Inst.Project.Saved = false;
                    DocManager.Inst.Project.FilePath = string.Empty;
                    this.RaisePropertyChanged(nameof(Title));
                    return;
                } catch (Exception e) {
                    var customEx = new MessageCustomizableException("Failed to load default template", "<translate:errors.failed.load>: default template", e);
                    DocManager.Inst.ExecuteCmd(new ErrorMessageNotification(customEx));
                }
            }
            DocManager.Inst.ExecuteCmd(new LoadProjectNotification(Core.Format.Ustx.Create()));
            DocManager.Inst.Recovered = false;
        }



        public void OpenProject(string[] files) {
            if (files == null) {
                return;
            }
            DocManager.Inst.ExecuteCmd(new LoadingNotification(typeof(MainWindow), true, "project"));
            try {

                Core.Format.Formats.LoadProject(files);
                DocManager.Inst.ExecuteCmd(new VoiceColorRemappingNotification(-1, true));
                this.RaisePropertyChanged(nameof(Title));
            } finally {
                DocManager.Inst.ExecuteCmd(new LoadingNotification(typeof(MainWindow), false, "project"));
            }
            DocManager.Inst.Recovered = false;
        }

        public void OpenRecent(string file) {
            try {
                OpenProject(new string[] { file });
                Page = 1;
            } catch (Exception e) {
                var customEx = new MessageCustomizableException("Failed to open recent", "<translate:errors.failed.openfile>: recent project", e);
                DocManager.Inst.ExecuteCmd(new ErrorMessageNotification(customEx));
            }
        }

        public void OpenTemplate(string file) {
            try {
                OpenProject(new string[] { file });
                Page = 1;
                DocManager.Inst.Project.Saved = false;
                DocManager.Inst.Project.FilePath = string.Empty;
                this.RaisePropertyChanged(nameof(Title));
            } catch (Exception e) {
                var customEx = new MessageCustomizableException("Failed to open template", "<translate:errors.failed.openfile>: project template", e);
                DocManager.Inst.ExecuteCmd(new ErrorMessageNotification(customEx));
            }
        }

        public void SaveProject(string file = "") {
            if (file == null) {
                return;
            }
            DocManager.Inst.ExecuteCmd(new SaveProjectNotification(file));
            this.RaisePropertyChanged(nameof(Title));
        }

        public void ImportTracks(UProject[] loadedProjects, bool importTempo){
            if (loadedProjects == null || loadedProjects.Length < 1) {
                return;
            }
            Core.Format.Formats.ImportTracks(DocManager.Inst.Project, loadedProjects, importTempo);
        }

        public void ImportTracks(string[] files, bool importTempo) {
            if (files == null) {
                return;
            }
            Core.Format.Formats.ImportTracks(DocManager.Inst.Project, files, importTempo);
        }

        public void ImportAudio(string file) {
            if (file == null) {
                return;
            }
            var project = DocManager.Inst.Project;
            UWavePart part = new UWavePart() {
                FilePath = file,
            };
            part.Load(project);
            if (part == null) {
                return;
            }
            int trackNo = project.tracks.Count;
            part.trackNo = trackNo;
            DocManager.Inst.StartUndoGroup("command.import.audio");
            DocManager.Inst.ExecuteCmd(new AddTrackCommand(project, new UTrack(project) { TrackNo = trackNo }));
            DocManager.Inst.ExecuteCmd(new AddPartCommand(project, part));
            DocManager.Inst.EndUndoGroup();
        }

        public void ImportMidi(string file) {
            if (file == null) {
                return;
            }
            var project = DocManager.Inst.Project;
            var parts = Core.Format.MidiWriter.Load(file, project);
            DocManager.Inst.StartUndoGroup("command.import.track");
            foreach (var part in parts) {
                var track = new UTrack(project);
                track.TrackNo = project.tracks.Count;
                part.trackNo = track.TrackNo;
                if(part.name != "New Part" && part.name != "Nouvelle partie"){
                    track.TrackName = part.name;
                }
                part.AfterLoad(project, track);
                DocManager.Inst.ExecuteCmd(new AddTrackCommand(project, track));
                DocManager.Inst.ExecuteCmd(new AddPartCommand(project, part));
            }
            DocManager.Inst.EndUndoGroup();
        }

        public void ClearWarnings(string? key = null) {
            DocManager.Inst.ExecuteCmd(new ClearWarningsNotification(key));
        }

        private void RefreshWarnings() {
            Warnings.Clear();
            Warnings.AddRange(DocManager.Inst.Warnings.Select(w => new WarningItem(w)));
            HasWarnings = Warnings.Count > 0;
        }

        public void RefreshOpenRecent() {
            openRecentMenuItems.Clear();
            openRecentMenuItems.AddRange(Preferences.Default.RecentFiles.Select(file => new MenuItemViewModel() {
                Header = file,
                Command = OpenRecentCommand,
                CommandParameter = file,
            }));
        }

        public void RefreshTemplates() {
            Directory.CreateDirectory(PathManager.Inst.TemplatesPath);
            var templates = Directory.GetFiles(PathManager.Inst.TemplatesPath, "*.ustx");
            openTemplatesMenuItems.Clear();
            openTemplatesMenuItems.AddRange(templates.Select(file => new MenuItemViewModel() {
                Header = Path.GetRelativePath(PathManager.Inst.TemplatesPath, file),
                Command = OpenTemplateCommand,
                CommandParameter = file,
            }));
        }

        public void RefreshCacheSize() {
            string header = ThemeManager.GetString("menu.tools.clearcache") ?? "";
            ClearCacheHeader = header;
            Task.Run(async () => {
                var cacheSize = PathManager.Inst.GetCacheSize();
                await Dispatcher.UIThread.InvokeAsync(() => {
                    ClearCacheHeader = $"{header} ({cacheSize})";
                });
            });
        }

        public void RefreshTimelineContextMenu(int tick) {
            TimelineContextMenuItems.Clear();
            var project = TracksViewModel.Project;
            var timeAxis = project.timeAxis;
            timeAxis.TickPosToBarBeat(tick, out int bar, out int beat, out int _);
            var timeSig = timeAxis.TimeSignatureAtBar(bar);
            if (bar == 0) {
                // Do nothing
            } else if (timeSig.barPosition != bar) {
                TimelineContextMenuItems.Add(new MenuItemViewModel {
                    Header = ThemeManager.GetString("context.timeline.addtimesig"),
                    Command = AddTimeSigChangeCmd,
                    CommandParameter = bar,
                });
            } else {
                TimelineContextMenuItems.Add(new MenuItemViewModel {
                    Header = ThemeManager.GetString("context.timeline.deltimesig"),
                    Command = DelTimeSigChangeCmd,
                    CommandParameter = bar,
                });
            }
            var tempo = project.tempos.LastOrDefault(t => t.position < tick);
            if (tempo != null && tempo.position > 0 && (tick - tempo.position) * TracksViewModel.TickWidth < 40) {
                string template = ThemeManager.GetString("context.timeline.deltempo");
                TimelineContextMenuItems.Add(new MenuItemViewModel {
                    Header = string.Format(template, tempo.position),
                    Command = DelTempoChangeCmd,
                    CommandParameter = tempo.position,
                });
            }
            TracksViewModel.TickToLineTick(tick, out int left, out int right);
            if (tempo == null || tempo.position != left) {
                string template = ThemeManager.GetString("context.timeline.addtempo");
                TimelineContextMenuItems.Add(new MenuItemViewModel {
                    Header = string.Format(template, left),
                    Command = AddTempoChangeCmd,
                    CommandParameter = left,
                });
            }
        }

        /// <summary>
        /// Remap a tick position from the old time axis to the new time axis without changing its absolute position (in ms).
        /// Note that this can only be used on positions, not durations.
        /// </summary>
        private int RemapTickPos(int tickPos, TimeAxis oldTimeAxis, TimeAxis newTimeAxis){
            double msPos = oldTimeAxis.TickPosToMsPos(tickPos);
            return newTimeAxis.MsPosToTickPos(msPos);
        }

        /// <summary>
        /// Remap the starting and ending positions of all the notes and parts in the whole project 
        /// from the old time axis to the new time axis, without changing their absolute positions in ms.
        /// </summary>
        public void RemapTimeAxis(TimeAxis oldTimeAxis, TimeAxis newTimeAxis){
            var project = DocManager.Inst.Project;
            foreach(var part in project.parts){
                var partOldStartTick = part.position;
                var partNewStartTick = RemapTickPos(part.position, oldTimeAxis, newTimeAxis);
                if(partNewStartTick != partOldStartTick){
                    DocManager.Inst.ExecuteCmd(new MovePartCommand(
                        project, part, partNewStartTick, part.trackNo));
                }
                if(part is UVoicePart voicePart){
                    var partOldDuration = voicePart.Duration;
                    var partNewDuration = RemapTickPos(partOldStartTick + voicePart.duration, oldTimeAxis, newTimeAxis) - partNewStartTick;
                    if(partNewDuration != partOldDuration) {
                        DocManager.Inst.ExecuteCmd(new ResizeVoicePartCommand(
                            project, voicePart, partNewDuration - partOldDuration, false));
                    }
                    var noteCommands = new List<UCommand>();
                    foreach(var note in voicePart.notes){
                        var noteOldStartTick = note.position + partOldStartTick;
                        var noteOldEndTick = note.End + partOldStartTick;
                        var noteOldDuration = note.duration;
                        var noteNewStartTick = RemapTickPos(noteOldStartTick, oldTimeAxis, newTimeAxis);
                        var noteNewEndTick = RemapTickPos(noteOldEndTick, oldTimeAxis, newTimeAxis);
                        var deltaPosTickInPart = (noteNewStartTick - partNewStartTick) - (noteOldStartTick - partOldStartTick);
                        if(deltaPosTickInPart != 0){
                            noteCommands.Add(new MoveNoteCommand(voicePart, note, deltaPosTickInPart, 0));
                        }
                        var noteNewDuration = noteNewEndTick - noteNewStartTick;
                        var deltaDur = noteNewDuration - noteOldDuration;
                        if(deltaDur != 0){
                            noteCommands.Add(new ResizeNoteCommand(voicePart, note, deltaDur));
                        }
                        //TODO: expression curve remapping, phoneme timing remapping
                    }
                    foreach(var command in noteCommands){
                        DocManager.Inst.ExecuteCmd(command);
                    }
                }
            }
        }

        #region ICmdSubscriber

        public void OnNext(UCommand cmd, bool isUndo) {
            if (cmd is ProgressBarNotification progressBarNotification) {
                Dispatcher.UIThread.InvokeAsync(() => {
                    Progress = progressBarNotification.Progress;
                    ProgressText = progressBarNotification.Info;
                }, DispatcherPriority.Background);
            } else if (cmd is WarningNotification || cmd is ClearWarningsNotification) {
                RefreshWarnings();
            } else if (cmd is LoadProjectNotification loadProject) {
                Preferences.AddRecentFileIfEnabled(loadProject.project.FilePath);
            } else if (cmd is SaveProjectNotification saveProject) {
                Preferences.AddRecentFileIfEnabled(saveProject.Path);
            } else if (cmd is PackagesChangedNotification) {
                App.ReloadPackages();
            }
            SetUndoState();
            this.RaisePropertyChanged(nameof(Title));
        }

        #endregion
    }
}
