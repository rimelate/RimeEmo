# RimeEmoのリリース

## 初回の公開設定

1. GitHubのリポジトリ設定で公開範囲をPublicにする。
2. `package.json`の`version`と`url`を確認する（初回は`0.1.0`）。
3. Releasesから`v0.1.0`というタグのReleaseを公開する。
4. Actionsの`Publish VPM release`が成功したら、次のURLをVCC / ALCOMに登録する。

```text
https://raw.githubusercontent.com/rimelate/RimeEmo/vpm/index.json
```

案内ページはSettings > PagesのSourceを`GitHub Actions`に設定し、
`Deploy install page`で公開します。URLは`https://rimelate.github.io/RimeEmo/`です。

## 次回以降

1. `package.json`の`version`とZIPの`url`を同時に更新する。
2. `CHANGELOG.md`を更新してコミットする。
3. バージョンに一致する`v0.1.1`等のReleaseを公開する。

Actionsは配布ZIPだけをReleaseに添付し、監査記録はActionsのArtifactに保存します。
`vpm`ブランチの`index.json`と`latest.json`を更新します。
公開に失敗した場合はActionsから同じタグを指定して再実行できます。
Releaseを削除した場合も、残っている公開済みタグを指定して再実行すると一覧を再構築できます。

プレリリースは`0.2.0-beta.1`のように記載し、GitHubのプレリリース設定も有効にしてください。
VPMの一覧には含まれますが、ツール内では安定版だけを通知します。

## 配布範囲

`release.py`で許可したツールのソース、UI、フォントとそのライセンス、ドキュメントだけを梱包します。
`Working`、`Generated`、`Backup~`、アバター、表情Clip、生成済みFX、プロジェクト固有設定は配布しません。
`Editor` / `Runtime`内に未確認の種類のファイルが追加された場合もビルドを停止します。
`package-audit.json`で配布ZIPに入る全ファイルを確認できます。

旧Assets版からの移行でVPMが削除するのはコードを含む`Editor`・`Runtime`だけです。
購入アバター由来の内容を含み得るローカルの作業データをアップロードする処理はありません。
新しい作業データは`Assets/RimeEmoData`に保存します。

RimeEmo本体の利用許諾はまだ設定していません（`UNLICENSED`）。
同梱フォントのライセンスと帰属表記は`Editor/UI/Icons`に保持しています。

## ローカル検証

Python 3.10以降（外部ライブラリ不要）:

```sh
python -m unittest discover -s Scripts~ -p "test_*.py"
python Scripts~/release.py build --tag v0.1.0 --output dist~
```

Unity 2022.3.22f1のテスト用プロジェクトで`Tools > RimeEmo > Run Core Checks`を実行します。
更新判定、数値バージョン比較、非正規URLの拒否、UI要素もこのチェックに含まれます。

更新の自動確認はツールを開いたときに1回だけ行います。HTTPSで公開メタデータをGETするだけで、
アバター名、アセット、プロジェクト内容などを送信しません。バッチモードでは通信しません。
自動確認の設定はUnity Editorの設定に保存されます。通信失敗は設定ページに表示し、
編集中のデータやプレビューには影響しません。

同じバージョンを修正して再配布する場合だけ、`Publish VPM release`の手動実行で
対象タグを入力し、`Replace the existing tag and ZIP...`を有効にします。
検証後、タグとZIPを選択ブランチのコミットに差し替え、VPM一覧も再生成します。
通常の更新ではバージョンを上げ、新しいReleaseを公開してください。