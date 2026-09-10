# 変更履歴

## [v0.1.1](https://github.com/Aoinasuko/BEP_ResoConverter/releases/tag/v0.1.1) — 2026-09-11

### 修正

- 3Dモデル・アイテム出力で、当たり判定が空になり掴めない場合がある問題を修正。メッシュの読込完了を待って有効な頂点位置から範囲を計算し、BoxColliderの全軸に最低限の厚みを設定するように変更。
- 旧版で生成された不正なサイズのコライダーと、厚みのない平面モデルを回帰検証の対象に追加。

### 検証の追加

- コンポーネントの有無・件数の確認に加え、Resoniteエンジン上の6方向のレイ判定とGrab処理を検証。
- 問題が報告された2メッシュのパッケージを再現入力とし、修正後は保存制限ON/OFFの両方でレイが当たり、レーザー選択・手の接触に対応するGrab処理が成功することを確認。
- 詳細な範囲と実機での未確認項目は[検証記録](VERIFICATION.md)を参照。

### 取り込み案内の訂正・既知の問題

- v0.1で案内していた、Resonite標準Dashの `Paste content from clipboard` ボタンからの取り込みは、反応しない報告があり未解消。ボタンで取り込めるという案内を訂正。
- 代わりにキーボードの `Ctrl+V`、ファイルのドラッグ＆ドロップ、またはVR時のDashのFilesタブから取り込む手順を案内。
- 旧版で出力したアイテムは自動修正されないため、元のUnityシーンから再変換が必要。

## [v0.1](https://github.com/Aoinasuko/BEP_ResoConverter/releases/tag/v0.1) — 2026-09-11

初回リリース。

### 追加

- `Assets/BEPFairyTech/ResoConverter` へインストールするUnity Editorツール。
- `BEP Fairy Tech → ResoConverter` メニューからの操作。
- シーン上のアバター／3DモデルをResoniteネイティブの `.resonitepackage` へ出力。
- 保存制限を初期値ONとし、チェックで切り替える設定。
- アバターの瞬き用BlendShapeの選択、VRC Viseme・Jaw Flapの変換。
- アバターの標準サイズと元サイズの選択。
- 現在のポーズ・表情を固定した静的アイテム出力。
- PhysBoneの基本設定・半径カーブ・対応コライダーの近似変換と、元設定の記録。
- Modular AvatarのBone Proxy・Merge Armatureを含む前処理への対応。
- lilToon材質の変換、テクスチャの合成、影ランプ・輪郭・リムライトへの対応。
- VRChat SDK・Modular Avatar・NDMF・lilToonへの必須コンパイル依存を持たない構成。
- 警告レポート、変換ログ、キャンセル、出力ファイル表示。
- Windows向け「成果物をクリップボードにコピー」ボタン。最新の出力をコピーし、Resoniteで `Ctrl+V` または `Paste content from clipboard` により貼り付け。
- Windows x64用の変換ランタイムを同梱した `.unitypackage` 配布。

### 検証

- 任意パッケージを導入した環境、および未導入の独立環境でUnity EditModeテストを実行。
- アバター・固定アイテム・保存制限の切り替えを、Resoniteエンジンでの書き出しと読み戻しで確認。
- 詳細は[検証記録](VERIFICATION.md)を参照。

### 既知の制限

- 揺れ方と材質の見た目は近似変換。元環境との完全一致は保証しない。
- Expression Menu・Animatorの操作ロジック・Contacts・独自スクリプトなどは自動移植しない。
- Resonite本体のローカルインストールが必要。内部APIの更新により再ビルドが必要になる場合がある。
- 対応する設定の詳細は[日本語マニュアル](Assets/BEPFairyTech/ResoConverter/README-ja.md)を参照。
