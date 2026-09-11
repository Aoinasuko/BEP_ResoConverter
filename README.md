# BEP Fairy Tech ResoConverter

Unityのシーンに配置したVRChat用アバター・3Dモデルを、Resoniteへ取り込める `.resonitepackage` に変換するUnity Editorツールです。

アバターの揺れ物・表情・材質やModular Avatarの配置設定を可能な範囲で移し、ポーズを固定した置物としての出力にも対応します。対応する設定をResoniteの標準コンポーネントへ変換します。

## 必要な環境

- Windows 64 bit
- Unity 2022.3 LTS
- ローカルにインストールしたResonite本体

VRChat SDK、Modular Avatar、NDMF、lilToonは任意です。未導入でもツールのコンパイルとモデル出力ができます。元の設定を読み取るには、その設定を持つコンポーネントやシェーダーが必要です。

配布パッケージには変換用ランタイムを含みます。.NET SDKを別途インストールする必要はありません。Resonite本体のDLLは同梱せず、指定したインストール先から読み込みます。変換時のResoniteへのログインは不要です。

## インストールと使い方

1. [Releases](https://github.com/Aoinasuko/BEP_ResoConverter/releases)から `BEP_ResoConverter-v0.2.0.unitypackage` をダウンロードします。
2. 対象のUnityプロジェクトで `Assets → Import Package → Custom Package…` を選び、パッケージをインポートします。
3. `BEP Fairy Tech → ResoConverter` を開きます。配置先は `Assets/BEPFairyTech/ResoConverter` です。
4. Hierarchy上の対象ルートを指定し、「アバター」または「3Dモデル・アイテム」を選びます。
5. 瞬き、ハンドサイン・メニュー表情、サイズ、ポーズ固定などの設定とResonite本体のフォルダーを確認し、UnityのAssetsフォルダー外へ書き出します。
6. 出力した `.resonitepackage` をResoniteへドラッグ＆ドロップして取り込みます。Windowsでは「成果物を文字列としてコピー」を押し、同じPCのResoniteでキーボードの `Ctrl+V` を使うこともできます。VRではDashの **Files** タブから出力ファイルを開いてください。

Project内のPrefabアセットは直接の対象にできません。シーンへ配置してから選択してください。変換は一時コピーで行い、元のシーンやモデルアセットは書き換えません。

「成果物を文字列としてコピー」は、変換に成功した最新の `.resonitepackage` の**絶対ファイルパスそのもの**をUnicode文字列としてコピーします。パッケージ本体を文字列化・Base64化したものや、URIではありません。日本語・空白・絵文字を含むファイル名に対応します。出力前はボタンを押せません。

コピーしたパスを読める**同じPCのResonite**から取り込んでください。貼り付けが完了するまで、コピー元の `.resonitepackage` を移動・削除しないでください。文字列だけを別のPCへ渡しても、モデルのデータは転送されません。

v0.1.2では、文字列を読み取る貼り付け経路に対応するため、クリップボードへ文字列だけを渡す方式に変更しました。**Resonite標準Dashの `Paste content from clipboard` ボタンによる実機動作は未確認です。** 反応しない場合はキーボードの `Ctrl+V`、ドラッグ＆ドロップ、またはFilesタブを使用してください。

v0.1.1以降で作成した出力ファイルはそのまま利用できます。新しい表情機能を追加する場合は、v0.2.0で設定して再変換してください。v0.1で出力したアイテムを掴めない場合も、最新版をインポートして元のUnityシーンから再変換が必要です。古いファイルの当たり判定は、コピーし直すだけでは修正されません。

## 主な機能

| 機能 | 内容 |
| --- | --- |
| アバター／アイテム出力 | Humanoidアバターとしての出力と、3Dモデルとしての出力を選択 |
| 保存制限 | 初期値ON。ResoniteのSimpleAvatarProtectionを付与し、チェックを外すと付与しない |
| 揺れ物 | VRC PhysBoneの基本値、対象ボーン、半径カーブ、対応コライダーを近似変換 |
| Modular Avatar | Bone Proxy・Merge Armatureなどの前処理結果を配置・ボーン階層・メッシュへ反映 |
| lilToon | 色・テクスチャ・透過・法線・発光・影・輪郭・リムライトを対応する材質設定へ変換 |
| 瞬き・口パク | 瞬き用BlendShapeの選択、VRC Viseme・Jaw Flap設定の変換 |
| ハンドサイン表情 | 左右それぞれ8種の手形またはAnyを指定し、両手の条件に合う表情へ切り替え |
| メニュー表情 | 名前とAnimationClipを登録し、Resonite上のメニューで表情を固定。ハンドサインより優先 |
| アバターサイズ | 標準の高さ1.8m、または元のアバターサイズを選択 |
| ポーズ固定 | 現在のポーズとBlendShapeを静的メッシュへ焼き込み、置物として出力 |
| 成果物を文字列としてコピー | Windowsで最新の出力ファイルの絶対パスをUnicode文字列としてコピー |

詳しい使い方と対応範囲は[日本語マニュアル](Assets/BEPFairyTech/ResoConverter/README-ja.md)を参照してください。

## 表情の設定

アバター出力では「両手の形に合わせて表情を変える」と「メニューから表情を変えられるようにする」を個別に有効にできます。左右のハンドサインはVRChat形式の8種と、条件を限定しない「指定なし（どの形でも）」（Any）から選びます。左右両方の条件が一致した行を使い、複数の行に一致した場合は上の行を優先します。手形はResonite上の実際の指の姿勢から近似判定するため、VRChatとの認識結果や機器ごとの操作感には差が生じる場合があります。

各表情にはAnimationClipとサンプル時刻を指定します。移すのは**指定時刻のBlendShapeの静止値だけ**で、時刻の初期値は0秒です。アニメーションの再生、Transform、材質、有効・無効の切り替えは移さず、対応外のキーを警告します。対象BlendShapeがないクリップや、変換対象への参照を解決できないクリップは変換を停止して知らせます。

メニュー項目を1件以上登録すると、Resonite上の「表情」メニューに「初期状態」を自動で追加します。メニュー表情はハンドサインより優先して固定され、両機能を使う場合は「ハンドサインに戻す」で手による切り替えを再開できます。初期状態は変換時のモデルの値を基準にし、表情が指定していないShapeの瞬き・口パクは維持します。3Dモデル・アイテム出力では両設定を無効化し、表情操作用の仕組みを付けません。

## 対応範囲について

UnityとResoniteではシェーダーと物理計算が異なるため、見た目や揺れ加減の完全一致は保証しません。表情はこのツールで明示的に設定します。既存のVRChat / Modular AvatarのExpression Menu、Animatorの操作ロジック、Contacts、独自スクリプト、すべてのシェーダー特殊効果を自動移植する機能はありません。

保存制限はResonite標準コンポーネントの挙動を利用します。暗号化やデータの複製そのものを防ぐ機能ではありません。取り込み後に、見た目・動き・保存制限を確認してください。

未対応設定や近似変換の警告は、出力と並ぶ `.report.json` に記録します。検証済みの範囲と未確認の実利用項目は[検証記録](VERIFICATION.md)にまとめています。Resoniteの内部APIが更新された場合、変換エンジンの更新が必要になることがあります。

## 開発

このリポジトリはUnityへ追加するツールのソースです。開発用Unityプロジェクトへコピーするには、リポジトリのルートから次を実行します。

```powershell
.\Tools\install.ps1 -ProjectPath '使用するUnityプロジェクトのフォルダー'
```

Unityの `Window → General → Test Runner` から、EditModeの `BEPFairyTech.ResoConverter.Editor.Tests` を実行できます。Unity MCPを導入した開発環境では、付属のクライアントも利用できます。Python 3が必要です。

```powershell
python Tools/unity_mcp.py --project '使用するUnityプロジェクトのフォルダー' --tool console_read_logs --args '{"type":"error","limit":20}'
python Tools/unity_mcp.py --project '使用するUnityプロジェクトのフォルダー' --tool test_run --args '{"mode":"edit","assembly":"BEPFairyTech.ResoConverter.Editor.Tests"}'
python Tools/unity_mcp.py --project '使用するUnityプロジェクトのフォルダー' --tool test_results
```

MCPクライアントは、起動中のUnityが作成するローカル接続記述から認証情報を読みます。接続記述やトークンはリポジトリへ追加しないでください。通常のツール利用にMCPは不要です。

変換エンジンを再ビルドする場合は、.NET 10 SDKとResonite本体が必要です。

```powershell
.\Backend\build.ps1 -ResonitePath 'Resonite本体のフォルダー'
```

Unityコードは `Assets/BEPFairyTech/ResoConverter`、変換エンジンは `Backend/Native` にあります。詳しくは[バックエンド開発資料](Backend/README.md)を参照してください。`BackendPayload.bytes` は配布に必要なためソース管理に含め、`Dist` 内の `.unitypackage` はReleasesの添付ファイルとして配布します。

## ライセンス

BEP Fairy Techによるコードは[MIT License](LICENSE)で提供します。第三者コードには、それぞれの著作権表示・ライセンスが引き続き適用されます。

- Modular Avatar Resonite由来のコード：MIT。[バックエンドのライセンス](Backend/Native/THIRD-PARTY-LICENSE.md)
- lilToonおよび材質変換に由来するコード：MIT。[材質変換のライセンス](Assets/BEPFairyTech/ResoConverter/Editor/Materials/COPYING.md)
- Google.Protobuf：BSD-3-Clause。[protobufのライセンス](Backend/UnityProtobuf/LICENSE)
- .NETランタイムなど同梱ソフトウェア：[第三者ソフトウェアの告知](Backend/THIRD-PARTY-NOTICES.md)

変更内容は[CHANGELOG](CHANGELOG.md)を参照してください。
