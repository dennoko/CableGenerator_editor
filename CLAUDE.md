# CableGenerator — CLAUDE.md

Unity エディタ拡張。ベジェスプラインを沿わせて任意断面のメッシュ（ケーブル・パイプ・道等）を
リアルタイム生成・Bake するツール。VRChat ワールド制作を主用途とする。

---

## プロジェクト構造

```
Assets/Editor/CableGenerator/          ← このディレクトリ（Editor専用コード）
├── CableGeneratorEditor.cs            ← Inspector UI + Scene ハンドル
├── CableGeneratorTheme.cs             ← IMGUI スタイル定義（dennoko.dev カラースキーマ）
├── CableMeshExporter.cs               ← .asset 保存ユーティリティ
├── Material/default_cable.mat         ← デフォルトマテリアル
├── Docs/Impl/                         ← 設計ドキュメント（要件定義・実装設計・UV解説）
└── dennokoworks_color_schema/         ← カラースキーマ仕様（外部参照用）

Assets/Scripts/CableGenerator/        ← Runtime コード（Editor外）
├── CableGenerator.cs                  ← MonoBehaviour: メッシュ生成エンジン
├── CableProfile.cs                    ← ScriptableObject 抽象基底: 断面プロファイル
└── CableKnotAttachment.cs             ← MonoBehaviour: ノットへのオブジェクトアタッチ
```

### ネームスペース

| ネームスペース             | 用途                        |
|---------------------------|-----------------------------|
| `CableGeneratorEditor`    | Editor フォルダ内の全クラス |
| `CableGeneratorRuntime`   | Runtime フォルダ内の全クラス|

---

## 使用技術・パッケージ

| 技術                      | バージョン/用途                                      |
|--------------------------|------------------------------------------------------|
| Unity                    | 2022+                                                |
| Unity Splines            | `SplineContainer`, `BezierKnot`, `SplineUtility`     |
| Unity.Mathematics        | `float3`, `quaternion`, `math.*`（高パフォーマンス数学）|
| UnityEditor / IMGUI      | `Editor`, `Handles`, `EditorGUILayout`               |
| `UnityEditorInternal`    | `InternalEditorUtility`（レイヤーマスク UI）         |

---

## アーキテクチャ原則

### SOLID

**S — 単一責任**  
各クラスは 1 つの関心事のみを持つ。

| クラス                     | 責任                                  |
|---------------------------|---------------------------------------|
| `CableGeneratorInspector` | UI レンダリング + ユーザー入力受け取り |
| `CableGenerator`          | スプライン→メッシュ生成アルゴリズム    |
| `CableProfile`            | 断面形状データの供給                   |
| `CableMeshExporter`       | AssetDatabase への書き込み             |
| `CableGeneratorTheme`     | GUIStyle の構築・キャッシュ            |
| `CableKnotAttachment`     | ノット追従による Transform 更新        |

**O — 開放/閉鎖**  
断面の追加は `CableProfile` を継承する新クラスを作るだけでよい。
`CableGenerator` や `CableGeneratorInspector` は変更しない。

**L — リスコフ置換**  
`CableProfile` のサブクラスは基底クラスの契約（`GetVertices()` / `GetNormals()` / `GetUCoords()` の長さ一致）を必ず守る。

**I — インターフェース分離**  
`CableProfile` の仮想メソッド（`GetVerticesPerLoop`, `GetPerimeters`）はデフォルト実装を持ち、
シンプルな断面では実装不要とする。追加機能は別インターフェース（例: `ICapProfile`）に切り出す。

**D — 依存関係逆転**  
`CableGenerator` は `CableProfile` 抽象に依存し、具体クラスに依存しない。
`CableGeneratorInspector` は `CableMeshExporter` のパブリック静的 API のみ呼び出す。

---

### 単方向データフロー

すべての変更は次の流れ**のみ**で伝播する。逆流・循環は禁止。

```
ユーザー操作
  │
  ▼
Editor 状態変更（static フィールド / SerializedProperty）
  │  Undo.RecordObject → SerializedProperty.Apply
  ▼
SplineContainer データ（BezierKnot 配列）
  │  Spline.Changed イベント
  ▼
CableGenerator.RebuildMesh()
  │  List<T>.Clear → サンプリング → 頂点/法線/UV 計算
  ▼
Mesh アセット（generatedMesh）
  │  MeshFilter.sharedMesh
  ▼
レンダリング（Scene ビュー / Game ビュー）
```

**禁止パターン**  
- メッシュ生成結果が Spline データや Inspector の状態フィールドを書き換える  
- `OnInspectorGUI` 内でメッシュを読んで UI 分岐する  
- `CableGenerator` が `CableGeneratorInspector` の型を参照する

---

## コーディング規約

### 命名

```
クラス/型     : PascalCase
メソッド      : PascalCase（Unity 規約に準拠）
フィールド    : camelCase
静的共有状態  : s_camelCase（Editor の static フィールドは s_ プレフィックス）
定数          : kPascalCase
```

### Undo/Dirty

Spline または ScriptableObject を変更するときは必ず:

```csharp
Undo.RecordObject(target, "操作名（日本語 OK）");
// ... 変更 ...
EditorUtility.SetDirty(target);
```

`SerializedProperty` 経由の変更は `serializedObject.ApplyModifiedProperties()` で代替可。

### パフォーマンス

- `RebuildMesh()` 内では `new` を使わない。`verts.Clear()` → 再充填で List を再利用する。
- `generatedMesh` はフィールドに保持し `new Mesh()` は初回のみ。
- エディタ用テクスチャは `HideFlags.HideAndDontSave`。static キャッシュし再生成しない。
- ドメインリロード後は `_initialized = false` になるため、`Initialize()` の null チェックを必ず入れる。

### 数値安定性

ゼロベクトルのガード:

```csharp
const float kVectorEpsilonSqr = 1e-12f;
if (vec.sqrMagnitude < kVectorEpsilonSqr) vec = Vector3.forward; // フォールバック
```

回転は `SafeLookRotation()` を使い、ジンバルロックを防ぐ。

### コメント方針

- **WHY（理由・制約）** のみコメントする。WHAT は書かない。
- 日本語コメント可（UI ラベルも日本語）。

---

## UI / テーマ

すべての IMGUI スタイルは `CableGeneratorTheme` 経由で取得する。
直接 `new GUIStyle(...)` を Inspector コードに書かない。

```csharp
// OnInspectorGUI の先頭で必ず呼ぶ
CableGeneratorTheme.Initialize();

// 使用例
GUILayout.Button("ラベル", CableGeneratorTheme.ActionButtonStyle);
GUILayout.Label("補足",   CableGeneratorTheme.CaptionStyle);
```

### カラーパレット（dennoko.dev スキーマ）

| トークン          | Hex       | 用途                    |
|------------------|-----------|-------------------------|
| Surface0         | `#121212` | Inspector 背景          |
| Surface1         | `#1e1e1e` | カード、入力フィールド  |
| Surface2         | `#2c2c2c` | ホバー、ツールバー      |
| Outline          | `#3a3a3a` | 枠線                    |
| TextPrimary      | `#ffffff` | 主要テキスト            |
| TextSecondary    | `#cccccc` | 説明テキスト            |
| TextTertiary     | `#aaaaaa` | キャプション、見出し    |
| SemanticError    | `#9b1b30` | エラー                  |
| SemanticWarning  | `#ffb74d` | 警告                    |
| SemanticSuccess  | `#4caf50` | 成功                    |

新しい GUIStyle が必要な場合は `CableGeneratorTheme` に追加する。

---

## 断面プロファイルの追加方法

1. `CableGeneratorRuntime` ネームスペースで `CableProfile` を継承した新クラスを作成  
2. `GetVertices()` / `GetNormals()` / `GetUCoords()` を実装（長さ一致が必須）  
3. `CreateAssetMenu` 属性を付与して ScriptableObject として作成可能にする  
4. `CableGenerator` / `CableGeneratorInspector` の変更は不要

```csharp
[CreateAssetMenu(menuName = "CableGenerator/Profiles/MyProfile")]
public class MyProfile : CableProfile
{
    public override Vector2[] GetVertices() { ... }
    public override Vector2[] GetNormals()  { ... }
    public override float[]   GetUCoords()  { ... }
}
```

---

## 既知の実装詳細

### ピッキングモード（2点選択）

複数の Inspector が同時に開いていても排他制御するため static フィールドで共有:

```
s_pickingTarget  — 現在ピッキング中の CableGenerator（null = 非アクティブ）
s_pickCount      — 取得済み点数 (0 or 1)
s_pickedPoints[] — 取得済みワールド座標
s_pickedNormals[]— 取得済みヒット法線
```

Escape キーまたは 2点目確定後に `CancelPickingMode()` で解除する。

### スプライン再構築の flow

```
Spline.Changed (or ボタン押下)
  → CableGenerator.RebuildMesh()
      → profile.GetVertices/Normals/UCoords
      → SplineUtility.Evaluate (position, tangent, up) × resolution サンプル
      → 各サンプルで 3D 押し出し → verts / normals / uvs / tris 充填
      → generatedMesh.SetVertices / SetNormals / ... / RecalculateBounds
```

### メッシュ Bake フロー

```
Inspector "メッシュを保存" ボタン
  → CableMeshExporter.SaveMeshAsset()  →  .asset 作成
  → SetupBakedMeshObject()
      → Baked 用 GameObject 生成（同階層・同 Transform）
      → MeshFilter/MeshRenderer を追加してベイク済みメッシュをセット
      → 元の Cable オブジェクトを EditorOnly タグ + SetActive(false)
```

---

## 非機能要件

- フレーム時間目標: 操作時平均 < 33ms（制御点ドラッグ時）  
- `RebuildMesh()` 内で GC Alloc ゼロを目指す（List 再利用厳守）  
- Unity 2022 以降でコンパイル・動作すること  
- Undo/Redo はすべての Spline 変更で機能すること  

---

## 参照ドキュメント

| ファイル                          | 内容                                 |
|----------------------------------|--------------------------------------|
| `Docs/Impl/要件定義書.md`         | 機能要件・受け入れ基準               |
| `Docs/Impl/実装設計.md`           | クラス図・メッシュ生成フロー         |
| `Docs/Impl/UV展開ロジック解説.md`  | UV マッピングの数学的説明            |
| `Docs/Impl/技術参照資料.md`        | Splines パッケージ・数学ライブラリ   |
| `dennokoworks_color_schema/`      | カラースキーマ仕様（JSON + Docs）    |
