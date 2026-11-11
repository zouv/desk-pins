using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Documents;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using DeskPins.Core;

namespace DeskPins.App
{
    public sealed class NoteWindow : Window
    {
        public readonly Note Note;
        public readonly AppHost Host;
        public TextEditor TextEditor { get; private set; }
        public ImageEditor ImageEditor { get; private set; }
        private readonly Border frame, title;
        private readonly TextBlock caption, indicator;
        private readonly Button closeButton;
        private readonly Grid content;
        private readonly StackPanel skinButtons;
        private string activeSkin = "";
        private readonly DispatcherTimer saveTimer;
        private DispatcherTimer alarmTimer;
        private bool internalChange, closing, movingGroup, alarming, dirty, resizing, showOpacity, directionChord;
        private double oldLeft, oldTop;
        private string lastFind = "";
        private int alarmStep;
        public NoteWindow(AppHost host, Note note)
        {
            Host = host; Note = note; Title = "DeskPins · " + note.DisplayTitle; ShowInTaskbar = false; WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; MinWidth = 100; MinHeight = 24;
            UseLayoutRounding = true; SnapsToDevicePixels = true; Background = Ui.Brush(note.Color);
            var root = new Grid(); root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(22) }); root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            frame = new Border { BorderThickness = new Thickness(1), BorderBrush = Brushes.DimGray, Child = root }; Content = frame;
            var strip = new Grid(); strip.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(9) }); strip.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); strip.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); strip.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) });
            indicator = new TextBlock(); strip.Children.Add(indicator); caption = new TextBlock { FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis, Foreground = Brushes.White }; Grid.SetColumn(caption, 1); strip.Children.Add(caption);
            var close = new Button { Content = "×", FontSize = 15, Padding = new Thickness(0), Background = Brushes.Transparent, Foreground = Brushes.White, BorderThickness = new Thickness(0), ToolTip = Ui.Text("关闭便签（可恢复）") }; closeButton = close; skinButtons = new StackPanel { Orientation = Orientation.Horizontal }; Grid.SetColumn(skinButtons, 2); strip.Children.Add(skinButtons); Grid.SetColumn(close, 3); strip.Children.Add(close); close.Click += (s, e) => { Flush(); Host.CloseNotes(Note); };
            title = new Border { Child = strip }; root.Children.Add(title);
            title.MouseLeftButtonDown += DragTitle;
            title.MouseMove += (s, e) => { if (e.LeftButton == MouseButtonState.Pressed && (Keyboard.Modifiers & ModifierKeys.Control) != 0) { Flush(); var copy = Host.Library.Copy(Note); Host.Focus(copy); try { Host.Windows[copy.Id].DragMove(); } catch (InvalidOperationException) { } } }; title.MouseRightButtonUp += (s, e) => { if (alarming) { StopAlarm(true); e.Handled = true; return; } var menu = Menu(); menu.PlacementTarget = title; menu.IsOpen = true; e.Handled = true; };
            content = new Grid(); Grid.SetRow(content, 1); root.Children.Add(content);
            saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) }; saveTimer.Tick += (s, e) => Flush();
            CreateEditor();
            SourceInitialized += (s, e) => { Native.ToolWindow(this); Native.SetGhost(this, Note.Ghost); };
            Activated += (s, e) => { ApplyAppearance(); if (Host.Library.Catalog.Settings.ClickMakesVisible) Host.ShowAllNotes(); Host.BringStacksForward(); }; Deactivated += (s, e) => { Flush(); showOpacity = false; ApplyAppearance(); };
            LocationChanged += Location; SizeChanged += (s, e) => { if (internalChange || Note.Rolled) return; Note.Width = Width; Note.Height = Height; Note.ExpandedHeight = Height; QueueSave(); };
            PreviewKeyDown += HandleKeyDown; PreviewKeyUp += HandleKeyUp;
            PreviewMouseLeftButtonDown += (s, e) => { if (alarming) StopAlarm(false); };
            PreviewMouseWheel += (s, e) => { if ((Keyboard.Modifiers & ModifierKeys.Control) == 0) return; if (TextEditor != null) TextEditor.Scale(e.Delta > 0 ? 1.1 : .9); else { ImageEditor.Scale = Math.Max(.1, Math.Min(4, ImageEditor.Scale * (e.Delta > 0 ? 1.1 : .9))); ImageEditor.InvalidateMeasure(); } QueueSave(); e.Handled = true; };
            Closing += ClosingWindow; Closed += (s, e) => { saveTimer.Stop(); if (alarmTimer != null) alarmTimer.Stop(); };
            AllowDrop = true; Drop += DropFiles; ApplyModel();
            WindowResize.Attach(this, 100, 40, () => !Note.Rolled,
                () => { resizing = true; if (alarming) StopAlarm(false); Flush(); },
                () => { resizing = false; oldLeft = Left; oldTop = Top; Note.Left = Left; Note.Top = Top; Note.Width = Width; if (!Note.Rolled) Note.Height = Note.ExpandedHeight = Height; QueueSave(); Flush(); },
                () => !(Host.Library.Catalog.Settings.LockPreventsMove && Note.Locked));
        }
        private void CreateEditor()
        {
            internalChange = true; content.Children.Clear(); Host.Library.Load(Note);
            if (Note.Kind == NoteKind.Text)
            {
                TextEditor = new TextEditor(); ImageEditor = null; TextEditor.Read(Note); TextEditor.LinksClickable = Host.Library.Catalog.Settings.LinksClickable; TextEditor.CheckboxTrailingSpace = Host.Library.Catalog.Settings.CheckboxTrailingSpace; TextEditor.TextChanged += (s, e) => { if (!internalChange) { QueueSave(); Dispatcher.BeginInvoke(new Action(AutoSize), DispatcherPriority.Background); } };
                TextEditor.PreviewMouseRightButtonUp += (s, e) => { if (alarming) { StopAlarm(true); e.Handled = true; return; } var menu = TextMenu(); menu.PlacementTarget = TextEditor; menu.IsOpen = true; e.Handled = true; }; TextEditor.PreviewMouseLeftButtonDown += (s, e) => { Host.Select(null, false); };
                content.Children.Add(TextEditor);
            }
            else
            {
                TextEditor = null; ImageEditor = new ImageEditor { Locked = Note.Locked, BackgroundColor = ((SolidColorBrush)Ui.Brush(Note.Color)).Color, MaxUndo = Host.Library.Catalog.Settings.ImageUndoLevels, SaveFormat = Host.Library.Catalog.Settings.ImageSaveType }; ImageEditor.Read(Note.Body); ImageEditor.Changed += () => QueueSave(); ImageEditor.PreviewMouseRightButtonUp += (s, e) => { var menu = ImageMenu(); menu.PlacementTarget = ImageEditor; menu.IsOpen = true; e.Handled = true; }; var scroll = new ScrollViewer { Content = ImageEditor, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }; content.Children.Add(scroll);
            }
            internalChange = false;
        }
        public void ReloadContent() { CreateEditor(); ApplyModel(); }
        public void ApplyModel()
        {
            internalChange = true; var requested = new Rect(Note.Left, Note.Top, Note.Width, Note.Rolled ? 24 : Math.Max(40, Note.Height)); var bounds = Host.Library.Catalog.Settings.KeepOnScreen ? Native.Clamp(requested) : new Rect(requested.Left, requested.Top, Math.Max(100, requested.Width), Math.Max(24, requested.Height)); Left = bounds.Left; Top = bounds.Top; Width = bounds.Width; Height = Note.Rolled ? 24 : bounds.Height; oldLeft = Left; oldTop = Top; content.Visibility = Note.Rolled ? Visibility.Collapsed : Visibility.Visible; Topmost = Note.Topmost; ApplyAppearance(); internalChange = false;
        }
        public void RefreshAppearance() { ApplyAppearance(); }
        private void ApplyAppearance()
        {
            Title = "DeskPins · " + Note.DisplayTitle; Topmost = Note.Topmost;
            Background = content.Background = Ui.Brush(Note.Color); title.Background = IsActive ? Ui.Brush("#527DA5") : Ui.Brush("#808080"); indicator.Background = Note.Topmost ? Ui.Brush("#193C61") : title.Background;
            frame.Effect = Ui.NoteShadow(Host.Library.Catalog.Settings.ShowShadow);
            caption.Text = (Note.Attachment != null ? "📎 " : "") + (Note.AlarmUtc.HasValue ? "◷ " : "") + Note.DisplayTitle; caption.ToolTip = Ui.Text("创建：") + Note.CreatedUtc.ToLocalTime() + "\n" + Ui.Text("修改：") + Note.ModifiedUtc.ToLocalTime() + (Note.AlarmUtc.HasValue ? "\n" + Ui.Text("闹钟：") + Note.AlarmUtc.Value.ToLocalTime() : "");
            closeButton.ToolTip = Ui.Text("关闭便签（可恢复）");
            if (TextEditor != null) { TextEditor.IsReadOnly = Note.Locked; TextEditor.Background = Ui.Brush(Note.Color); TextEditor.VerticalScrollBarVisibility = Note.Scrollbar ? ScrollBarVisibility.Auto : ScrollBarVisibility.Hidden; TextEditor.LinksClickable = Host.Library.Catalog.Settings.LinksClickable; TextEditor.CheckboxTrailingSpace = Host.Library.Catalog.Settings.CheckboxTrailingSpace; }
            if (ImageEditor != null) { ImageEditor.Locked = Note.Locked; ImageEditor.BackgroundColor = ((SolidColorBrush)Ui.Brush(Note.Color)).Color; ImageEditor.MaxUndo = Host.Library.Catalog.Settings.ImageUndoLevels; ImageEditor.SaveFormat = Host.Library.Catalog.Settings.ImageSaveType; }
            Opacity = Host.Library.Catalog.Settings.OpaqueEditing && IsActive && !showOpacity ? 1 : Math.Max(.1, Math.Min(1, Note.Opacity));
            content.Opacity = Note.Secret && !IsActive ? 0 : 1; frame.BorderBrush = Host.Selected.Contains(Note.Id) ? Brushes.DodgerBlue : Brushes.DimGray; Native.SetGhost(this, Note.Ghost);
            var path = string.IsNullOrWhiteSpace(Note.SkinPath) ? Host.Library.Catalog.Settings.SkinPath : Note.SkinPath; var skin = Host.GetSkin(path); if (activeSkin != path) { activeSkin = path; skinButtons.Children.Clear(); if (skin?.Buttons != null) foreach (var item in skin.Buttons) { var action = item.Action; var button = new Button { Content = item.Text, FontSize = 10, Background = Brushes.Transparent, BorderThickness = new Thickness(0), Foreground = Brushes.White, Padding = new Thickness(4, 0, 4, 0), ToolTip = action }; button.Click += (s, e) => Run(action); skinButtons.Children.Add(button); } }
            if (skin != null) { title.Background = Ui.Brush(skin.TitleColor); caption.Foreground = Ui.Brush(skin.TitleTextColor); frame.BorderBrush = Ui.Brush(skin.BorderColor); }
        }
        public void QueueSave() { if (internalChange || closing) return; dirty = true; saveTimer.Stop(); saveTimer.Start(); }
        public void Flush()
        {
            saveTimer.Stop(); if (internalChange || closing || !dirty || !Host.Library.Notes.ContainsKey(Note.Id)) return;
            if (TextEditor != null) TextEditor.Write(Note); else if (ImageEditor != null) Note.Body = ImageEditor.Bytes();
            if (!alarming) Note.Left = Left; Note.Top = Top; Note.Width = Width; if (!Note.Rolled) Note.Height = Note.ExpandedHeight = Height;
            Host.Library.Save(Note); dirty = false;
        }
        public void CloseInternal(bool save = true) { if (save) Flush(); closing = true; Close(); }
        public void ToggleRoll()
        {
            Flush(); var settings = Host.Library.Catalog.Settings;
            if (!Note.Rolled)
            {
                if (settings.RolledDifferentPosition)
                {
                    Note.ExpandedLeft = Note.Left; Note.ExpandedTop = Note.Top;
                    if (Note.RolledLeft.HasValue && Note.RolledTop.HasValue) { Note.Left = Note.RolledLeft.Value; Note.Top = Note.RolledTop.Value; }
                }
                Note.Rolled = true;
            }
            else
            {
                if (settings.RolledDifferentPosition)
                {
                    Note.RolledLeft = Note.Left; Note.RolledTop = Note.Top;
                    if (Note.ExpandedLeft.HasValue && Note.ExpandedTop.HasValue) { Note.Left = Note.ExpandedLeft.Value; Note.Top = Note.ExpandedTop.Value; }
                }
                Note.Rolled = false; Note.Height = Note.ExpandedHeight;
            }
            Host.Library.Save(Note); ApplyModel();
        }
        public void Toggle(string property)
        {
            switch (property) { case "top": Note.Topmost = !Note.Topmost; break; case "lock": Note.Locked = !Note.Locked; break; case "ghost": Note.Ghost = !Note.Ghost; break; case "secret": Note.Secret = !Note.Secret; break; case "scroll": Note.Scrollbar = !Note.Scrollbar; break; case "stand": Note.Stand = !Note.Stand; break; }
            Host.Library.Save(Note); ApplyAppearance();
        }
        public void StartAlarm()
        {
            if (Note.Rolled) { Note.Rolled = false; ApplyModel(); }
            Show(); Activate(); alarming = true; alarmStep = 0; Host.PlayAlarm(); if (alarmTimer == null) { alarmTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) }; alarmTimer.Tick += (s, e) => { alarmStep++; if (Host.Library.Catalog.Settings.AlarmFlash) frame.BorderBrush = alarmStep % 2 == 0 ? Brushes.Red : Brushes.Yellow; if (Host.Library.Catalog.Settings.AlarmJiggle) { internalChange = true; Left = Note.Left + (alarmStep % 2 == 0 ? 3 : -3); internalChange = false; } }; }
            alarmTimer.Start();
        }
        private void StopAlarm(bool snooze) { alarming = false; if (alarmTimer != null) alarmTimer.Stop(); internalChange = true; Left = Note.Left; oldLeft = Left; internalChange = false; Host.StopSound(); ApplyAppearance(); if (snooze) { Note.AlarmUtc = DateTime.UtcNow.AddMinutes(Host.Library.Catalog.Settings.SnoozeMinutes); Host.Library.Save(Note); } }
        private void DragTitle(object sender, MouseButtonEventArgs e)
        {
            if (alarming) { StopAlarm(false); return; }
            if (e.ClickCount == 2)
            {
                var action = Host.Library.Catalog.Settings.DoubleClickAction;
                if (string.IsNullOrWhiteSpace(action)) action = Host.Library.Catalog.Settings.DoubleClickRoll ? "roll" : "none";
                if (action == "roll") ToggleRoll(); else if (action == "manage") Host.Manage();
                e.Handled = true; return;
            }
            bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0, shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
            if (ctrl) { if (Host.Selected.Contains(Note.Id)) Host.Selected.Remove(Note.Id); else Host.Selected.Add(Note.Id); foreach (var w in Host.Windows.Values) w.RefreshAppearance(); Host.BringStacksForward(); return; }
            if (Host.Library.Catalog.Settings.LockPreventsMove && Note.Locked) return;
            Host.Select(Note, shift); double beforeLeft = Left, beforeTop = Top; try { DragMove(); } catch (InvalidOperationException) { }
            if (Math.Abs(Left - beforeLeft) > .1 || Math.Abs(Top - beforeTop) > .1) Host.SnapAfterDrag(this, (Keyboard.Modifiers & ModifierKeys.Alt) != 0);
            Flush(); Host.SaveSelected();
        }
        private void Location(object sender, EventArgs e)
        {
            if (internalChange || movingGroup) return;
            if (resizing) { oldLeft = Left; oldTop = Top; Note.Left = Left; Note.Top = Top; return; }
            double dx = Left - oldLeft, dy = Top - oldTop; oldLeft = Left; oldTop = Top;
            Host.MoveSelected(this, dx, dy); Note.Left = Left; Note.Top = Top; QueueSave();
        }
        public void MoveBy(double dx, double dy) { movingGroup = true; Left += dx; Top += dy; oldLeft = Left; oldTop = Top; Note.Left = Left; Note.Top = Top; movingGroup = false; QueueSave(); }
        private void AutoSize()
        {
            if (Note.Scrollbar || Note.Rolled || TextEditor == null || !IsLoaded) return; var rect = TextEditor.Document.ContentEnd.GetCharacterRect(LogicalDirection.Backward); if (rect.IsEmpty) return; double height = Math.Max(60, Math.Min(SystemParameters.WorkArea.Height - 40, rect.Bottom + 34)); if (Math.Abs(height - Height) < 2) return; double bottom = Top + Height; internalChange = true; Height = height; if (Note.Stand) Top = bottom - height; internalChange = false; Note.Height = Note.ExpandedHeight = height; QueueSave();
        }
        private void ClosingWindow(object sender, CancelEventArgs e) { if (closing || Host.Exiting) return; e.Cancel = true; Flush(); Host.CloseNotes(Note); }
        private void DropFiles(object sender, DragEventArgs e)
        {
            if (Note.Locked) return; if (e.Data.GetDataPresent(DataFormats.FileDrop)) { var files = (string[])e.Data.GetData(DataFormats.FileDrop); if (files.Length == 1 && Host.IsImage(files[0]) && (Note.Kind == NoteKind.Image || string.IsNullOrWhiteSpace(TextEditor.PlainText))) { Host.LoadImage(Note, files[0]); ReloadContent(); } else if (TextEditor != null) foreach (var file in files) { TextEditor.InsertText(((Keyboard.Modifiers & ModifierKeys.Shift) != 0 ? file : new Uri(file).AbsoluteUri) + "\n"); } e.Handled = true; }
        }
        private void HandleKeyDown(object sender, KeyEventArgs e)
        {
            Key key = e.Key == Key.System ? e.SystemKey : e.Key; var mods = Keyboard.Modifiers;
            if (Host.Library.Catalog.Settings.ShiftCtrlDirection && TextEditor != null)
            {
                if (key == Key.LeftShift || key == Key.RightShift || key == Key.LeftCtrl || key == Key.RightCtrl)
                {
                    if ((Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift)) == (ModifierKeys.Control | ModifierKeys.Shift)) directionChord = true;
                }
                else directionChord = false;
            }
            if (mods == ModifierKeys.Alt && key == Key.F4) { Host.Exit(); e.Handled = true; return; }
            if (key == Key.Escape) { ImageEditor?.CancelEdit(); Host.Select(null, false); if (alarming) StopAlarm(false); e.Handled = true; return; }
            if (mods == ModifierKeys.Control && key == Key.F4) { Run("close"); e.Handled = true; return; }
            if (key == Key.Apps) { var menu = mods == ModifierKeys.Shift ? (TextEditor != null ? TextMenu() : ImageMenu()) : Menu(); menu.PlacementTarget = this; menu.IsOpen = true; e.Handled = true; return; }
            if (TextEditor != null && mods == ModifierKeys.Shift && key == Key.Delete) { TextEditor.DeleteLine(); e.Handled = true; return; }
            if (mods == ModifierKeys.Control && (key == Key.OemOpenBrackets || key == Key.OemCloseBrackets || key == Key.OemMinus || key == Key.OemPlus)) { bool larger = key == Key.OemCloseBrackets || key == Key.OemPlus; if (TextEditor != null) TextEditor.Scale(larger ? 1.1 : .9); else { ImageEditor.Scale = Math.Max(.1, Math.Min(4, ImageEditor.Scale * (larger ? 1.1 : .9))); ImageEditor.InvalidateMeasure(); } QueueSave(); e.Handled = true; return; }
            string action = Host.ActionFor(key, mods);
            if (action != null) { Run(action); e.Handled = true; return; }
            if (mods == ModifierKeys.Alt && key >= Key.D0 && key <= Key.D9) { ApplyStyle((int)key - (int)Key.D0); e.Handled = true; return; }
            if ((mods & ModifierKeys.Alt) != 0 && (key == Key.Left || key == Key.Right || key == Key.Up || key == Key.Down)) { double dx = key == Key.Left ? -5 : key == Key.Right ? 5 : 0, dy = key == Key.Up ? -5 : key == Key.Down ? 5 : 0; if ((mods & ModifierKeys.Control) != 0) { if (dx != 0) Width = Math.Max(100, Width + dx); if (dy != 0 && !Note.Rolled) Height = Math.Max(40, Height + dy); } else MoveBy(dx, dy); QueueSave(); e.Handled = true; return; }
            if (ImageEditor != null && mods == ModifierKeys.Shift && key == Key.E) { if (Clipboard.ContainsImage()) ImageEditor.PasteInto(Clipboard.GetImage()); e.Handled = true; return; }
            if (ImageEditor != null && mods == ModifierKeys.Shift && key == Key.S) { Run("image-size"); e.Handled = true; return; }
            if (ImageEditor != null && mods == ModifierKeys.Shift && key == Key.Delete) { ImageEditor.FillSelection(true); e.Handled = true; return; }
            if (ImageEditor != null && mods == ModifierKeys.None) { var tools = new[] { Key.S, Key.P, Key.T, Key.N, Key.E, Key.L, Key.A, Key.R, Key.F, Key.C, Key.D }; int index = Array.IndexOf(tools, key); if (index >= 0) { ImageEditor.Tool = (ImageTool)index; e.Handled = true; } else if (key == Key.Enter) { ImageEditor.Crop(); e.Handled = true; } else if (key == Key.Delete) { ImageEditor.FillSelection(false); e.Handled = true; } else if (key == Key.B) { ImageEditor.AddBorder(); e.Handled = true; } else if (key == Key.G || key == Key.Z || key == Key.J || key == Key.M || key == Key.U) { ImageEditor.Transform(key == Key.G ? "gray" : key == Key.Z ? "blur" : key == Key.J ? "jitter" : key == Key.M ? "mirror" : "flip"); e.Handled = true; } else if (key == Key.Left || key == Key.Right) { ImageEditor.Transform(key == Key.Left ? "left" : "right"); e.Handled = true; } }
            if ((mods == ModifierKeys.Control || mods == (ModifierKeys.Control | ModifierKeys.Shift)) && key >= Key.D0 && key <= Key.D9) { var f = Host.Library.Catalog.Friends.FirstOrDefault(x => x.Favorite == (key == Key.D0 ? 10 : (int)key - (int)Key.D0)); if (f != null) { Flush(); var target = Note; if ((mods & ModifierKeys.Shift) != 0) { target = Note.Clone(); target.Secret = true; } Host.Send(target, f); e.Handled = true; } }
        }
        private void HandleKeyUp(object sender, KeyEventArgs e)
        {
            if (!Host.Library.Catalog.Settings.ShiftCtrlDirection || TextEditor == null || !directionChord) return;
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (key == Key.LeftShift || key == Key.RightShift || key == Key.LeftCtrl || key == Key.RightCtrl)
            {
                directionChord = false; TextEditor.ToggleDirection(); e.Handled = true;
            }
        }
        public void Run(string action)
        {
            try
            {
                switch (action)
                {
                    case "new": Host.New(); break;
                    case "new-color": var created = Host.New(); created.Color = Note.Color; Host.Library.Save(created); break;
                    case "close": Flush(); Host.CloseNotes(Note); break;
                    case "delete": Flush(); Host.DeleteNotes(Note); break;
                    case "top": Toggle("top"); break;
                    case "lock": Toggle("lock"); break;
                    case "roll": ToggleRoll(); break;
                    case "title": var name = Ui.Ask("设置标题", "便签标题", Note.Title); if (name != null) { Note.Title = name; Host.Library.Save(Note); ApplyAppearance(); } break;
                    case "manage": Host.Manage(); break;
                    case "settings": Host.Settings(); break;
                    case "friends": Host.Friends(); break;
                    case "sync-friends": Host.SyncFriends(); break;
                    case "edit-friends": Host.EditFriendsSource(); break;
                    case "add-friend": Host.AddFriend(Note); break;
                    case "send-many": Flush(); Host.SendCustom(Note); break;
                    case "reply": Host.Reply(Note, false); break;
                    case "reply-secret": Host.Reply(Note, true); break;
                    case "source": Host.EditSource(Note); break;
                    case "log": Host.OpenLog(); break;
                    case "transfer": Flush(); Host.TransferFile(Note, true); break;
                    case "quote": if (TextEditor != null) TextEditor.Quote(); break;
                    case "format-copy": if (TextEditor != null) TextEditor.CopyFormat(); break;
                    case "format-paste": if (TextEditor != null) TextEditor.PasteFormat(); break;
                    case "skin-reset": Note.SkinPath = ""; Host.Library.Save(Note); ApplyAppearance(); break;
                    case "width-left": double edge = Left + Width; Run("width"); Left = edge - Width; QueueSave(); break;
                    case "alarm": Flush(); new ScheduleWindow(Host, Note, false, false).ShowDialog(); break;
                    case "sleep": Flush(); new ScheduleWindow(Host, Note, true, false).ShowDialog(); break;
                    case "repeat": Flush(); new ScheduleWindow(Host, Note, true, true).ShowDialog(); break;
                    case "tomorrow": Flush(); Host.Library.Sleep(Note, DateTime.Today.AddDays(1).AddHours(Host.Library.Catalog.Settings.TomorrowHour).AddMinutes(Host.Library.Catalog.Settings.TomorrowMinute).AddSeconds(1).ToUniversalTime()); break;
                    case "find": var find = Ui.Ask("查找", "查找文本", lastFind); if (find != null) { lastFind = find; if (TextEditor != null && !TextEditor.Find(lastFind)) MessageBox.Show(Ui.Text("未找到。"), "DeskPins"); } break;
                    case "find-next": if (TextEditor != null) TextEditor.Find(lastFind); break;
                    case "save": Flush(); Host.Export(Note); break;
                    case "save-all": Host.FlushAll(); break;
                    case "print": Host.Print(this); break;
                    case "paste": Host.Paste(this); break;
                    case "copy": if (TextEditor != null) TextEditor.Copy(); else ImageEditor.CopySelection(); break;
                    case "cut": if (TextEditor != null) TextEditor.Cut(); else { ImageEditor.CopySelection(); ImageEditor.FillSelection(false); } break;
                    case "paste-plain": if (TextEditor != null && Clipboard.ContainsText()) TextEditor.InsertText(Clipboard.GetText()); break;
                    case "copy-plain": if (TextEditor != null) { var plain = TextEditor.SelectedPlainText; if (!string.IsNullOrEmpty(plain)) Clipboard.SetText(plain); } break;
                    case "convert-image": if (!Note.Locked && TextEditor != null && string.IsNullOrWhiteSpace(new TextRange(TextEditor.Document.ContentStart, TextEditor.Document.ContentEnd).Text)) { Note.Kind = NoteKind.Image; Note.Body = new byte[0]; CreateEditor(); QueueSave(); ApplyAppearance(); } break;
                    case "cycle": Host.Cycle(Note, 1); break;
                    case "cycle-back": Host.Cycle(Note, -1); break;
                    case "undo": if (ImageEditor != null) ImageEditor.Undo(); else TextEditor.Undo(); break;
                    case "redo": if (ImageEditor != null) ImageEditor.Undo(true); else TextEditor.Redo(); break;
                    case "size": var size = Ui.Ask("便签尺寸", "宽,高", ((int)Width) + "," + ((int)Height)); if (size != null) { var parts = size.Split(','); if (parts.Length == 2) { Width = Math.Max(100, Ui.Number(parts[0], Width)); Height = Math.Max(40, Ui.Number(parts[1], Height)); Note.Scrollbar = true; QueueSave(); ApplyAppearance(); } } break;
                    case "width": Width = Math.Max(100, Math.Min(800, (TextEditor?.PlainText ?? Note.PlainText ?? "").Split('\n').Select(l => l.Length).DefaultIfEmpty(1).Max() * Note.FontSize * .6 + 24)); QueueSave(); break;
                    case "image-size": if (ImageEditor != null && !Note.Locked) { var dimensions = Ui.Ask("图片尺寸", "宽,高", ImageEditor.Image.PixelWidth + "," + ImageEditor.Image.PixelHeight); if (dimensions != null) { var parts = dimensions.Split(','); int width, height; if (parts.Length != 2 || !int.TryParse(parts[0], out width) || !int.TryParse(parts[1], out height)) throw new ArgumentException("请输入两个整数，例如 320,240。"); ImageEditor.ResizeImage(width, height); } } break;
                    case "skin": var file = Ui.OpenFile("便签皮肤|*.json"); if (file != null) { Host.ValidateSkin(file); Note.SkinPath = file; Host.Library.Save(Note); ApplyAppearance(); } break;
                    default: if (TextEditor != null) TextEditor.Format(action); break;
                }
            }
            catch (Exception ex) { Ui.Error(ex); }
        }
        public void ApplyStyle(int index)
        {
            var style = Host.Library.Catalog.Settings.Styles[index]; if (TextEditor != null && !TextEditor.Selection.IsEmpty) { if (Note.Locked) return; TextEditor.Selection.ApplyPropertyValue(TextElement.FontFamilyProperty, new FontFamily(style.FontName)); TextEditor.Selection.ApplyPropertyValue(TextElement.FontSizeProperty, style.FontSize); TextEditor.Selection.ApplyPropertyValue(TextElement.ForegroundProperty, Ui.Brush(style.Foreground)); QueueSave(); }
            else { showOpacity = true; style.Apply(Note); if (TextEditor != null && !Note.Locked) { TextEditor.SelectAll(); TextEditor.Selection.ApplyPropertyValue(TextElement.FontFamilyProperty, new FontFamily(style.FontName)); TextEditor.Selection.ApplyPropertyValue(TextElement.FontSizeProperty, style.FontSize); TextEditor.Selection.ApplyPropertyValue(TextElement.ForegroundProperty, Ui.Brush(style.Foreground)); TextEditor.Selection.Select(TextEditor.Document.ContentEnd, TextEditor.Document.ContentEnd); } QueueSave(); Flush(); ApplyAppearance(); }
        }
        private ContextMenu Menu()
        {
            var m = new ContextMenu(); m.Items.Add(Ui.Item("设置标题…", () => Run("title"), Host.ShortcutFor("title"))); m.Items.Add(Ui.Item("设置颜色…", () => Host.SetColor(Note)));
            var styles = Ui.Sub("设为样式"); for (int i = 0; i < 10; i++) { int ix = i; styles.Items.Add(Ui.Item(i + "  " + Host.Library.Catalog.Settings.Styles[i].Name, () => ApplyStyle(ix), "Alt+" + i)); }
            m.Items.Add(styles);
            m.Items.Add(new Separator()); m.Items.Add(Ui.Item("置顶", () => Toggle("top"), Host.ShortcutFor("top"), Note.Topmost)); m.Items.Add(Ui.Item("卷起", () => ToggleRoll(), Host.ShortcutFor("roll"), Note.Rolled)); m.Items.Add(Ui.Item("锁定内容", () => Toggle("lock"), Host.ShortcutFor("lock"), Note.Locked));
            var opacity = Ui.Sub("不透明度"); foreach (var percent in new[] { 100, 90, 80, 70, 60, 50, 40, 30, 20 }) { var value = percent; opacity.Items.Add(Ui.Item(value + "%", () => { showOpacity = true; Note.Opacity = value / 100d; Host.Library.Save(Note); ApplyAppearance(); }, "", Math.Abs(Note.Opacity - value / 100d) < .01)); }
            m.Items.Add(opacity);
            m.Items.Add(Ui.Item("Ghost / 点击穿透", () => Toggle("ghost"), "", Note.Ghost)); m.Items.Add(Ui.Item("秘密便签", () => Toggle("secret"), "", Note.Secret)); m.Items.Add(Ui.Item("滚动模式", () => Toggle("scroll"), "", Note.Scrollbar)); m.Items.Add(Ui.Item("底部固定（Stand）", () => Toggle("stand"), "", Note.Stand)); m.Items.Add(Ui.Item(Host.SoloId == Note.Id ? "取消 Solo" : "Solo / 仅显示此便签", () => Host.Solo(Note)));
            m.Items.Add(new Separator()); m.Items.Add(Ui.Item("闹钟…", () => Run("alarm"), Host.ShortcutFor("alarm"))); var sleep = Ui.Sub("休眠"); foreach (var minutes in new[] { 5, 15, 30, 60, 180, 1440 }) { int value = minutes; sleep.Items.Add(Ui.Item(value >= 60 ? value / 60 + " " + Ui.Text("小时") : value + " " + Ui.Text("分钟"), () => { Flush(); Host.Library.Sleep(Note, DateTime.UtcNow.AddMinutes(value)); })); }
            sleep.Items.Add(Ui.Item("明天", () => Run("tomorrow"), Host.ShortcutFor("tomorrow"))); sleep.Items.Add(Ui.Item("自定义…", () => Run("sleep"), Host.ShortcutFor("sleep"))); sleep.Items.Add(Ui.Item("重复…", () => Run("repeat"), Host.ShortcutFor("repeat"))); m.Items.Add(sleep);
            m.Items.Add(Ui.Item(Note.Attachment == null ? "附着到窗口…" : "修改附着 / 分离…", () => new AttachWindow(Host, Note).ShowDialog())); m.Items.Add(Ui.Item("标签…", () => Host.Tags(Note)));
            var stored = Ui.Sub("存入文件夹"); stored.Items.Add(Ui.Item("根目录", () => { Flush(); Host.Library.Move(Note, NoteState.Stored); })); foreach (var folder in Host.Library.Catalog.Folders) { var f = folder; stored.Items.Add(Ui.Item(Host.FolderPath(f), () => { Flush(); Host.EnsureUnlocked(f); Host.Library.Move(Note, NoteState.Stored, f.Id); })); }
            m.Items.Add(stored);
            var stacks = Ui.Sub("放入堆叠"); stacks.Items.Add(Ui.Item("移出堆叠", () => { Note.StackId = ""; Host.Library.Save(Note); })); stacks.Items.Add(Ui.Item("新建堆叠…", () => Host.NewStack(Note))); foreach (var stack in Host.Library.Catalog.Stacks) { var st = stack; stacks.Items.Add(Ui.Item(st.Name, () => { Note.StackId = st.Id; Host.Library.Save(Note); })); }
            m.Items.Add(stacks);
            var send = Ui.Sub("发送到"); foreach (var friend in Host.Library.Catalog.Friends.Where(f => !f.Separator)) { var f = friend; send.Items.Add(Ui.Item((f.Group.Length > 0 ? f.Group + " / " : "") + f.Name, () => { Flush(); Host.Send(Note, f); })); }
            send.Items.Add(Ui.Item("地址 / 多人…", () => { Flush(); Host.SendCustom(Note); })); m.Items.Add(send);
            m.Items.Add(new Separator()); m.Items.Add(Ui.Item("复制便签", () => { Flush(); Host.Focus(Host.Library.Copy(Note)); })); m.Items.Add(Ui.Item("保存为…", () => Run("save"), Host.ShortcutFor("save"))); m.Items.Add(Ui.Item("复制到传输文件", () => { Flush(); Host.TransferFile(Note, false); })); m.Items.Add(Ui.Item("发送到传输文件", () => { Flush(); Host.TransferFile(Note, true); })); m.Items.Add(Ui.Item("打印…", () => Run("print"), Host.ShortcutFor("print"))); m.Items.Add(Ui.Item("设置皮肤…", () => Run("skin"), Host.ShortcutFor("skin"))); m.Items.Add(Ui.Item("恢复默认皮肤", () => { Note.SkinPath = ""; Host.Library.Save(Note); ApplyAppearance(); }));
            Host.AddExtensionMenus(m, Note); m.Items.Add(new Separator()); m.Items.Add(Ui.Item("管理便签…", () => Run("manage"), Host.ShortcutFor("manage"))); m.Items.Add(Ui.Item("关闭（可恢复）", () => Run("close"), Host.ShortcutFor("close"))); m.Items.Add(Ui.Item("永久删除…", () => Run("delete"), Host.ShortcutFor("delete"))); return m;
        }
        private ContextMenu TextMenu()
        {
            var m = new ContextMenu(); m.Items.Add(Ui.Item("剪切", () => TextEditor.Cut(), Host.ShortcutFor("cut"))); m.Items.Add(Ui.Item("复制", () => TextEditor.Copy(), Host.ShortcutFor("copy"))); m.Items.Add(Ui.Item("粘贴", () => Host.Paste(this), Host.ShortcutFor("paste"))); m.Items.Add(Ui.Item("粘贴为纯文本", () => Run("paste-plain"), Host.ShortcutFor("paste-plain"))); m.Items.Add(new Separator()); m.Items.Add(Ui.Item("字体…", () => TextEditor.FontDialog())); foreach (var pair in new[] { new[] { "粗体", "bold" }, new[] { "斜体", "italic" }, new[] { "下划线", "underline" }, new[] { "删除线", "strike" }, new[] { "项目符号", "bullets" }, new[] { "编号", "numbers" }, new[] { "左对齐", "left" }, new[] { "居中", "center" }, new[] { "右对齐", "right" }, new[] { "从左到右", "ltr" }, new[] { "从右到左", "rtl" }, new[] { "插入复选框", "checkbox" }, new[] { "插入链接…", "link" }, new[] { "插入日期时间", "date" } }) { var action = pair[1]; m.Items.Add(Ui.Item(pair[0], () => Run(action))); }
            m.Items.Add(Ui.Item("查找…", () => Run("find"), Host.ShortcutFor("find")));
            var highlight = Ui.Sub("高亮");
            for (int i = 0; i < Host.Library.Catalog.Settings.HighlightColors.Count && i < 7; i++)
            {
                var color = Host.Library.Catalog.Settings.HighlightColors[i];
                highlight.Items.Add(Ui.Item(Ui.Text("颜色") + " " + (i + 1), () => TextEditor.Highlight(color)));
            }
            highlight.Items.Add(new Separator()); highlight.Items.Add(Ui.Item("清除高亮", () => TextEditor.Highlight(null)));
            m.Items.Add(highlight); return m;
        }
        private ContextMenu ImageMenu()
        {
            var m = new ContextMenu(); var tools = new[] { "选择", "画笔", "文字", "编号", "橡皮擦", "直线", "箭头", "矩形", "填充", "椭圆", "取色" }; foreach (ImageTool tool in Enum.GetValues(typeof(ImageTool))) { var t = tool; m.Items.Add(Ui.Item(tools[(int)tool], () => ImageEditor.Tool = t)); }
            m.Items.Add(new Separator()); m.Items.Add(Ui.Item("画笔颜色…", () => { var c = Ui.Color("#FF0000"); if (c != null) ImageEditor.PenColor = ((SolidColorBrush)Ui.Brush(c)).Color; })); m.Items.Add(Ui.Item("画笔宽度…", () => { var v = Ui.Ask("画笔宽度", "像素", ImageEditor.PenWidth.ToString()); if (v != null) ImageEditor.PenWidth = Math.Max(1, Math.Min(80, Ui.Number(v, 3))); })); m.Items.Add(Ui.Item("载入图片…", () => { var p = Ui.OpenFile("图片|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif"); if (p != null) { Host.LoadImage(Note, p); ReloadContent(); } })); m.Items.Add(Ui.Item("粘贴图片", () => Host.Paste(this))); m.Items.Add(Ui.Item("复制选区", () => ImageEditor.CopySelection())); m.Items.Add(Ui.Item("粘贴到图片内", () => { if (Clipboard.ContainsImage()) ImageEditor.PasteInto(Clipboard.GetImage()); }, "Shift+E")); m.Items.Add(Ui.Item("增加边框", () => ImageEditor.AddBorder())); m.Items.Add(Ui.Item("裁剪到选区", () => ImageEditor.Crop())); m.Items.Add(Ui.Item("调整图片大小…", () => Run("image-size"), "Shift+S")); foreach (var pair in new[] { new[] { "向左旋转", "left" }, new[] { "向右旋转", "right" }, new[] { "水平镜像", "mirror" }, new[] { "垂直翻转", "flip" }, new[] { "灰度", "gray" }, new[] { "模糊", "blur" }, new[] { "抖动", "jitter" } }) { var action = pair[1]; m.Items.Add(Ui.Item(pair[0], () => ImageEditor.Transform(action))); }
            m.Items.Add(Ui.Item("撤销", () => ImageEditor.Undo(), Host.ShortcutFor("undo"))); m.Items.Add(Ui.Item("重做", () => ImageEditor.Undo(true), Host.ShortcutFor("redo"))); return m;
        }
    }
}
