using System.Globalization;

namespace CoverDoctor.Core;

/// 画面とメッセージの文言。英語が基準（キー = 英語の文言）。日本語は Mac 版の Resources/ja.lproj/Localizable.strings の訳を使い、
/// 「Mac」「Finder」を Windows の言い方に置き換えている。OS の表示言語が日本語なら日本語、それ以外は英語。
public static class L
{
    public static bool Japanese { get; set; } = Detect();

    static bool Detect()
    {
        var forced = Environment.GetEnvironmentVariable("COVERDOCTOR_LANG");
        if (!string.IsNullOrEmpty(forced)) return forced.StartsWith("ja", StringComparison.OrdinalIgnoreCase);
        return CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ja";
    }

    /// 文言を引く。args があれば string.Format で埋め込む（{0}, {1}…）。
    public static string T(string english, params object[] args)
    {
        var s = Japanese && Ja.TryGetValue(english, out var ja) ? ja : english;
        return args.Length == 0 ? s : string.Format(CultureInfo.CurrentCulture, s, args);
    }

    /// 英語の単数・複数（日本語は区別しない）。
    public static string Plural(int n, string one, string other) => T(n == 1 ? one : other, n);

    static readonly Dictionary<string, string> Ja = new()
    {
        ["Preparing…"] = "準備中…",
        ["Restoring…"] = "元に戻しています…",
        ["Fix the covers?"] = "ジャケットを直しますか？",
        ["Fix"] = "直す",
        ["Cancel"] = "キャンセル",
        ["This replaces {0} cover image (an album or a playlist) on your iPhone. The original image is saved on this PC, so you can undo this later."] =
            "{0} 件のジャケット画像（アルバムとプレイリスト）を、iPhone 内で置き換えます。置き換える前の画像はこの PC に保存され、あとで元に戻せます。",
        ["This replaces {0} cover images (albums and playlists) on your iPhone. The original images are saved on this PC, so you can undo this later."] =
            "{0} 件のジャケット画像（アルバムとプレイリスト）を、iPhone 内で置き換えます。置き換える前の画像はこの PC に保存され、あとで元に戻せます。",
        ["Undo the changes?"] = "元に戻しますか？",
        ["Undo"] = "元に戻す",
        ["This puts the covers replaced by this app back to how they were before."] = "このアプリで置き換えたジャケットを、置き換える前の状態に戻します。",
        ["Cover Doctor for Windows"] = "Cover Doctor for Windows",
        ["Fixes the problem where the Music app on your iPhone shows another album's cover."] = "iPhone のミュージックで、別のアルバムのジャケットが表示される問題を直します。",
        ["Connected to {0} (iOS {1})"] = "{0}（iOS {1}）に接続中",
        ["Connect your iPhone with a USB cable and unlock it"] = "iPhone を USB でつないで、ロックを解除してください",
        ["Start with a diagnosis. It only reads data and does not change your iPhone."] = "まず診断します。診断は読み取りだけで、iPhone は変更しません。",
        ["Diagnose"] = "診断する",
        ["Diagnosing…"] = "診断中…",
        ["Working…"] = "処理中…",
        ["The first run extracts artwork from your songs and can take a few minutes. Keep your iPhone connected."] =
            "初回は曲を取り出すため、数分かかることがあります。iPhone をつないだままにしてください。",
        ["If the connection is lost, run the diagnosis again: albums already checked are not copied from the iPhone again."] =
            "途中で接続が切れたときは、もう一度診断してください。取り出し済みのアルバムは、iPhone から取り出し直しません。",
        ["Could not fix: {0}"] = "直せなかった件数: {0}",
        ["Fixed {0} cover"] = "{0} 件のジャケットを直しました",
        ["Fixed {0} covers"] = "{0} 件のジャケットを直しました",
        ["Changed on the iPhone after the diagnosis (not written; diagnose again): {0}"] = "診断のあとに iPhone 側で変わっていたもの（書き込んでいません。もう一度診断してください）: {0} 件",
        ["Fully quit the Music app (swipe it up in the app switcher) and open it again."] = "ミュージックアプリを完全に終了して（アプリスイッチャーで上にスワイプ）、開き直してください。",
        ["Diagnose Again"] = "もう一度診断する",
        ["Undo…"] = "元に戻す…",
        ["Restored {0} cover"] = "{0} 件を元に戻しました",
        ["Restored {0} covers"] = "{0} 件を元に戻しました",
        ["Undo what this app fixed…"] = "このアプリで直した分を元に戻す…",
        ["Unofficial · use at your own risk"] = "非公式・自己責任",
        ["Correct"] = "正しい",
        ["Fixable"] = "直せる",
        ["Not fixable"] = "直せない",
        ["“Not fixable” albums have no cover image inside their song files."] = "「直せない」は、曲ファイルにジャケット画像が入っていないアルバムです。",
        ["Nothing to fix"] = "直すものはありません",
        ["Fix {0} cover"] = "{0} 件を直す",
        ["Fix {0} covers"] = "{0} 件を直す",
        ["Playlist"] = "プレイリスト",
        ["Playlist covers that may be custom (left alone): {0}"] = "自分で設定したかもしれないプレイリストのカバー（そのままにしています）: {0} 件",
        ["Left: the cover on your iPhone now. Right: the image from the song file (how it will look after the fix)."] =
            "左が今の iPhone のジャケット、右が曲ファイルの画像（直したあとの姿）です。",
        ["Before you start"] = "はじめに",
        ["This app is not an official Apple method. It replaces the cover image files that the Music app on your iPhone keeps internally with the correct images found in your song files, which fixes what is displayed."] =
            "このアプリは、Apple が公式に認めた方法ではありません。iPhone のミュージックが内部に持つ「ジャケット画像のファイル」を、曲ファイルに入っている正しい画像で置き換えて、表示を直します。",
        ["• It does not change the Music database itself, your song files, or the music library on your PC."] =
            "• ミュージックのデータベース本体や、曲ファイル、PC のミュージックライブラリは変更しません。",
        ["• The images it replaces are saved on this PC, so you can undo the change later."] = "• 置き換える前の画像はこの PC に保存し、あとで元に戻せます。",
        ["• It may stop working depending on the iOS version."] = "• iOS のバージョンによっては、動かなくなる可能性があります。",
        ["• Syncing again with iTunes, the Apple Devices app or similar tools can bring the wrong covers back. If that happens, diagnose and fix again."] =
            "• 次に iTunes や Apple デバイス等で同期すると、また誤りが出ることがあります。そのときは、もう一度診断して直してください。",
        ["• Use it at your own risk."] = "• 使用は自己責任でお願いします。",
        ["I Understand"] = "了解して使う",
        ["Could not start the helper tool: {0}"] = "補助ツールを起動できません: {0}",
        ["The iPhone did not respond in time. Check the USB connection and try again."] = "iPhone から時間内に応答がありません。USB の接続を確かめて、もう一度お試しください。",
        ["No iPhone found. Connect it with a USB cable, unlock it, and choose “Trust This Computer”."] =
            "iPhone が見つかりません。USB で接続し、ロックを解除して「このコンピュータを信頼」を選んでください。",
        ["Reading the library information…"] = "ライブラリ情報を読み取り中…",
        ["Could not read the library information. Unlock your iPhone and try again. ({0})"] = "ライブラリ情報を取得できません。ロックを解除して、もう一度お試しください。（{0}）",
        ["Done"] = "完了",
        ["The iPhone you diagnosed is not connected."] = "診断したときの iPhone が接続されていません。",
        ["No iPhone found."] = "iPhone が見つかりません。",
        ["There is nothing to undo."] = "取り消せる履歴がありません。",
        ["Albums in the library: {0} · checked: {1} · no cover record: {2}"] = "ライブラリのアルバム: {0} 件 ・ 診断した: {1} 件 ・ 画像の記録なし: {2} 件",
        ["Albums with no cover record on the iPhone cannot be checked or fixed by this app."] = "iPhone に画像の記録がないアルバムは、このアプリでは診断も修復もできません。",
        ["The helper tool could not reach Apple Mobile Device Support. Install iTunes or the Apple Devices app, then connect your iPhone again."] =
            "補助ツールが Apple Mobile Device Support に接続できません。iTunes または「Apple デバイス」アプリを入れてから、iPhone をつなぎ直してください。",
    };
}
