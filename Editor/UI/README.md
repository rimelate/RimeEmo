# UIのデザインを変更する

画面はUI Toolkitで構築し、Googleの[Material Design 3](https://m3.material.io/)（ダークテーマ）の考え方に合わせています。3Dプレビューの描画部分だけIMGUIContainerを使います。

## 画面構成

- 上部バー（`utility-toolbar`）：ブランド名、Avatar、保存、アバターへの適用（`apply`）、使い方（？）、言語。適用ボタンは未適用で`.re-button--tonal`、未反映の変更があると`.re-button--filled`、適用済みで`.re-apply--done`になります。未反映の間はGestureのレール項目に`.re-rail-badge`の印を表示します。
- ナビゲーションレール（`nav-rail`）：作業の順に「表情を作る」「動きを作る」「Gesture」、下端に「設定」。選んだページだけを表示します。
- ページ：`editor-page`（素材・プレビュー・編集欄）、`gesture-page`（Gesture割り当てとFX適用）、`settings-page`（アバター設定・顔メッシュ・部位・整理）。
- Gesture画面は `RimeEmoGestureWindow.uxml` を `RimeEmoGesturePanel.cs` がメイン画面と単独ウィンドウの両方に組み込みます。

## ファイル

- `RimeEmoWindow.uxml`: 画面構造と操作要素の名前。
- `RimeEmoGestureWindow.uxml`: Gestureページの構造。
- `RimeEmoTheme.uss`: Material 3の色の役割（`--md-*`）・形・ボタン・チップ・タイムラインの見た目。
- `RimeEmoCustom.uss`: 最後に読み込むカスタマイズ用スタイル。独自デザインはここへ追記します。
- `../RimeEmoToolkitUI.cs`: ページ切り替え、共通操作とデータの接続。
- `../RimeEmoStaticToolkitUI.cs`: 静的編集の検索・フィルターチップ・スライダー。
- `../RimeEmoTimelineElement.cs`: キーの選択・ドラッグ・ズームなど。
- `../RimeEmoIcons.cs`: 操作名とアイコンの対応。

## 色を変える

色は `--md-*` 変数（Material 3の色の役割）で管理しています。ブランド色を変える場合は `RimeEmoCustom.uss` で上書きします。

```css
.re-app {
    --md-primary: rgb(208, 188, 255);
    --md-on-primary: rgb(56, 30, 114);
    --md-secondary-container: rgb(74, 68, 88);
    --md-on-secondary-container: rgb(232, 222, 248);
}
```

## ボタンの強調段階

Material 3と同じく、強調の強い順に使い分けます。

| class | 用途 |
| --- | --- |
| `.re-button--filled` | 画面で一番大事な操作（保存、再生、再生成して適用） |
| `.re-button--tonal` | よく使う操作（新しい表情、分割、表情を編集） |
| `.re-button--text` | 補助的な操作（別名で保存、全体表示） |
| `.re-button--icon` | アイコンだけの操作（停止、前後のキー、複製、削除） |

旧クラス名 `.re-button--primary`・`.re-button--quiet`・`.re-toolbar`・`.re-heading`・`.re-panel` も同じ見た目で使えます。選択状態は `.is-active`、マウスオーバー・押下・無効状態は `:hover`・`:active`・`:disabled` で変更します。特定のボタンだけを変える場合は `#play` などUXMLのnameをセレクターに使います。

ナビゲーションレールは `.re-rail-item`、選択中は `.is-active`（アイコンが塗りつぶしに変わります）。部位の絞り込みはフィルターチップ `.re-chip` で、選択中は `.is-active` とチェックマークで表します。

## アイコン

アイコンは [Material Symbols Rounded](https://fonts.google.com/icons)（Apache License 2.0）です。可変フォントから FILL 0 / FILL 1、wght 400、GRAD 0、opsz 24 の静的フォントを作り、使うアイコンだけに絞ったサブセット（各10KB程度）を `Icons/` に同梱しています。出典とライセンスは `Icons/ATTRIBUTION.txt` と `Icons/LICENSE.txt` にあります。配布時もライセンスを同梱してください。

アイコンを追加する場合は、`RimeEmoIcons.cs` の `Symbols` にコードポイント、`Glyphs` にボタン名との対応を追加し、サブセットフォントにそのアイコンを含めて作り直します。アイコンの色はボタンから継承します。

文字のフォントは、各ウィンドウのルートに `RimeEmoFonts` が割り当てます。Unity標準の日本語フォールバックフォントはPlayモードの後などに文字が抜けることがあるため、OSの日本語フォント（Windowsは Yu Gothic UI、macOSはヒラギノ角ゴシック）から専用のフォントを作り、Playモードの開始・終了のたびに作り直します。日本語フォントが見つからない環境ではUnity標準のフォントのままです。

## 注意

C#はnameで操作を接続しています。レイアウトやclassは変更できますが、既存要素のnameと要素の型は維持してください。タイムラインの位置・サイズは時間座標の計算に使うためC#側で管理し、配色や境界線はUSSで変更します。USSを変更して表示が更新されない場合はウィンドウを開き直してください。

素材一覧とプレビュー、タイムラインと詳細の境界線をドラッグすると幅を調整できます。ダブルクリックで初期比率に戻ります。境界線にフォーカスして左右キーでも調整できます。

素材一覧・プレビューと、下の編集欄（Shape Keyの一覧、または動きのタイムライン）の境界線をドラッグすると高さを調整できます。表情と動きで別々に記憶し、ダブルクリックで初期値に戻ります。境界線にフォーカスして上下キーでも調整できます。タイムラインは5つの部位がすべて見える高さより低くはならず、広げた分は右の詳細欄に使われます。ウィンドウを小さくしたときは素材とプレビューの高さを確保するよう一時的に縮み、ウィンドウを戻すと元の高さに戻ります。
