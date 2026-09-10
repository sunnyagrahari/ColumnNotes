using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using ColumnNotes.Models;
using ColumnNotes.Services;
using Microsoft.Win32;

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

    public MainWindow()
    {
        InitializeComponent();
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
        var doc = NoteDocument.Parse(File.ReadAllText(path, Encoding.UTF8), Path.GetFileNameWithoutExtension(path));
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
            var name = tab.Path != null ? Path.GetFileName(tab.Path) : tab.Doc.Title + ".cnotes";
            var close = new Button
            {
                Content = "×",
                Width = 22,
                Padding = new Thickness(0),
                BorderThickness = new Thickness(0),
                Background = Brushes.Transparent,
                Foreground = (Brush)Resources["InkBrush"],
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
                Margin = new Thickness(10, 6, 8, 6),
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = (Brush)Resources["InkBrush"]
            };
            var row = new DockPanel
            {
                LastChildFill = true,
                Tag = tab,
                Background = tab == _tab ? (Brush)Resources["PaperBrush"] : (Brush)Resources["ChromeBrush"],
                Cursor = Cursors.Hand
            };
            DockPanel.SetDock(close, Dock.Right);
            row.Children.Add(close);
            row.Children.Add(label);
            row.MouseLeftButtonUp += (_, _) => SwitchTab(tab);
            TabStrip.Children.Add(row);
        }
        var plus = new Button
        {
            Content = "+",
            Width = 32,
            BorderThickness = new Thickness(0),
            Background = (Brush)Resources["ChromeBrush"],
            Foreground = (Brush)Resources["InkBrush"],
            ToolTip = "New tab"
        };
        plus.Click += NewDoc;
        TabStrip.Children.Add(plus);
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

            var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            var stack = new StackPanel { Margin = new Thickness(0) };
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
                    Margin = new Thickness(0, 0, 0, 4)
                };
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
            FontFamily = new FontFamily("Consolas"),
            FontSize = 12,
            Padding = new Thickness(8, 4, 4, 4)
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
        var add = new Button { Content = "+ section", Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(0, 2, 8, 2) };
        add.Click += (_, _) =>
        {
            _activeColumn = columnIndex;
            InsertSection(this, new RoutedEventArgs());
        };
        var row = new DockPanel { LastChildFill = true, Background = (Brush)Resources["ChromeBrush"] };
        DockPanel.SetDock(add, Dock.Right);
        row.Children.Add(add);
        row.Children.Add(box);
        return row;
    }

    FrameworkElement BuildSectionHeader(int columnIndex, NoteSection section)
    {
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
            var swatch = new Button
            {
                Width = 12,
                Height = 12,
                Margin = new Thickness(2, 0, 0, 0),
                Background = SectionBrush(c),
                BorderThickness = new Thickness(section.Color == c ? 2 : 1),
                Padding = new Thickness(0),
                Tag = c
            };
            swatch.Click += (_, _) =>
            {
                section.Color = c;
                RebuildBoard();
                MarkDirty();
            };
            colors.Children.Add(swatch);
        }
        var row = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(colors, Dock.Right);
        row.Children.Add(colors);
        row.Children.Add(title);
        return row;
    }

    static Brush SectionBrush(string color)
    {
        return color switch
        {
            "sage" => new SolidColorBrush(Color.FromArgb(48, 39, 103, 73)),
            "ochre" => new SolidColorBrush(Color.FromArgb(48, 146, 64, 14)),
            "slate" => new SolidColorBrush(Color.FromArgb(48, 43, 76, 126)),
            "rose" => new SolidColorBrush(Color.FromArgb(48, 155, 44, 44)),
            "mist" => new SolidColorBrush(Color.FromArgb(48, 74, 85, 104)),
            _ => Brushes.Transparent
        };
    }

    FrameworkElement BuildBlock(NoteBlock block, int columnIndex)
    {
        var row = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 1, 0, 1), Tag = block };
        var rtb = new RichTextBox
        {
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            Document = block.ToFlowDocument(),
            FontFamily = new FontFamily(SettingsService.Current.FontFamily),
            FontSize = SettingsService.Current.FontSize * SettingsService.Current.Zoom / 100.0,
            AcceptsReturn = true,
            VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
            Tag = block
        };
        rtb.GotFocus += (_, _) => _activeColumn = columnIndex;
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
        if (_tab.SelectedIds.Contains(block.Id))
            row.Background = new SolidColorBrush(Color.FromArgb(40, 61, 107, 90));

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
        return row;
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
        var name = _path != null ? Path.GetFileName(_path) : _doc.Title + ".cnotes";
        Title = $"{(_dirty ? "*" : "")}{name} — ColumnNotes";
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
            _doc.Title = Path.GetFileNameWithoutExtension(path);
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
        _tab.SelectedIds.Clear();
        foreach (var b in col.Blocks) _tab.SelectedIds.Add(b.Id);
        RebuildBoard();
        var colBoxes = _columnBoxes.Where(rtb => rtb.Tag is NoteBlock block && col.Blocks.Contains(block)).ToList();
        if (colBoxes.Count > 0)
        {
            colBoxes[0].Focus();
            colBoxes[0].SelectAll();
            try
            {
                var text = string.Join("\n", col.Blocks.Select(b =>
                    b.Type == "check" ? $"{(b.IsChecked == true ? "[x]" : "[ ]")} {b.PlainText}" : b.PlainText));
                Clipboard.SetText(text);
            }
            catch { /* ignore */ }
        }
        Status($"Selected column “{col.Name}”");
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
        foreach (var (name, hex) in palette)
        {
            var item = new MenuItem { Header = name };
            var color = (Color)ColorConverter.ConvertFromString(hex)!;
            item.Click += (_, _) =>
            {
                if (Keyboard.FocusedElement is RichTextBox rtb)
                    rtb.Selection.ApplyPropertyValue(TextElement.ForegroundProperty, new SolidColorBrush(color));
            };
            menu.Items.Add(item);
        }
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

    void ColumnsMenu(object s, RoutedEventArgs e) => ChangeColumns(_doc.Columns.Count == 1 ? 2 : 1);

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
