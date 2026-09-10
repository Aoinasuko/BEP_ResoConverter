# BEP Fairy Tech ResoConverter

シーン上のアバター・3Dモデルを、Resoniteへ取り込める `.resonitepackage` に変換するUnity Editorツールです。

## 必要な環境

- Windows 64 bit / Unity 2022.3 LTS
- ローカルにインストールされたResonite本体
- VRChat SDK、Modular Avatar、NDMF、lilToonは任意です。未導入でもコンパイル・モデル出力ができます。

ResoniteのエンジンDLLを同梱せず、指定されたインストール先から読み込みます。変換用プロセスは別に起動され、Resoniteへのログインは不要です。

## 使い方

1. [Releases](https://github.com/Aoinasuko/BEP_ResoConverter/releases)から `BEP_ResoConverter-v0.1.2.unitypackage` を取得してインポートします。配置先は `Assets/BEPFairyTech/ResoConverter` です。
2. `BEP Fairy Tech → ResoConverter` を開きます。
3. Hierarchy上の対象ルートを「変換対象」に指定します。Project内のPrefabアセットは対象にできません。
4. 「アバター」または「3Dモデル・アイテム」を選びます。
5. 必要に応じて瞬き用のメッシュとBlendShape、サイズ、ポーズ固定を設定します。
6. Resonite本体のフォルダーを確認し、UnityのAssets外へ書き出します。
7. 出力された `.resonitepackage` をResoniteへドラッグ＆ドロップします。Windowsでは「成果物を文字列としてコピー」を押し、同じPCのResoniteでキーボードの `Ctrl+V` を使って取り込むこともできます。VRではDashの **Files** タブから出力ファイルを開いてください。

処理は一時コピーに対して行います。元のシーンやモデルアセットは書き換えません。

「成果物を文字列としてコピー」は変換ボタンの下にあり、変換に成功した最新の出力を対象にします。出力前は無効です。コピーされるのは `.resonitepackage` の**絶対ファイルパスそのもの**です。パッケージ本体を文字列化・Base64化したデータや、URIではありません。日本語・空白・絵文字を含むファイル名にも対応します。

同じPCで動くResoniteから取り込み、貼り付けが完了するまでコピー元の `.resonitepackage` を移動・削除しないでください。文字列だけを別のPCへコピーしても、モデルのデータは転送されません。

v0.1.2では文字列を読み取る貼り付け経路に対応するため、クリップボードへUnicode文字列だけを渡します。**Resonite標準Dashの `Paste content from clipboard` ボタンによる実機動作は未確認です。** 反応しない場合はキーボードの `Ctrl+V`、ドラッグ＆ドロップ、またはFilesタブを使用してください。

## 以前の版からの更新

v0.1.1で作成した `.resonitepackage` は再変換せずに使えます。v0.1.2をインポートした後、「成果物を文字列としてコピー」でコピーし直してください。

v0.1.1では、出力したアイテムの当たり判定が空になり、掴めない場合がある問題を修正しました。メッシュの読込完了後に当たり判定を計算し、薄いモデルにも各軸の最低限の厚みを設定します。

v0.1で出力したアイテムを掴めない場合は、最新の `.unitypackage` をインポートしてから、元のUnityシーンで再度変換し、新しい `.resonitepackage` を取り込んでください。古いファイルの当たり判定は、コピーし直すだけでは修正されません。

## 設定

| 設定 | 動作 |
| --- | --- |
| アバター | ルートのHumanoid Animatorからリグを設定。VRCの視点、Viseme、Jaw Flapを可能な範囲で変換 |
| 3Dモデル・アイテム | アバター用の装着・IK設定を付けずに出力 |
| 他の人による保存を制限 | 初期値ON。ResoniteのSimpleAvatarProtectionを付与。OFFなら付与しない |
| 瞬き | 指定したメッシュのBlendShapeをResoniteの瞬き制御へ割り当て |
| 標準サイズ | レンダラーの高さを基準に1.8mへ調整 |
| 元アバターのサイズ | シーン上のスケールを維持 |
| 現在のポーズと表情で固定 | SkinnedMeshRendererを現在の変形結果でベイクし、静的メッシュに変換。揺れ物は停止 |
| Modular Avatarを反映 | 導入済みなら一時コピーで前処理し、Bone ProxyやMerge Armatureの配置・追従を反映 |

## 互換性と制限

- PhysBoneはResoniteのDynamicBoneChainへ近似変換します。Pull、Spring、Stiffness、Gravity、Immobileの基本値を、チェーン全体の弾性・減衰・硬さ・重力・慣性へ換算します。VRCとResoniteは別の物理計算を使うため、同じ揺れ加減を再現するものではなく、取り込み後の調整が必要になる場合があります。
- PhysBoneの対象ボーン、除外ボーン、末端オフセット、半径と半径カーブを反映します。Pullなどのボーンごとのカーブはサンプリング値をパッケージ内の参照情報へ記録しますが、Resoniteの各ボーンの物理係数としては適用しません。元のUnityシリアライズ値も `.report.json` に記録します。
- PhysBoneの角度制限、Gravity Falloff、Stretch / Squish、計算モード、Animatorやパラメーターによる動作変更は再現しません。球コライダーを移し、カプセルは複数の球で近似します。平面・内側拘束・半径0のコライダーは省略して警告します。VRCの相手別の衝突・つかみ許可やCollision Tagsも同じ意味では移植されません。
- lilToonはResoniteのXiexeToon材質へ変換します。色、テクスチャ、透過、法線、発光、影、輪郭など対応する要素を移し、一部はテクスチャへ焼き込みます。独自シェーダーコードや全特殊効果は移せません。
- Modular Avatar導入時はNDMFの前処理を一時コピーへ適用し、Bone ProxyやMerge Armatureによる配置・統合の結果を通常のボーン階層・メッシュとして出力します。両コンポーネントの追従と元シーンを変更しないことをテストしています。NDMFが利用できない場合の代替処理は、Bone ProxyとMerge Armatureの基本的な追従だけに限定し、未適用のMAコンポーネントを警告します。
- Modular AvatarのExpression MenuやAnimatorの操作ロジック、VRChatのContacts、独自スクリプトはResonite用ロジックへ自動変換しません。VRC Constraintは書き出し時点のTransformを出力し、Resonite上で制約による追従を再計算しません。静的な小物にする場合は「現在のポーズと表情で固定」を使用してください。
- 1頂点に5本以上のボーンが影響するメッシュは、Resonite出力で上位4本へ重みを正規化し、警告します。選択ルート外のボーンに依存するメッシュは、アバター全体を対象にするかポーズ固定を有効にしてください。
- 削除されたパッケージに由来するMissing Scriptの設定は復元できません。未導入でもツールは動きますが、元設定の読み出しには元コンポーネントが必要です。
- 保存制限はResonite標準コンポーネントの挙動を利用します。暗号化やデータの複製そのものを防止する仕組みではありません。自分のインベントリへの保存後、別ユーザー視点で動作を確認してください。
- 変換結果と並ぶ `.report.json` に警告を記録します。詳細ログはUnityプロジェクトの `Library/BEPFairyTech/ResoConverter/Runs` にあります。
- Resonite本体の更新で内部APIが変わる場合は、変換エンジンの更新が必要になります。

## 使用している第三者コード

このツールのBEP Fairy Techによるコードは、同梱の `LICENSE.txt` に記載したMIT Licenseで提供します。第三者コードには、それぞれのライセンスと著作権表示が引き続き適用されます。

ネイティブ変換エンジン、メッシュ・材質変換の一部は、bd_氏の [Modular Avatar Resonite](https://github.com/bdunderscore/modular-avatar-resonite)（MIT）をもとにしています。各フォルダーのライセンス表記を参照してください。BEP ResoConverterの変更部分は独立したUnity UI、任意依存への変更、アイテム出力、ポーズ固定、追加の変換設定などです。
