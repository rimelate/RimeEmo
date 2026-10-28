# RimeEmo

**VRChatアバターの表情と動きを、Unity上で編集するツール。**

表情の調整、アニメーションの作成、Gestureへの割り当てからアバターへの適用までを、ひとつの画面で行えます。日本語・Englishに対応しています。

**[VCCに追加](https://rimelate.github.io/RimeEmo/)** · [操作マニュアル](https://github.com/rimelate/RimeEmo/blob/main/Documentation~/Manual.md) · [更新履歴・ダウンロード](https://github.com/rimelate/RimeEmo/releases)

## できること

- **表情を作る** — プレビューを見ながらShape Keyを調整。部位・グループ・名前で絞り込めます。
- **動きを作る** — 目・眉・口などの部位ごとに、表情や動きをタイムラインへ配置できます。
- **Gestureに割り当てる** — 左右の手の形と、両手の組み合わせに表情・動きを設定できます。元FXの表情も取り込めます。Expressions Menuからの表情固定・表情の選択にも対応しています。
- **アバターに適用する** — Modular Avatarを通して反映。元のFX Controllerを直接編集せずに適用できます。

## 必要な環境

- Unity 2022.3
- VRChat SDK — Avatars
- **[Modular Avatar](https://modular-avatar.nadena.dev/)**（アバターへの適用に必要）

## インストール

1. [配布ページ](https://rimelate.github.io/RimeEmo/)を開き、**VCCに追加** をクリックします。
2. VCCでリポジトリの追加を承認します。
3. 使用するプロジェクトの管理画面で **RimeEmo** を追加します。

<details>
<summary>ALCOMを使う場合・URLを手動で登録する場合</summary>

リポジトリの追加画面に、次のURLを貼り付けてください。

```text
https://raw.githubusercontent.com/rimelate/RimeEmo/vpm/index.json
```

</details>

## はじめて使う

1. Unityで **Tools → RimeEmo → Open** を開きます。
2. **Avatar** にシーン上のアバターを指定します。
3. **設定** ページで顔メッシュと **読込元FX** を確認し、**新規セットアップ** を押します。
4. **表情を作る** で素材を選び、Shape Keyを調整して **保存** します。
5. **Gesture** で手の形に表情を割り当て、**再生成して適用** を押します。
6. シーンを保存します。

初回は画面内の操作ガイドに沿って進められます。右上の **？** からいつでも開き直せます。

詳しい操作は [操作マニュアル](https://github.com/rimelate/RimeEmo/blob/main/Documentation~/Manual.md) を参照してください。

## 更新

新しい安定版がある場合、**ツールを開いたとき**に通知します。更新はVCC / ALCOMのプロジェクト管理画面から行ってください。

**設定** ページで手動確認や、自動確認の有効・無効を切り替えられます。

## 知っておくこと

- 編集・プレビューの対象は、選択した顔メッシュのBlendShapeです。MaterialやTransformなどの動きはプレビューに反映されません。
- **Default Face** の編集は、シーン上のアバターの顔にも反映されます。
- 作業データは `Assets/RimeEmoData` に保存されます。バックアップや別プロジェクトへの移行時は、このフォルダーも含めてください。
- 旧Assets版から移行する場合は、先にプロジェクトをバックアップしてください。

## ドキュメント・サポート

- [操作マニュアル](https://github.com/rimelate/RimeEmo/blob/main/Documentation~/Manual.md) — 表情、タイムライン、Gesture、保存データの管理
- [リリース一覧](https://github.com/rimelate/RimeEmo/releases) — 更新履歴と配布ZIP
- [お問い合わせ（BOOTH）](https://rimerime.booth.pm/) — ショップのメッセージからご連絡ください。不具合の際は使用バージョン、操作手順、エラー内容を添えてください

同梱アイコンのライセンス・クレジットは [ATTRIBUTION.txt](https://github.com/rimelate/RimeEmo/blob/main/Editor/UI/Icons/ATTRIBUTION.txt) を参照してください。
