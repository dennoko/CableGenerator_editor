# CableGenerator

ベジェスプラインに沿って任意断面のメッシュをリアルタイム生成する Unity エディタ拡張。  
ケーブル・パイプ・道路など、3D パスに断面形状を押し出したオブジェクトを VRChat ワールド向けに手軽に作れます。

## 主な機能

- スプライン編集に連動したメッシュのリアルタイム再生成
- `CableProfile` ScriptableObject による断面形状のカスタマイズ（継承で拡張可）
- Scene ビューでの制御点・接線ハンドル編集
- 2点選択・ノット細分化・面投影・たわみシミュレーション
- ライトマップ UV（UV1）の自動パッキング
- ベイク済みメッシュの `.asset` エクスポート（VRChat アップロード用）
- 全操作の Undo/Redo 対応

## 動作要件

- Unity 2022 以降
- Unity Splines パッケージ
- Unity.Mathematics パッケージ

## 使い方

`Docs/USAGE_ja.md` を参照してください。

## ライセンス

Copyright © dennoko
