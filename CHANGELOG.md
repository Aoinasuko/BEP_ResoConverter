# 変更履歴

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
