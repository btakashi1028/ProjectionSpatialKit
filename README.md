# Projection Spatial Kit

Unityで作ったインタラクティブ展示の会場構成を、現地入り前に試し、検査するための
**探索的なソーススナップショット**です。

完成した汎用会場シミュレータやUPMパッケージではありません。開発時点の実装を、
記事の再現と設計参照のために公開するものです。継続的な保守や、将来のUnityバージョンとの
互換性は約束していません。

## 何が入っているか

Projection Spatial Kitには、役割の異なる二つの機能があります。

### Preview

- 既存のUnityコンテンツシーンを、別の会場シーンからAdditive Loadする
- 各Content Displayを、仮想プロジェクタやモニタへ表示する
- 会場側のクリックや理想URG入力を、New Input Systemの仮想入力としてコンテンツへ戻す
- 部屋、プロジェクタ、モニタ、URGをSceneビュー上で配置する

Previewは構成の理解と理想状態での挙動確認に使います。実プロジェクタ、実マルチディスプレイ、
実センサの忠実な代替ではありません。

### Preflight

Editモードで投影配置と画面構成を計算し、要対応・警告・情報を対処つきで表示します。

- 投影像の着弾位置と大きさ
- 必要スローレシオと機材プロファイルの整合
- レンズシフト上限、入射角、フォーカス距離
- 投影経路上の遮蔽物
- Display、チャネル、解像度、縦横方向の対応
- コンテンツシーンのBuild Settings登録

結果はMarkdownとしてコピー・保存できます。

Preflightも、実輝度、環境光、色、レンズ歪み、実URGのノイズや遅延までは判断しません。
これらは実機で確認してください。

## 確認環境

| 項目 | 環境 |
| --- | --- |
| Unity | 6000.3系（開発環境: 6000.3.11f1） |
| Render Pipeline | URP 17系 |
| Input | New Input System |
| OpenCV | 任意。DLLは同梱していません |

開発プロジェクトではコンパイルエラー0・警告0、
ProjectionSpatialKitのEditModeテスト38件成功を確認しています。

## ダウンロード

記事執筆時点のZIPをGitHub Releasesからダウンロードできます。

- [ProjectionSpatialKit experimental source snapshot（ZIP）](https://github.com/btakashi1028/ProjectionSpatialKit/releases/download/snapshot-2026-07-29/ProjectionSpatialKit-experimental-snapshot-2026-07-29.zip)
- [Release notes](https://github.com/btakashi1028/ProjectionSpatialKit/releases/tag/snapshot-2026-07-29)

## 導入

1. ZIPを展開します。
2. `ProjectionSpatialKit`フォルダを、対象プロジェクトの
   `Assets/ProjectionSpatialKit`へ`.meta`を含めてコピーします。
3. Unityのコンパイル完了後、メニュー
   `Projection Spatial Kit ▸ Run Project Setup`を実行します。
4. まずサンプルを試す場合は、
   `Assets/ProjectionSpatialKit/Samples/Scenes/910_SampleVenue.unity`を開いてPlayします。
5. 投影配置と画面構成を検査する場合は、
   `Projection Spatial Kit ▸ Preflight Check`を実行します。

既存の`Assets/ProjectionSpatialKit`へ上書きする用途は想定していません。試す前にプロジェクトを
バージョン管理するか、バックアップを作成してください。

詳しい操作とトラブルシュートは[詳細ガイド](USAGE.md)を参照してください。

## できないこと

- 現地検証を不要にすること
- 実プロジェクタの輝度、色、レンズ歪み、最終画質の保証
- 実URGのノイズ、遅延、取りこぼしの再現
- あらゆるUnity、Render Pipeline、入力構成への対応
- 継続的な更新やIssueへの即応

## ライセンス

コードは[MIT License](LICENSE.md)です。

同梱フォントと任意依存関係については
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)を参照してください。
