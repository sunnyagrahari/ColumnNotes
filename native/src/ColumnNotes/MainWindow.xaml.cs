using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using ColumnNotes.Models;
using ColumnNotes.Services;
using Microsoft.Win32;
using IOPath = System.IO.Path;

namespace ColumnNotes;

public partial class MainWindow : Window
{
    sealed class EditorTab
    {
        public string Id { get; } = Guid.NewGuid().ToString();
        public NoteDocument Doc { get; set; } = NoteDocument.Empty();
        public string? Path { get; set; }
        public bool Dirty { get; set; }
        public Stack<string> Undo { get; } = new();
        public Stack<string> Redo { get; } = new();
        public int ActiveColumn { get; set; }
        public HashSet<string> SelectedIds { get; } = new();
    }

    readonly List<EditorTab> _tabs = new();
    EditorTab _tab = null!;
    NoteDocument _doc { get => _tab.Doc; set => _tab.Doc = value; }
    string? _path { get => _tab.Path; set => _tab.Path = value; }
    bool _dirty { get => _tab.Dirty; set => _tab.Dirty = value; }
    Stack<string> _undo => _tab.Undo;
    Stack<string> _redo => _tab.Redo;
    int _activeColumn { get => _tab.ActiveColumn; set => _tab.ActiveColumn = value; }
    WindowState _beforeFull;
    bool _fullscreen;
    readonly List<List<FrameworkElement>> _blockViews = new();
    readonly List<RichTextBox> _columnBoxes = new();
    string? _focusBlockId;

    public MainWindow()
    {
        InitializeComponent();
        Board.PreviewMouseLeftButtonDown += OnBoardClickAway;
        ApplySettingsChrome();
        var start = NoteDocument.Welcome();
        if (File.Exists(AppPaths.AutosavePath) && SettingsService.Current.RecentFiles.Count > 0)
        {
            try { start = NoteDocument.Parse(File.ReadAllText(AppPaths.AutosavePath), "Untitled"); }
            catch { start = NoteDocument.Welcome(); }
        }
        AddTab(start, start.Title == "Welcome" ? "Welcome.cnotes" : null, dirty: false);
        RebuildRecent();
    }

    public void OpenPath(string path)
    {
        var existing = _tabs.FirstOrDefault(t => string.Equals(t.Path, path, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
        {
            SwitchTab(existing);
            return;
        }
        var doc = NoteDocument.Parse(File.ReadAllText(path, Encoding.UTF8), IOPath.GetFileNameWithoutExtension(path));
        SettingsService.PushRecent(path);
        RebuildRecent();
        AddTab(doc, path, dirty: false);
        Status("Opened " + path);
    }

    void AddTab(NoteDocument doc, string? path, bool dirty)
    {
        var tab = new EditorTab { Doc = doc, Path = path, Dirty = dirty };
        _tabs.Add(tab);
        SwitchTab(tab);
    }

    void SwitchTab(EditorTab tab)
    {
        _tab = tab;
        RebuildTabs();
        RebuildBoard();
        UpdateTitle();
        UpdateStatus();
    }

    void RebuildTabs()
    {
        TabStrip.Children.Clear();
        foreach (var tab in _tabs)
        {
            var name = tab.Path != null ? IOPath.GetFileName(tab.Path) : tab.Doc.Title + ".cnotes";
            var active = tab == _tab;
            var close = new Button
            {
                Content = "×",
                Width = 22,
                Height = 22,
                Padding = new Thickness(0),
                FontSize = 14,
                ToolTip = "Close tab"
            };
            close.Click += (_, e) =>
            {
                e.Handled = true;
                CloseTab(tab);
            };
            var label = new TextBlock
            {
                Text = (tab.Dirty ? "*" : "") + name,
                Margin = new Thickness(12, 0, 4, 0),
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = 12
            };
            var row = new DockPanel
            {
                LastChildFill = true,
                Tag = tab,
                MinWidth = 112,
                Height = 32,
                Cursor = Cursors.Hand,
                Background = active ? (Brush)Resources["PaperBrush"] : Brushes.Transparent
            };
            DockPanel.SetDock(close, Dock.Right);
            row.Children.Add(close);
            row.Children.Add(label);
            row.MouseLeftButtonUp += (_, _) => SwitchTab(tab);
            var wrap = new Border
            {
                Child = row,
                BorderBrush = (Brush)Resources["LineBrush"],
                BorderThickness = new Thickness(0, 0, 1, 0),
                Background = active ? (Brush)Resources["PaperBrush"] : Brushes.Transparent
            };
            TabStrip.Children.Add(wrap);
        }
    }

    void CloseActiveTab(object s, RoutedEventArgs e) => CloseTab(_tab);

    void CloseTab(EditorTab tab)
    {
        if (tab.Dirty)
        {
            var prev = _tab;
            _tab = tab;
            if (!ConfirmDiscard())
            {
                _tab = prev;
                return;
            }
            _tab = prev;
        }
        _tabs.Remove(tab);
        if (_tabs.Count == 0)
            AddTab(NoteDocument.Empty(), null, dirty: false);
        else if (_tab == tab)
            SwitchTab(_tabs[^1]);
        else
            RebuildTabs();
    }

    void ApplySettingsChrome()
    {
        WordWrapItem.IsChecked = SettingsService.Current.WordWrap;
        StatusBarItem.IsChecked = SettingsService.Current.ShowStatusBar;
        StatusBar.Visibility = SettingsService.Current.ShowStatusBar ? Visibility.Visible : Visibility.Collapsed;
        ApplyTheme(SettingsService.Current.Theme);
        StatusZoom.Text = $"Zoom {SettingsService.Current.Zoom}%";
    }

    void ApplyTheme(string theme)
    {
        SettingsService.Current.Theme = theme;
        var paper = theme == "dark" ? Color.FromRgb(0x16, 0x14, 0x12) : Color.FromRgb(0xF4, 0xEF, 0xE6);
        var chrome = theme == "dark" ? Color.FromRgb(0x22, 0x1F, 0x1B) : Color.FromRgb(0xE8, 0xE2, 0xD6);
        var ink = theme == "dark" ? Color.FromRgb(0xED, 0xE8, 0xDF) : Color.FromRgb(0x1A, 0x17, 0x14);
        Resources["PaperBrush"] = new SolidColorBrush(paper);
        Resources["ChromeBrush"] = new SolidColorBrush(chrome);
        Resources["InkBrush"] = new SolidColorBrush(ink);
        Board.Background = (Brush)Resources["PaperBrush"];
        Background = (Brush)Resources["PaperBrush"];
        Foreground = (Brush)Resources["InkBrush"];
    }

    void RebuildBoard()
    {
        Board.Children.Clear();
        Board.ColumnDefinitions.Clear();
        _blockViews.Clear();
        _columnBoxes.Clear();
        var n = Math.Max(1, _doc.Columns.Count);
        for (var i = 0; i < n; i++)
        {
            _doc.Columns[i].EnsureMeta(i);
            Board.ColumnDefinitions.Add(new ColumnDefinition());
            var colPanel = new DockPanel { LastChildFill = true };
            var header = BuildColumnHeader(i);
            DockPanel.SetDock(header, Dock.Top);
            colPanel.Children.Add(header);

            var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, AllowDrop = true };
            var stack = new StackPanel { Margin = new Thickness(4, 0, 4, 4), AllowDrop = true };
            stack.DragOver += (_, e) => { e.Effects = DragDropEffects.Move; e.Handled = true; };
            stack.Drop += (_, e) => HandleDrop(e, colIndex, null, null);
            var views = new List<FrameworkElement>();
            var colIndex = i;
            var col = _doc.Columns[i];
            foreach (var section in col.Sections)
            {
                var secBlocks = col.Blocks.Where(b => b.SectionId == section.Id).ToList();
                var band = new Border
                {
                    Background = SectionBrush(section.Color),
                    Padding = new Thickness(8, 6, 8, 8),
                    Margin = new Thickness(0, 0, 0, 8),
                    CornerRadius = new CornerRadius(6),
                    AllowDrop = true,
                    Tag = section
                };
                band.DragOver += (_, e) => { e.Effects = DragDropEffects.Move; e.Handled = true; };
                band.Drop += (_, e) => HandleDrop(e, colIndex, section.Id, null);
                var inner = new StackPanel();
                inner.Children.Add(BuildSectionHeader(colIndex, section));
                foreach (var block in secBlocks)
                {
                    var row = BuildBlock(block, colIndex);
                    inner.Children.Add(row);
                    views.Add(row);
                }
                band.Child = inner;
                stack.Children.Add(band);
            }
            scroll.Content = stack;
            colPanel.Children.Add(scroll);
            Grid.SetColumn(colPanel, i);
            Board.Children.Add(colPanel);
            _blockViews.Add(views);
            if (i < n - 1)
            {
                var split = new Border { Width = 1, Background = new SolidColorBrush(Color.FromArgb(40, 0, 0, 0)) };
                Grid.SetColumn(split, i);
                split.HorizontalAlignment = HorizontalAlignment.Right;
                Board.Children.Add(split);
            }
        }
    }

    FrameworkElement BuildColumnHeader(int columnIndex)
    {
        var col = _doc.Columns[columnIndex];
        var box = new TextBox
        {
            Text = col.Name,
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 13,
            FontWeight = FontWeights.Bold,
            Padding = new Thickness(10, 6, 4, 6),
            VerticalContentAlignment = VerticalAlignment.Center
        };
        box.GotFocus += (_, _) => _activeColumn = columnIndex;
        box.LostFocus += (_, _) =>
        {
            col.Name = string.IsNullOrWhiteSpace(box.Text) ? $"Column {columnIndex + 1}" : box.Text.Trim();
            box.Text = col.Name;
            MarkDirty();
        };
        box.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                Keyboard.ClearFocus();
                e.Handled = true;
            }
        };
        var add = new Button
        {
            Content = "+",
            Width = 28,
            Height = 28,
            FontSize = 16,
            ToolTip = "Add section",
            Margin = new Thickness(4, 2, 6, 2)
        };
        add.Click += (_, _) =>
        {
            _activeColumn = columnIndex;
            InsertSection(this, new RoutedEventArgs());
        };
        var row = new DockPanel { LastChildFill = true, Background = (Brush)Resources["ChromeBrush"] };
        row.Height = 32;
        DockPanel.SetDock(add, Dock.Right);
        row.Children.Add(add);
        row.Children.Add(box);
        return row;
    }

    FrameworkElement BuildSectionHeader(int columnIndex, NoteSection section)
    {
        var grip = DragHandle();
        AttachDrag(grip, "section", columnIndex, section.Id);
        var title = new TextBox
        {
            Text = section.Title,
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            FontSize = 11,
            Padding = new Thickness(0, 0, 8, 4),
            Foreground = Brushes.Gray
        };
        title.LostFocus += (_, _) =>
        {
            if (!string.IsNullOrWhiteSpace(title.Text)) section.Title = title.Text.Trim();
            MarkDirty();
        };
        var colors = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var id in NoteSection.Colors)
        {
            var c = id;
            var swatch = new Border
            {
                Width = 14,
                Height = 14,
                CornerRadius = new CornerRadius(7),
                Margin = new Thickness(3, 0, 0, 0),
                Background = SectionDot(c),
                BorderBrush = section.Color == c ? (Brush)Resources["InkBrush"] : (Brush)Resources["LineBrush"],
                BorderThickness = new Thickness(section.Color == c ? 2 : 1),
                Cursor = Cursors.Hand,
                Tag = c,
                ToolTip = c
            };
            swatch.MouseLeftButtonUp += (_, _) =>
            {
                section.Color = c;
                RebuildBoard();
                MarkDirty();
            };
            colors.Children.Add(swatch);
        }
        var close = new Button { Content = "×", Width = 22, Height = 22, FontSize = 14, ToolTip = "Delete section" };
        close.Click += (_, _) =>
        {
            Snapshot();
            _doc.DeleteSection(columnIndex, section.Id);
            RebuildBoard();
            MarkDirty();
            Status("Section deleted");
        };
        var row = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(grip, Dock.Left);
        DockPanel.SetDock(close, Dock.Right);
        DockPanel.SetDock(colors, Dock.Right);
        row.Children.Add(grip);
        row.Children.Add(close);
        row.Children.Add(colors);
        row.Children.Add(title);
        return row;
    }

    static Color SectionRgb(string color) => color switch
    {
        "sage" => Color.FromRgb(0x27, 0x67, 0x49),
        "ochre" => Color.FromRgb(0x92, 0x40, 0x0E),
        "slate" => Color.FromRgb(0x2B, 0x4C, 0x7E),
        "rose" => Color.FromRgb(0x9B, 0x2C, 0x2C),
        "mist" => Color.FromRgb(0x4A, 0x55, 0x68),
        _ => Color.FromRgb(0xF4, 0xEF, 0xE6)
    };

    static Brush SectionDot(string color) => new SolidColorBrush(SectionRgb(color));

    static Brush SectionBrush(string color)
    {
        if (color == "paper") return Brushes.Transparent;
        var c = SectionRgb(color);
        return new SolidColorBrush(Color.FromArgb(42, c.R, c.G, c.B));
    }

    FrameworkElement BuildBlock(NoteBlock block, int columnIndex)
    {
        var row = new DockPanel { LastChildFill = true, Tag = block };
        var grip = DragHandle();
        AttachDrag(grip, "block", columnIndex, block.Id);
        DockPanel.SetDock(grip, Dock.Left);
        row.Children.Add(grip);

        var rtb = new RichTextBox
        {
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            Document = block.ToFlowDocument(),
            FontFamily = new FontFamily(SettingsService.Current.FontFamily),
            FontSize = SettingsService.Current.FontSize * SettingsService.Current.Zoom / 100.0,
            AcceptsReturn = false,
            VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
            Tag = block
        };
        rtb.GotFocus += (_, _) => _activeColumn = columnIndex;
        rtb.PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter || Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) return;
            e.Handled = true;
            Snapshot();
            var next = block.Type == "check"
                ? NoteBlock.Check("", false, block.SectionId)
                : NoteBlock.Paragraph("", false, block.SectionId);
            _doc.InsertAfter(columnIndex, block.Id, next);
            _focusBlockId = next.Id;
            RebuildBoard();
            MarkDirty();
        };
        rtb.TextChanged += (_, _) =>
        {
            block.CaptureFrom(new RichTextBoxAdapter(rtb.Document));
            MarkDirty();
        };
        rtb.SelectionChanged += (_, _) =>
        {
            if (rtb.IsFocused && rtb.Selection.Text.Length > 0)
            {
                _tab.SelectedIds.Clear();
                _tab.SelectedIds.Add(block.Id);
            }
        };
        TextOptions.SetTextFormattingMode(rtb, TextFormattingMode.Display);
        if (!SettingsService.Current.WordWrap)
            rtb.Document.PageWidth = 4000;
        _columnBoxes.Add(rtb);
        if (block.Id == _focusBlockId)
        {
            _focusBlockId = null;
            rtb.Loaded += (_, _) => rtb.Focus();
        }

        if (block.Type == "check")
        {
            var cb = new CheckBox
            {
                IsChecked = block.IsChecked == true,
                Margin = new Thickness(0, 4, 8, 0),
                VerticalAlignment = VerticalAlignment.Top
            };
            cb.Checked += (_, _) => { block.IsChecked = true; MarkDirty(); UpdateStatus(); };
            cb.Unchecked += (_, _) => { block.IsChecked = false; MarkDirty(); UpdateStatus(); };
            DockPanel.SetDock(cb, Dock.Left);
            row.Children.Add(cb);
            if (block.IsChecked == true)
                rtb.Foreground = Brushes.Gray;
        }
        row.Children.Add(rtb);

        var frame = new Border
        {
            Child = row,
            BorderBrush = _tab.SelectedIds.Contains(block.Id)
                ? (Brush)Resources["AccentBrush"]
                : new SolidColorBrush(Color.FromArgb(48, 26, 23, 20)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(4, 2, 6, 2),
            Margin = new Thickness(0, 2, 0, 2),
            Background = _tab.SelectedIds.Contains(block.Id)
                ? new SolidColorBrush(Color.FromArgb(40, 61, 107, 90))
                : new SolidColorBrush(Color.FromArgb(24, 255, 255, 255)),
            AllowDrop = true,
            Tag = block
        };
        frame.DragOver += (_, e) => { e.Effects = DragDropEffects.Move; e.Handled = true; };
        frame.Drop += (_, e) => HandleDrop(e, columnIndex, block.SectionId, block.Id);
        return frame;
    }

    TextBlock DragHandle() => new()
    {
        Text = "⋮⋮",
        FontSize = 11,
        Opacity = 0.45,
        Margin = new Thickness(0, 4, 6, 0),
        Cursor = Cursors.SizeAll,
        VerticalAlignment = VerticalAlignment.Top,
        ToolTip = "Drag"
    };

    void AttachDrag(FrameworkElement handle, string kind, int columnIndex, string id)
    {
        Point? start = null;
        handle.PreviewMouseLeftButtonDown += (_, e) => start = e.GetPosition(this);
        handle.MouseMove += (_, e) =>
        {
            if (e.LeftButton != MouseButtonState.Pressed || start == null) return;
            var now = e.GetPosition(this);
            if (Math.Abs(now.X - start.Value.X) < 4 && Math.Abs(now.Y - start.Value.Y) < 4) return;
            start = null;
            DragDrop.DoDragDrop(handle, new DataObject("cnotes", $"{kind}|{columnIndex}|{id}"), DragDropEffects.Move);
        };
    }

    void HandleDrop(DragEventArgs e, int toCol, string? sectionId, string? beforeBlockId)
    {
        if (!e.Data.GetDataPresent("cnotes")) return;
        var raw = e.Data.GetData("cnotes") as string;
        if (string.IsNullOrEmpty(raw)) return;
        var parts = raw.Split('|');
        if (parts.Length != 3) return;
        var kind = parts[0];
        if (!int.TryParse(parts[1], out var fromCol)) return;
        var id = parts[2];
        e.Handled = true;
        Snapshot();
        if (kind == "section")
            _doc.MoveSection(fromCol, id, toCol, sectionId);
        else
        {
            var destSection = sectionId ?? _doc.Columns[Math.Clamp(toCol, 0, _doc.Columns.Count - 1)].Sections.LastOrDefault()?.Id;
            if (destSection == null) return;
            _doc.MoveBlock(fromCol, id, toCol, destSection, beforeBlockId);
        }
        RebuildBoard();
        MarkDirty();
    }

    void OnBoardClickAway(object sender, MouseButtonEventArgs e)
    {
        if (_tab.SelectedIds.Count == 0) return;
        DependencyObject? cur = e.OriginalSource as DependencyObject;
        while (cur != null)
        {
            if (cur is FrameworkElement fe && fe.Tag is NoteBlock block && _tab.SelectedIds.Contains(block.Id))
                return;
            cur = VisualTreeHelper.GetParent(cur);
        }
        _tab.SelectedIds.Clear();
        RebuildBoard();
    }

    void CaptureBoard()
    {
        foreach (var col in _doc.Columns)
        foreach (var b in col.Blocks)
        {
            /* already captured on TextChanged */
        }
        _doc.RefreshPlainText();
    }

    void Snapshot()
    {
        CaptureBoard();
        _undo.Push(NoteDocument.Serialize(_doc));
        _redo.Clear();
        if (_undo.Count > 80)
        {
            var keep = _undo.Take(80).Reverse().ToArray();
            _undo.Clear();
            foreach (var snap in keep) _undo.Push(snap);
        }
    }

    void MarkDirty()
    {
        var first = !_dirty;
        _dirty = true;
        UpdateTitle();
        if (first) RebuildTabs();
        UpdateStatus();
        try { File.WriteAllText(AppPaths.AutosavePath, NoteDocument.Serialize(_doc)); } catch { /* ignore */ }
    }

    void UpdateTitle()
    {
        var name = _path != null ? IOPath.GetFileName(_path) : _doc.Title + ".cnotes";
        Title = $"{(_dirty ? "*" : "")}{name} — ColumnNotes";
        if (TitleLabel != null) TitleLabel.Text = Title;
    }

    void UpdateStatus()
    {
        var (n, total) = _doc.CheckboxStats();
        StatusColumns.Text = $"Col {_activeColumn + 1}/{_doc.Columns.Count}";
        StatusZoom.Text = $"Zoom {SettingsService.Current.Zoom}%";
        StatusChecks.Text = total == 0 ? "No checks" : $"Checks {n}/{total}";
    }

    void Status(string msg) => StatusMessage.Text = msg;

    void RebuildRecent()
    {
        RecentMenu.Items.Clear();
        foreach (var p in SettingsService.Current.RecentFiles)
        {
            var item = new MenuItem { Header = p, Tag = p };
            item.Click += (_, _) => { if (File.Exists(p)) OpenPath(p); };
            RecentMenu.Items.Add(item);
        }
        if (RecentMenu.Items.Count == 0)
            RecentMenu.Items.Add(new MenuItem { Header = "(empty)", IsEnabled = false });
    }

    void NewDoc(object s, RoutedEventArgs e)
    {
        AddTab(NoteDocument.Empty(), null, dirty: false);
    }

    void NewWindow(object s, RoutedEventArgs e)
    {
        var w = new MainWindow();
        w.Show();
    }

    void OpenDoc(object s, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Filter = "ColumnNotes (*.cnotes)|*.cnotes|Text (*.txt)|*.txt|All files (*.*)|*.*"
        };
        if (dlg.ShowDialog() == true) OpenPath(dlg.FileName);
    }

    void SaveDoc(object s, RoutedEventArgs e) => Save(false, "cnotes");
    void SaveDocAs(object s, RoutedEventArgs e) => Save(true, "cnotes");
    void ExportHtml(object s, RoutedEventArgs e) => Save(true, "html");
    void ExportMd(object s, RoutedEventArgs e) => Save(true, "md");
    void ExportRtf(object s, RoutedEventArgs e) => Save(true, "rtf");
    void ExportTxt(object s, RoutedEventArgs e) => Save(true, "txt");

    void Save(bool saveAs, string kind)
    {
        CaptureBoard();
        string? path = saveAs ? null : _path;
        if (path == null || kind != "cnotes")
        {
            var dlg = new SaveFileDialog
            {
                FileName = (_doc.Title ?? "Untitled") + "." + kind,
                Filter = kind switch
                {
                    "html" => "HTML (*.html)|*.html",
                    "md" => "Markdown (*.md)|*.md",
                    "rtf" => "RTF (*.rtf)|*.rtf",
                    "txt" => "Text (*.txt)|*.txt",
                    _ => "ColumnNotes (*.cnotes)|*.cnotes"
                }
            };
            if (dlg.ShowDialog() != true) return;
            path = dlg.FileName;
        }
        var text = kind switch
        {
            "html" => ExportService.ToHtml(_doc),
            "md" => ExportService.ToMarkdown(_doc),
            "rtf" => ExportService.ToRtf(_doc),
            "txt" => _doc.PlainText,
            _ => NoteDocument.Serialize(_doc)
        };
        File.WriteAllText(path, text, new UTF8Encoding(false));
        if (kind == "cnotes")
        {
            _path = path;
            _doc.Title = IOPath.GetFileNameWithoutExtension(path);
            _dirty = false;
            SettingsService.PushRecent(path);
            RebuildRecent();
            RebuildTabs();
        }
        UpdateTitle();
        Status("Saved " + path);
    }

    void PrintDoc(object s, RoutedEventArgs e)
    {
        CaptureBoard();
        var fd = new FlowDocument();
        fd.Blocks.Add(new Paragraph(new Run(_doc.Title)) { FontWeight = FontWeights.Bold });
        foreach (var col in _doc.Columns)
        foreach (var b in col.Blocks)
        {
            var prefix = b.Type == "check" ? (b.IsChecked == true ? "[x] " : "[ ] ") : "";
            fd.Blocks.Add(new Paragraph(new Run(prefix + b.PlainText)));
        }
        var dlg = new PrintDialog();
        if (dlg.ShowDialog() == true)
            dlg.PrintDocument(((IDocumentPaginatorSource)fd).DocumentPaginator, _doc.Title);
    }

    void ExitApp(object s, RoutedEventArgs e) => Close();

    void Undo(object s, RoutedEventArgs e)
    {
        if (_undo.Count == 0) return;
        CaptureBoard();
        _redo.Push(NoteDocument.Serialize(_doc));
        _doc = NoteDocument.Parse(_undo.Pop());
        RebuildBoard();
        MarkDirty();
    }

    void Redo(object s, RoutedEventArgs e)
    {
        if (_redo.Count == 0) return;
        CaptureBoard();
        _undo.Push(NoteDocument.Serialize(_doc));
        _doc = NoteDocument.Parse(_redo.Pop());
        RebuildBoard();
        MarkDirty();
    }

    void PastePlain(object s, RoutedEventArgs e)
    {
        if (!Clipboard.ContainsText()) return;
        var text = Clipboard.GetText();
        var rtb = Keyboard.FocusedElement as RichTextBox;
        rtb?.CaretPosition.InsertTextInRun(text);
    }

    void ShowFind(object s, RoutedEventArgs e)
    {
        FindBar.Visibility = Visibility.Visible;
        ReplaceBox.Visibility = Visibility.Collapsed;
        ReplaceBtn.Visibility = Visibility.Collapsed;
        ReplaceAllBtn.Visibility = Visibility.Collapsed;
        FindBox.Focus();
    }

    void ShowReplace(object s, RoutedEventArgs e)
    {
        ShowFind(s, e);
        ReplaceBox.Visibility = Visibility.Visible;
        ReplaceBtn.Visibility = Visibility.Visible;
        ReplaceAllBtn.Visibility = Visibility.Visible;
    }

    void HideFind(object s, RoutedEventArgs e) => FindBar.Visibility = Visibility.Collapsed;

    void FindBoxKey(object s, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { FindNext(s, e); e.Handled = true; }
        if (e.Key == Key.Escape) HideFind(s, e);
    }

    void FindNext(object s, RoutedEventArgs e)
    {
        var q = FindBox.Text;
        if (string.IsNullOrEmpty(q)) return;
        var cmp = MatchCaseBox.IsChecked == true ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        foreach (var col in _doc.Columns)
        foreach (var b in col.Blocks)
        {
            if (b.PlainText.Contains(q, cmp))
            {
                Status("Found in current document");
                return;
            }
        }
        Status("No matches");
    }

    void ReplaceOne(object s, RoutedEventArgs e)
    {
        var q = FindBox.Text;
        if (string.IsNullOrEmpty(q)) return;
        Snapshot();
        var cmp = MatchCaseBox.IsChecked == true ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        foreach (var col in _doc.Columns)
        foreach (var b in col.Blocks)
        {
            var idx = b.PlainText.IndexOf(q, cmp);
            if (idx < 0) continue;
            b.PlainText = b.PlainText.Remove(idx, q.Length).Insert(idx, ReplaceBox.Text);
            b.Html = System.Net.WebUtility.HtmlEncode(b.PlainText);
            RebuildBoard();
            MarkDirty();
            return;
        }
    }

    void ReplaceAll(object s, RoutedEventArgs e)
    {
        var q = FindBox.Text;
        if (string.IsNullOrEmpty(q)) return;
        Snapshot();
        foreach (var col in _doc.Columns)
        foreach (var b in col.Blocks)
        {
            b.PlainText = MatchCaseBox.IsChecked == true
                ? b.PlainText.Replace(q, ReplaceBox.Text)
                : ReplaceInsensitive(b.PlainText, q, ReplaceBox.Text);
            b.Html = System.Net.WebUtility.HtmlEncode(b.PlainText);
        }
        RebuildBoard();
        MarkDirty();
    }

    static string ReplaceInsensitive(string source, string q, string r) =>
        System.Text.RegularExpressions.Regex.Replace(source, System.Text.RegularExpressions.Regex.Escape(q), r,
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    void ConvertToChecks(object s, RoutedEventArgs e)
    {
        Snapshot();
        var col = _doc.Columns[Math.Clamp(_activeColumn, 0, _doc.Columns.Count - 1)];
        var selected = _tab.SelectedIds;
        var targets = selected.Count > 0
            ? col.Blocks.Where(b => selected.Contains(b.Id)).ToList()
            : col.Blocks.ToList();
        if (targets.Count == 0) return;
        var allChecks = targets.All(b => b.Type == "check");
        var keep = new HashSet<NoteBlock>();
        if (allChecks)
        {
            foreach (var b in targets) b.AsParagraph();
            keep = targets.ToHashSet();
            Status("Checkboxes off");
        }
        else
        {
            var next = new List<NoteBlock>();
            foreach (var b in col.Blocks)
            {
                if (targets.Contains(b))
                {
                    foreach (var n in SplitToChecks(b))
                    {
                        next.Add(n);
                        keep.Add(n);
                    }
                }
                else next.Add(b);
            }
            col.Blocks = next;
            Status("Checkboxes on");
        }
        _tab.SelectedIds.Clear();
        foreach (var b in keep) _tab.SelectedIds.Add(b.Id);
        RebuildBoard();
        MarkDirty();
    }

    static IEnumerable<NoteBlock> SplitToChecks(NoteBlock b)
    {
        if (b.Type == "check") { yield return b; yield break; }
        var lines = (b.PlainText ?? "").Replace("\r\n", "\n").Split('\n');
        if (lines.Length == 0) yield return NoteBlock.Check("", false, b.SectionId);
        foreach (var line in lines) yield return NoteBlock.Check(line, false, b.SectionId);
    }

    void InsertSection(object s, RoutedEventArgs e)
    {
        Snapshot();
        _doc.AddSection(_activeColumn);
        RebuildBoard();
        MarkDirty();
        Status("Section added");
    }

    void SelectAllColumn(object s, RoutedEventArgs e)
    {
        var col = _doc.Columns[Math.Clamp(_activeColumn, 0, _doc.Columns.Count - 1)];
        var focused = Keyboard.FocusedElement as RichTextBox;
        var sectionId = (focused?.Tag as NoteBlock)?.SectionId ?? col.Sections.FirstOrDefault()?.Id;
        _tab.SelectedIds.Clear();
        foreach (var b in col.Blocks.Where(b => sectionId == null || b.SectionId == sectionId))
            _tab.SelectedIds.Add(b.Id);
        RebuildBoard();
        Status("Selected section");
    }

    void CopyColumnIfNeeded()
    {
        if (_tab.SelectedIds.Count <= 1) return;
        var col = _doc.Columns[Math.Clamp(_activeColumn, 0, _doc.Columns.Count - 1)];
        var blocks = col.Blocks.Where(b => _tab.SelectedIds.Contains(b.Id)).ToList();
        if (blocks.Count == 0) blocks = col.Blocks;
        var text = string.Join("\n", blocks.Select(b =>
            b.Type == "check" ? $"{(b.IsChecked == true ? "[x]" : "[ ]")} {b.PlainText}" : b.PlainText));
        Clipboard.SetText(text);
    }

    void ConvertToParagraphs(object s, RoutedEventArgs e)
    {
        Snapshot();
        var col = _doc.Columns[Math.Clamp(_activeColumn, 0, _doc.Columns.Count - 1)];
        foreach (var b in col.Blocks) if (b.Type == "check") b.AsParagraph();
        RebuildBoard();
        MarkDirty();
    }

    void InsertDateTime(object s, RoutedEventArgs e)
    {
        var stamp = DateTime.Now.ToString("HH:mm dd-MM-yyyy");
        if (Keyboard.FocusedElement is RichTextBox rtb)
            rtb.CaretPosition.InsertTextInRun(stamp);
        MarkDirty();
    }

    void StyleBold(object s, RoutedEventArgs e) => EditingCommands.ToggleBold.Execute(null, Keyboard.FocusedElement as IInputElement);
    void StyleItalic(object s, RoutedEventArgs e) => EditingCommands.ToggleItalic.Execute(null, Keyboard.FocusedElement as IInputElement);
    void StyleUnderline(object s, RoutedEventArgs e) => EditingCommands.ToggleUnderline.Execute(null, Keyboard.FocusedElement as IInputElement);

    void PickColor(object s, RoutedEventArgs e)
    {
        var menu = new ContextMenu();
        var palette = new (string Name, string Hex)[]
        {
            ("Ink", "#1A1714"), ("Brick", "#9B2C2C"), ("Moss", "#276749"),
            ("Slate", "#2B4C7E"), ("Ochre", "#92400E"), ("Sage", "#234E52")
        };
        var row = new WrapPanel { Margin = new Thickness(8), Width = 160 };
        foreach (var (name, hex) in palette)
        {
            var color = (Color)ColorConverter.ConvertFromString(hex)!;
            var swatch = new Button
            {
                Width = 28,
                Height = 28,
                Margin = new Thickness(3),
                Background = new SolidColorBrush(color),
                ToolTip = name
            };
            swatch.Click += (_, _) =>
            {
                if (Keyboard.FocusedElement is RichTextBox rtb)
                    rtb.Selection.ApplyPropertyValue(TextElement.ForegroundProperty, new SolidColorBrush(color));
                menu.IsOpen = false;
            };
            row.Children.Add(swatch);
        }
        menu.Items.Add(new MenuItem { Header = row, StaysOpenOnClick = true });
        menu.PlacementTarget = ColorBtn;
        menu.Placement = PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    void ChooseFont(object s, RoutedEventArgs e)
    {
        var win = new Window
        {
            Title = "Font",
            Width = 360,
            Height = 200,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            Background = (Brush)Resources["PaperBrush"]
        };
        var fonts = new ComboBox { Margin = new Thickness(16, 16, 16, 8) };
        foreach (var f in new[] { "Calibri", "Cambria", "Georgia", "Segoe UI", "Consolas", "Times New Roman" })
            fonts.Items.Add(f);
        fonts.SelectedItem = SettingsService.Current.FontFamily;
        var size = new TextBox { Margin = new Thickness(16, 0, 16, 8), Text = SettingsService.Current.FontSize.ToString() };
        var ok = new Button { Content = "OK", Width = 80, Margin = new Thickness(16), HorizontalAlignment = HorizontalAlignment.Right };
        ok.Click += (_, _) =>
        {
            if (fonts.SelectedItem is string fam) SettingsService.Current.FontFamily = fam;
            if (double.TryParse(size.Text, out var n)) SettingsService.Current.FontSize = n;
            SettingsService.Save();
            RebuildBoard();
            win.Close();
        };
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = "Family", Margin = new Thickness(16, 12, 16, 0) });
        panel.Children.Add(fonts);
        panel.Children.Add(new TextBlock { Text = "Size", Margin = new Thickness(16, 4, 16, 0) });
        panel.Children.Add(size);
        panel.Children.Add(ok);
        win.Content = panel;
        win.ShowDialog();
    }

    void ToggleWordWrap(object s, RoutedEventArgs e)
    {
        SettingsService.Current.WordWrap = WordWrapItem.IsChecked == true;
        SettingsService.Save();
        RebuildBoard();
    }

    void SetColumns(object s, RoutedEventArgs e)
    {
        if (s is MenuItem { Tag: string t } && int.TryParse(t, out var n))
            ChangeColumns(n);
    }

    void ColumnsMenu(object s, RoutedEventArgs e)
    {
        var menu = new ContextMenu();
        var current = _doc.Columns.Count;
        for (var n = 1; n <= 6; n++)
        {
            var count = n;
            var item = new MenuItem
            {
                Header = $"{(count == current ? "✓  " : "    ")}{count} {(count == 1 ? "column" : "columns")}",
                FontWeight = count == current ? FontWeights.SemiBold : FontWeights.Normal
            };
            item.Click += (_, _) => ChangeColumns(count);
            menu.Items.Add(item);
        }
        menu.Items.Add(new Separator());
        var custom = new MenuItem { Header = "    Custom…" };
        custom.Click += CustomColumns;
        menu.Items.Add(custom);
        menu.PlacementTarget = ColumnsBtn;
        menu.Placement = PlacementMode.Bottom;
        menu.HorizontalOffset = 0;
        menu.IsOpen = true;
    }

    void CustomColumns(object s, RoutedEventArgs e)
    {
        var win = new Window
        {
            Title = "Custom columns",
            Width = 320,
            Height = 160,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            Background = (Brush)Resources["PaperBrush"]
        };
        var box = new TextBox { Text = _doc.Columns.Count.ToString(), Margin = new Thickness(16) };
        var ok = new Button { Content = "Apply", Width = 80, Margin = new Thickness(16), HorizontalAlignment = HorizontalAlignment.Right };
        ok.Click += (_, _) =>
        {
            if (int.TryParse(box.Text, out var n)) ChangeColumns(n);
            win.Close();
        };
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = "Columns (1–12)", Margin = new Thickness(16, 12, 16, 0) });
        panel.Children.Add(box);
        panel.Children.Add(ok);
        win.Content = panel;
        win.ShowDialog();
    }

    void ChangeColumns(int n)
    {
        n = Math.Clamp(n, 1, 12);
        if (n < _doc.Columns.Count)
        {
            var r = MessageBox.Show(
                $"Reducing from {_doc.Columns.Count} to {n} columns will merge leftover text into the last remaining column. Nothing is deleted.",
                "Merge columns?",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Question);
            if (r != MessageBoxResult.OK) return;
        }
        Snapshot();
        _doc.SetColumnCount(n);
        RebuildBoard();
        MarkDirty();
    }

    void ZoomIn(object s, RoutedEventArgs e) { SettingsService.Current.Zoom = Math.Min(300, SettingsService.Current.Zoom + 10); SettingsService.Save(); RebuildBoard(); UpdateStatus(); }
    void ZoomOut(object s, RoutedEventArgs e) { SettingsService.Current.Zoom = Math.Max(50, SettingsService.Current.Zoom - 10); SettingsService.Save(); RebuildBoard(); UpdateStatus(); }
    void ZoomReset(object s, RoutedEventArgs e) { SettingsService.Current.Zoom = 100; SettingsService.Save(); RebuildBoard(); UpdateStatus(); }

    void ToggleStatusBar(object s, RoutedEventArgs e)
    {
        SettingsService.Current.ShowStatusBar = StatusBarItem.IsChecked == true;
        StatusBar.Visibility = SettingsService.Current.ShowStatusBar ? Visibility.Visible : Visibility.Collapsed;
        SettingsService.Save();
    }

    void SetLight(object s, RoutedEventArgs e) { ApplyTheme("light"); SettingsService.Save(); }
    void SetDark(object s, RoutedEventArgs e) { ApplyTheme("dark"); SettingsService.Save(); }

    void ToggleFullScreen(object s, RoutedEventArgs e)
    {
        if (!_fullscreen)
        {
            _beforeFull = WindowState;
            WindowStyle = WindowStyle.None;
            WindowState = WindowState.Maximized;
            _fullscreen = true;
        }
        else
        {
            WindowStyle = WindowStyle.SingleBorderWindow;
            WindowState = _beforeFull;
            _fullscreen = false;
        }
    }

    void ShowKeys(object s, RoutedEventArgs e) =>
        MessageBox.Show(
            "Ctrl+N New tab   Ctrl+O Open (new tab)   Ctrl+W Close tab\n" +
            "Ctrl+P Print   Ctrl+F Find   Ctrl+H Replace   F3 Find next\n" +
            "Ctrl+A Select column   Ctrl+Z Undo   Ctrl+Y Redo\n" +
            "Ctrl+Shift+K Checkboxes on/off   Ctrl+Shift+V Paste plain\n" +
            "F5 Date/time   F11 Full screen   Ctrl+Shift+N New window",
            "Keyboard shortcuts");

    void ShowAbout(object s, RoutedEventArgs e) =>
        MessageBox.Show(
            "ColumnNotes 1.0.0\nA local notepad with columns and checklists.\nNo account, no cloud, no telemetry.\n\n" +
            (AppPaths.IsPortable ? "Portable mode — settings next to the EXE." : "Installed mode — settings in %APPDATA%\\ColumnNotes"),
            "About ColumnNotes");

    void OnPreviewKeyDown(object s, KeyEventArgs e)
    {
        var ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        var shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        if (e.Key == Key.F3) { FindNext(s, e); e.Handled = true; }
        else if (e.Key == Key.F5) { InsertDateTime(s, e); e.Handled = true; }
        else if (e.Key == Key.F11) { ToggleFullScreen(s, e); e.Handled = true; }
        else if (ctrl && shift && e.Key == Key.N) { NewWindow(s, e); e.Handled = true; }
        else if (ctrl && shift && e.Key == Key.S) { SaveDocAs(s, e); e.Handled = true; }
        else if (ctrl && shift && e.Key == Key.K) { ConvertToChecks(s, e); e.Handled = true; }
        else if (ctrl && shift && e.Key == Key.V) { PastePlain(s, e); e.Handled = true; }
        else if (ctrl && e.Key == Key.A) { SelectAllColumn(s, e); e.Handled = true; }
        else if (ctrl && e.Key == Key.C && _tab.SelectedIds.Count > 1) { CopyColumnIfNeeded(); e.Handled = true; }
        else if (ctrl && e.Key == Key.W) { CloseActiveTab(s, e); e.Handled = true; }
        else if (ctrl && e.Key == Key.N) { NewDoc(s, e); e.Handled = true; }
        else if (ctrl && e.Key == Key.O) { OpenDoc(s, e); e.Handled = true; }
        else if (ctrl && e.Key == Key.S) { SaveDoc(s, e); e.Handled = true; }
        else if (ctrl && e.Key == Key.P) { PrintDoc(s, e); e.Handled = true; }
        else if (ctrl && e.Key == Key.F) { ShowFind(s, e); e.Handled = true; }
        else if (ctrl && e.Key == Key.H) { ShowReplace(s, e); e.Handled = true; }
        else if (ctrl && e.Key == Key.Z) { Undo(s, e); e.Handled = true; }
        else if (ctrl && e.Key == Key.Y) { Redo(s, e); e.Handled = true; }
        else if (ctrl && (e.Key == Key.OemPlus || e.Key == Key.Add)) { ZoomIn(s, e); e.Handled = true; }
        else if (ctrl && (e.Key == Key.OemMinus || e.Key == Key.Subtract)) { ZoomOut(s, e); e.Handled = true; }
        else if (ctrl && e.Key == Key.D0) { ZoomReset(s, e); e.Handled = true; }
    }

    void OnDragOver(object s, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    void OnDrop(object s, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] files) return;
        foreach (var f in files) if (File.Exists(f)) OpenPath(f);
    }

    void OnClosing(object s, System.ComponentModel.CancelEventArgs e)
    {
        foreach (var tab in _tabs.ToList())
        {
            if (!tab.Dirty) continue;
            SwitchTab(tab);
            if (!ConfirmDiscard())
            {
                e.Cancel = true;
                return;
            }
            tab.Dirty = false;
        }
        SettingsService.Save();
    }

    bool ConfirmDiscard()
    {
        if (!_dirty) return true;
        var r = MessageBox.Show("Save changes?", "ColumnNotes", MessageBoxButton.YesNoCancel);
        if (r == MessageBoxResult.Cancel) return false;
        if (r == MessageBoxResult.Yes) Save(false, "cnotes");
        return true;
    }
}
