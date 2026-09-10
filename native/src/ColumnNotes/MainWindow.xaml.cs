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
    NoteDocument _doc = NoteDocument.Welcome();
    string? _path;
    bool _dirty;
    readonly Stack<string> _undo = new();
    readonly Stack<string> _redo = new();
    WindowState _beforeFull;
    bool _fullscreen;
    int _activeColumn;
    readonly List<List<FrameworkElement>> _blockViews = new();

    public MainWindow()
    {
        InitializeComponent();
        ApplySettingsChrome();
        if (File.Exists(AppPaths.AutosavePath) && SettingsService.Current.RecentFiles.Count > 0)
        {
            try { _doc = NoteDocument.Parse(File.ReadAllText(AppPaths.AutosavePath), "Untitled"); }
            catch { _doc = NoteDocument.Welcome(); }
        }
        RebuildBoard();
        RebuildRecent();
        UpdateTitle();
        UpdateStatus();
    }

    public void OpenPath(string path)
    {
        CaptureBoard();
        _doc = NoteDocument.Parse(File.ReadAllText(path, Encoding.UTF8), Path.GetFileNameWithoutExtension(path));
        _path = path;
        _dirty = false;
        SettingsService.PushRecent(path);
        RebuildRecent();
        RebuildBoard();
        UpdateTitle();
        Status("Opened " + path);
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
        var n = Math.Max(1, _doc.Columns.Count);
        for (var i = 0; i < n; i++)
        {
            Board.ColumnDefinitions.Add(new ColumnDefinition());
            var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            var stack = new StackPanel { Margin = new Thickness(8) };
            if (n > 1)
            {
                stack.Children.Add(new TextBlock
                {
                    Text = $"Column {i + 1}",
                    FontFamily = new FontFamily("Consolas"),
                    FontSize = 11,
                    Opacity = 0.65,
                    Margin = new Thickness(0, 0, 0, 6)
                });
            }
            var views = new List<FrameworkElement>();
            var colIndex = i;
            foreach (var block in _doc.Columns[i].Blocks)
            {
                var row = BuildBlock(block, colIndex);
                stack.Children.Add(row);
                views.Add(row);
            }
            scroll.Content = stack;
            Grid.SetColumn(scroll, i);
            Board.Children.Add(scroll);
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
        TextOptions.SetTextFormattingMode(rtb, TextFormattingMode.Display);
        if (!SettingsService.Current.WordWrap)
            rtb.Document.PageWidth = 4000;

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
        _dirty = true;
        UpdateTitle();
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
        if (!ConfirmDiscard()) return;
        Snapshot();
        _doc = NoteDocument.Empty();
        _path = null;
        _dirty = false;
        RebuildBoard();
        UpdateTitle();
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
        col.Blocks = col.Blocks.SelectMany(SplitToChecks).ToList();
        RebuildBoard();
        MarkDirty();
        Status("Converted to checkboxes");
    }

    static IEnumerable<NoteBlock> SplitToChecks(NoteBlock b)
    {
        if (b.Type == "check") { yield return b; yield break; }
        var lines = (b.PlainText ?? "").Replace("\r\n", "\n").Split('\n');
        if (lines.Length == 0) yield return NoteBlock.Check("", false);
        foreach (var line in lines) yield return NoteBlock.Check(line, false);
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
            "Ctrl+N New   Ctrl+O Open   Ctrl+S Save   Ctrl+Shift+S Save As\n" +
            "Ctrl+P Print   Ctrl+F Find   Ctrl+H Replace   F3 Find next\n" +
            "Ctrl+Z Undo   Ctrl+Y Redo   Ctrl+Shift+K Checkboxes\n" +
            "Ctrl+Shift+V Paste plain   F5 Date/time   F11 Full screen\n" +
            "Ctrl+Shift+N New window",
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
        if (!ConfirmDiscard()) e.Cancel = true;
        else SettingsService.Save();
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
