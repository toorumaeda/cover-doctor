using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using CoverDoctor.Core;

namespace CoverDoctor.App;

// 画面（Mac 版 App.swift の ContentView / ResultView / NoticeView に対応）。
// 文言を実行時に切り替えるため、XAML ではなくコードで組み立てる。状態は AppModel が持つ。

public static class Program
{
    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    static extern bool AttachConsole(int processId);

    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Length > 0)
        {
            // 引数付きで起動したときは、画面を出さずにコマンド版として動く（例: CoverDoctor.exe diagnose）。
            // 結果は、起動した PowerShell / コマンドプロンプトに表示する。
            AttachConsole(-1);
            var stdout = new StreamWriter(Console.OpenStandardOutput(), new System.Text.UTF8Encoding(false)) { AutoFlush = true };
            return Cli.Run(args, stdout.WriteLine);
        }
        var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
        var win = new MainWindow();
        app.MainWindow = win;
        win.Show();
        return app.Run();
    }
}

public sealed class MainWindow : Window
{
    readonly AppModel model;
    readonly TextBlock deviceText = new() { VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
    readonly Ellipse deviceDot = new() { Width = 10, Height = 10, Margin = new Thickness(0, 0, 8, 0) };
    readonly ContentControl content = new();
    readonly StackPanel footerLeft = new() { Orientation = Orientation.Horizontal };
    static readonly Brush Secondary = new SolidColorBrush(Color.FromRgb(0x6b, 0x6b, 0x6b));
    static readonly Brush Green = new SolidColorBrush(Color.FromRgb(0x1f, 0x9d, 0x55));
    static readonly Brush Orange = new SolidColorBrush(Color.FromRgb(0xd9, 0x6c, 0x00));

    public MainWindow()
    {
        model = new AppModel(a => Dispatcher.BeginInvoke(a));
        Title = L.T("Cover Doctor for Windows") + " " + Cli.AppVersion;
        Width = 760; Height = 780; MinWidth = 620; MinHeight = 660;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        FontFamily = new FontFamily(L.Japanese ? "Yu Gothic UI, Meiryo UI, Segoe UI" : "Segoe UI");
        FontSize = 14;
        try { using var st = typeof(MainWindow).Assembly.GetManifestResourceStream("AppIcon.png"); if (st != null) Icon = BitmapFrame.Create(st, BitmapCreateOptions.None, BitmapCacheOption.OnLoad); } catch { }

        var root = new DockPanel { Margin = new Thickness(20), LastChildFill = true };
        var header = new StackPanel();
        header.Children.Add(new TextBlock { Text = L.T("Cover Doctor for Windows"), FontSize = 28, FontWeight = FontWeights.Bold });
        header.Children.Add(new TextBlock { Text = L.T("Fixes the problem where the Music app on your iPhone shows another album's cover."), Foreground = Secondary, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 12) });
        var deviceRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
        deviceRow.Children.Add(deviceDot); deviceRow.Children.Add(deviceText);
        header.Children.Add(deviceRow);
        header.Children.Add(new Separator { Margin = new Thickness(0, 0, 0, 12) });
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);

        var footer = new DockPanel { Margin = new Thickness(0, 12, 0, 0) };
        var unofficial = new TextBlock { Text = L.T("Unofficial · use at your own risk"), FontSize = 12, Foreground = Secondary, VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(unofficial, Dock.Right);
        footer.Children.Add(unofficial); footer.Children.Add(footerLeft);
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);
        root.Children.Add(content);
        Content = root;

        model.PropertyChanged += (_, _) => Render();
        Render();
        Loaded += async (_, _) =>
        {
            model.StartPolling();
            var selfTestOut = Environment.GetEnvironmentVariable("COVERDOCTOR_SELFTEST_OUT");
            if (!string.IsNullOrEmpty(selfTestOut))
            {
                var result = await model.SelfTest();
                File.WriteAllText(selfTestOut, result);
                Application.Current.Shutdown(result.StartsWith("PASS") ? 0 : 1);
                return;
            }
            if (!Settings.AcceptedNotice)
            {
                new NoticeWindow { Owner = this }.ShowDialog();
                Settings.AcceptedNotice = true;
            }
        };
    }

    // ---- 部品 ----

    static TextBlock Text(string s, double size = 14, Brush? color = null, bool bold = false) =>
        new() { Text = s, FontSize = size, Foreground = color ?? SystemColors.ControlTextBrush, FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) };

    static Button Btn(string label, Action onClick, bool primary = false)
    {
        var b = new Button { Content = label, Padding = new Thickness(16, primary ? 8 : 5, 16, primary ? 8 : 5), Margin = new Thickness(0, 0, 8, 0), MinWidth = primary ? 140 : 0, HorizontalAlignment = HorizontalAlignment.Left };
        if (primary) { b.Background = new SolidColorBrush(Color.FromRgb(0x00, 0x67, 0xc0)); b.Foreground = Brushes.White; b.FontWeight = FontWeights.SemiBold; }
        b.Click += (_, _) => onClick();
        return b;
    }

    static StackPanel Row(params UIElement[] items)
    {
        var p = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        foreach (var i in items) p.Children.Add(i);
        return p;
    }

    static UIElement Link(string label, Action onClick)
    {
        var h = new Hyperlink(new Run(label));
        h.Click += (_, _) => onClick();
        return new TextBlock(h) { VerticalAlignment = VerticalAlignment.Center };
    }

    // ---- 確認 ----

    void ConfirmFix()
    {
        int n = model.FixCount;
        var msg = L.Plural(n, "This replaces {0} cover image (an album or a playlist) on your iPhone. The original image is saved on this PC, so you can undo this later.",
                              "This replaces {0} cover images (albums and playlists) on your iPhone. The original images are saved on this PC, so you can undo this later.");
        if (Ask(L.T("Fix the covers?"), msg, L.T("Fix"))) model.Fix();
    }

    void ConfirmRestore()
    {
        if (Ask(L.T("Undo the changes?"), L.T("This puts the covers replaced by this app back to how they were before."), L.T("Undo"))) model.Restore();
    }

    bool Ask(string title, string message, string ok)
    {
        var w = new Window { Title = title, Owner = this, SizeToContent = SizeToContent.WidthAndHeight, ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner, FontFamily = FontFamily, FontSize = FontSize };
        bool result = false;
        var p = new StackPanel { Margin = new Thickness(24), MaxWidth = 440 };
        p.Children.Add(Text(title, 16, bold: true));
        p.Children.Add(Text(message));
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
        var cancel = Btn(L.T("Cancel"), () => w.Close()); cancel.IsCancel = true;
        var yes = Btn(ok, () => { result = true; w.Close(); }, primary: true); yes.MinWidth = 0;
        buttons.Children.Add(cancel); buttons.Children.Add(yes);
        p.Children.Add(buttons);
        w.Content = p;
        w.ShowDialog();
        return result;
    }

    // ---- 表示 ----

    void Render()
    {
        var d = model.Device;
        deviceDot.Fill = d == null ? Brushes.Gray : Green;
        deviceText.Text = d != null ? L.T("Connected to {0} (iOS {1})", d.Name, d.Version)
            : model.MuxdMissing ? L.T("The helper tool could not reach Apple Mobile Device Support. Install iTunes or the Apple Devices app, then connect your iPhone again.")
            : L.T("Connect your iPhone with a USB cable and unlock it");
        deviceText.Foreground = d == null ? Secondary : SystemColors.ControlTextBrush;

        footerLeft.Children.Clear();
        if (model.HasUndo && !model.Busy && model.Phase != AppModel.PhaseKind.Fixed)
            footerLeft.Children.Add(Link(L.T("Undo what this app fixed…"), ConfirmRestore));

        var p = new StackPanel();
        if (model.ErrorMessage is string err)
            p.Children.Add(new TextBox { Text = "⚠ " + err, Foreground = Orange, IsReadOnly = true, BorderThickness = new Thickness(0), Background = Brushes.Transparent, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) });
        switch (model.Phase)
        {
            case AppModel.PhaseKind.Idle:
            {
                p.Children.Add(Text(L.T("Start with a diagnosis. It only reads data and does not change your iPhone."), color: Secondary));
                var b = Btn(L.T("Diagnose"), () => model.Diagnose(), primary: true); b.IsEnabled = d != null;
                p.Children.Add(b);
                break;
            }
            case AppModel.PhaseKind.Diagnosing:
            case AppModel.PhaseKind.Fixing:
            {
                p.Children.Add(Text(model.Phase == AppModel.PhaseKind.Diagnosing ? L.T("Diagnosing…") : L.T("Working…"), 16, bold: true));
                var pr = model.Progress;
                var bar = new ProgressBar { Height = 8, Margin = new Thickness(0, 0, 0, 6), IsIndeterminate = pr.Total == 0, Maximum = Math.Max(1, pr.Total), Value = pr.Done };
                p.Children.Add(bar);
                p.Children.Add(Text(pr.Total > 0 ? $"{pr.Done} / {pr.Total}  {pr.Message}" : pr.Message, 12, Secondary));
                if (model.Phase == AppModel.PhaseKind.Diagnosing) p.Children.Add(Btn(L.T("Cancel"), model.Cancel));
                p.Children.Add(Text(L.T("The first run extracts artwork from your songs and can take a few minutes. Keep your iPhone connected."), 12, Secondary));
                if (model.Phase == AppModel.PhaseKind.Diagnosing)
                    p.Children.Add(Text(L.T("If the connection is lost, run the diagnosis again: albums already checked are not copied from the iPhone again."), 12, Secondary));
                break;
            }
            case AppModel.PhaseKind.Diagnosed when model.Plan != null:
                content.Content = ResultView(p, model.Plan);
                return;
            case AppModel.PhaseKind.Fixed:
            {
                var r = model.LastFix ?? new FixResult();
                p.Children.Add(Text("✔ " + L.Plural(r.Ok, "Fixed {0} cover", "Fixed {0} covers"), 18, Green, bold: true));
                if (r.Failed.Count > 0) p.Children.Add(Text(L.T("Could not fix: {0}", r.Failed.Count), color: Orange));
                if (r.ChangedSinceDiagnosis.Count > 0) p.Children.Add(Text(L.T("Changed on the iPhone after the diagnosis (not written; diagnose again): {0}", r.ChangedSinceDiagnosis.Count), color: Orange));
                p.Children.Add(Text(L.T("Fully quit the Music app (swipe it up in the app switcher) and open it again.")));
                p.Children.Add(Row(Btn(L.T("Diagnose Again"), () => model.Diagnose(reuse: true)), Btn(L.T("Undo…"), ConfirmRestore)));
                break;
            }
            case AppModel.PhaseKind.Restored:
            {
                p.Children.Add(Text("↩ " + L.Plural(model.RestoredCount, "Restored {0} cover", "Restored {0} covers"), 18, bold: true));
                var b = Btn(L.T("Diagnose"), () => model.Diagnose()); b.IsEnabled = d != null;
                p.Children.Add(b);
                break;
            }
        }
        content.Content = new ScrollViewer { Content = p, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }

    UIElement ResultView(StackPanel top, Plan plan)
    {
        var wrong = plan.Entries.Where(e => e.Status == "wrong").ToList();
        int same = plan.Entries.Count(e => e.Status == "same"), none = plan.Entries.Count(e => e.Status == "noEmbeddedArt"), custom = plan.Entries.Count(e => e.Status == "custom");
        var counters = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        UIElement Counter(string title, int n, Brush color)
        {
            var s = new StackPanel { Margin = new Thickness(0, 0, 28, 0) };
            s.Children.Add(new TextBlock { Text = n.ToString(), FontSize = 34, FontWeight = FontWeights.Bold, Foreground = color });
            s.Children.Add(new TextBlock { Text = title, FontSize = 12, Foreground = Secondary });
            return s;
        }
        counters.Children.Add(Counter(L.T("Correct"), same, Green));
        counters.Children.Add(Counter(L.T("Fixable"), wrong.Count, wrong.Count == 0 ? Secondary : Orange));
        counters.Children.Add(Counter(L.T("Not fixable"), none, Secondary));
        top.Children.Add(counters);
        if (plan.LibraryAlbumCount is int total && plan.NotDiagnosedCount is int skipped && skipped > 0)
        {
            top.Children.Add(Text(L.T("Albums in the library: {0} · checked: {1} · no cover record: {2}", total, total - skipped, skipped), 12));
            top.Children.Add(Text(L.T("Albums with no cover record on the iPhone cannot be checked or fixed by this app."), 12, Secondary));
        }
        if (none > 0) top.Children.Add(Text(L.T("“Not fixable” albums have no cover image inside their song files."), 12, Secondary));
        if (custom > 0) top.Children.Add(Text(L.T("Playlist covers that may be custom (left alone): {0}", custom), 12, Secondary));

        var dock = new DockPanel();
        DockPanel.SetDock(top, Dock.Top);
        dock.Children.Add(top);
        if (wrong.Count == 0)
        {
            top.Children.Add(Text("✔ " + L.T("Nothing to fix"), color: Green));
            top.Children.Add(Btn(L.T("Diagnose Again"), () => model.Diagnose(reuse: true)));
            return new ScrollViewer { Content = dock, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        }
        top.Children.Add(Row(Btn(L.Plural(wrong.Count, "Fix {0} cover", "Fix {0} covers"), ConfirmFix, primary: true), Btn(L.T("Diagnose Again"), () => model.Diagnose(reuse: true))));
        top.Children.Add(Text(L.T("Left: the cover on your iPhone now. Right: the image from the song file (how it will look after the fix)."), 12, Secondary));

        string work = model.Work!;
        var list = new ListBox { HorizontalContentAlignment = HorizontalAlignment.Stretch, MinHeight = 200 };
        VirtualizingPanel.SetIsVirtualizing(list, true);
        foreach (var e in wrong)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(2) };
            row.Children.Add(Thumb(System.IO.Path.Combine(work, "current", e.Token + ".img")));
            row.Children.Add(new TextBlock { Text = "→", Foreground = Secondary, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 8, 0) });
            row.Children.Add(Thumb(System.IO.Path.Combine(work, "fresh", e.Token + ".jpg")));
            var label = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
            label.Children.Add(new TextBlock { Text = e.Album, TextWrapping = TextWrapping.Wrap, MaxWidth = 460 });
            if (e.IsPlaylist) label.Children.Add(new TextBlock { Text = L.T("Playlist"), FontSize = 11, Foreground = Secondary });
            row.Children.Add(label);
            list.Items.Add(row);
        }
        dock.Children.Add(list);
        return dock;
    }

    static UIElement Thumb(string path)
    {
        var frame = new Border { Width = 52, Height = 52, CornerRadius = new CornerRadius(4), Background = new SolidColorBrush(Color.FromRgb(0xe6, 0xe6, 0xe6)), ClipToBounds = true };
        try
        {
            if (File.Exists(path))
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;       // ファイルを開いたままにしない（次の診断で上書きできるように）
                bmp.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
                bmp.DecodePixelWidth = 104;
                bmp.UriSource = new Uri(path);
                bmp.EndInit();
                bmp.Freeze();
                frame.Child = new Image { Source = bmp, Stretch = Stretch.UniformToFill };
            }
        }
        catch { }   // 壊れた画像は灰色の枠のまま
        return frame;
    }
}

sealed class NoticeWindow : Window
{
    public NoticeWindow()
    {
        Title = L.T("Before you start");
        SizeToContent = SizeToContent.Height; Width = 520; ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FontFamily = new FontFamily(L.Japanese ? "Yu Gothic UI, Meiryo UI, Segoe UI" : "Segoe UI"); FontSize = 14;
        var p = new StackPanel { Margin = new Thickness(24) };
        TextBlock T(string s, double size = 14, bool bold = false) => new() { Text = s, FontSize = size, FontWeight = bold ? FontWeights.Bold : FontWeights.Normal, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) };
        p.Children.Add(T(L.T("Before you start"), 20, true));
        foreach (var s in new[] {
            "This app is not an official Apple method. It replaces the cover image files that the Music app on your iPhone keeps internally with the correct images found in your song files, which fixes what is displayed.",
            "• It does not change the Music database itself, your song files, or the music library on your PC.",
            "• The images it replaces are saved on this PC, so you can undo the change later.",
            "• It may stop working depending on the iOS version.",
            "• Syncing again with iTunes, the Apple Devices app or similar tools can bring the wrong covers back. If that happens, diagnose and fix again.",
            "• Use it at your own risk." }) p.Children.Add(T(L.T(s)));
        var ok = new Button { Content = L.T("I Understand"), IsDefault = true, Padding = new Thickness(16, 6, 16, 6), HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
        ok.Click += (_, _) => Close();
        p.Children.Add(ok);
        Content = p;
    }
}

/// 利用者ごとの設定（%APPDATA%\CoverRepair\settings.json）。今は「はじめに」を読んだかどうかだけ。
static class Settings
{
    static string PathName => System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CoverRepair", "settings.json");

    public static bool AcceptedNotice
    {
        get
        {
            try { return JsonDocument.Parse(File.ReadAllText(PathName)).RootElement.GetProperty("acceptedNotice").GetBoolean(); }
            catch { return false; }
        }
        set
        {
            try
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(PathName)!);
                File.WriteAllText(PathName, JsonSerializer.Serialize(new { acceptedNotice = value }));
            }
            catch { }
        }
    }
}
