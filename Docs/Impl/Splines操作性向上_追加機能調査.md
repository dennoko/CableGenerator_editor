# Splinesの操作性向上に関する追加機能 調査結果

## 調査対象
Issue「Splinesの操作性を高めるための追加機能の検討」で挙がっている以下3点について、既存実装を確認した上で実装可否を調査した。

1. ワールド軸平行投影（Blenderの正面/側面/上面のような視点）での操作補助
2. knot選択時のマウスホイールで制御ハンドル長を増減
3. Splines操作ガイドをInspectorのSplinesコンポーネント直前に表示

---

## 現状把握（既存コード）
`CableGeneratorEditor.cs` には以下がすでに実装されている。

- Scene上のknot移動ハンドル描画: `OnSceneGUI()`（約242行以降）
- tangentハンドル描画・編集: `DrawTangentHandle()`（約331行以降）
- 2点選択によるSpline初期化: Picking Mode（約398行以降）
- 指定方向レイでknotを面へ投影: `SnapKnotInDirection()`（約475行以降）

このため、**SceneViewイベント処理・Spline編集API・Undo連携の土台は既にある**。

---

## 1) ワールド軸平行投影視点の操作補助

### 実装可否
**実装可能（高）**。

### 根拠
- Editor拡張側で `SceneView` を制御できるため、
  - 直交投影ON/OFF
  - カメラ向き（±X, ±Y, ±Z）
  - ピボット維持
  といった切替をボタンから実行可能。
- 現行の `OnSceneGUI()` ベース操作と競合しにくい（視点補助はSceneView制御、knot編集は既存Handle処理）。

### 実装イメージ
- Inspectorの `SPLINE SETUP` または新規セクションに「Front/Back/Left/Right/Top/Bottom」ボタン追加。
- ボタン押下で `SceneView.lastActiveSceneView` を対象に、
  - `orthographic = true`
  - 軸方向へ回転
  - 必要なら `SceneView.Frame` 相当で対象を見やすく調整

### 注意点
- SceneViewが未生成/非アクティブ時のnull対策が必要。
- 複数SceneView環境では「lastActive」に限定するか、対象選定方針を決める必要がある。

---

## 2) knot選択時のマウスホイールで制御ハンドル長を増減

### 実装可否
**実装可能（中〜高）**。

### 根拠
- `OnSceneGUI()` 内で `Event.current` を既に扱っており（Picking Modeのクリック/ESC処理）、ホイールイベント追加が可能。
- knot/tangentは `BezierKnot` の `TangentIn/TangentOut` で管理されており、長さスカラー操作を適用しやすい。
- `Undo.RecordObject` + `spline.SetKnot` + `EditorUtility.SetDirty` の既存パターンが再利用できる。

### 実装イメージ
- 直近で操作したknot indexを保持（または `HandleUtility.nearestControl` と紐づけ）。
- `EventType.ScrollWheel` を検出し、対象knotの `TangentIn/TangentOut` を倍率変更。
- 倍率例: `scale = 1f + (-event.delta.y * sensitivity)`
- `TangentMode` に応じて挙動を分岐:
  - Mirrored/Continuous: In/Outを同率で変更
  - Broken: 片側のみ or 両側同率（仕様決めが必要）

### 注意点
- どのknotを「選択中」とみなすかの仕様定義が必要（最後にドラッグしたknot、クリック選択、近傍など）。
- Scrollイベントを `Use()` しないとSceneViewズームと競合する。
- 極小/極大値へのクランプが必要（0付近での不安定化回避）。

---

## 3) Splines操作ガイドをInspectorのSplinesコンポーネント直前に追加

### 実装可否
**実装可能だが、実現方法の選定が必要（中）**。

### 背景
現状の `CableGenerator` カスタムInspectorは「CableGeneratorコンポーネント領域」にのみ描画される。
そのため、**そのままでは別コンポーネント（SplineContainer）直前には描けない**。

### 実現案

#### 案A: `SplineContainer` 用カスタムEditorを追加
- `SplineContainer` のInspector描画時にガイドを先頭表示。
- 要件「Splinesコンポーネント直前」に最も近い。

懸念:
- Unity Splinesパッケージ側Inspectorとの共存設計が必要。
- バージョン差異で追従コストが出る可能性。

#### 案B: ガイド専用コンポーネントを追加し、SplineContainerの上に配置
- 例: `CableSplineGuide`（EditorではHelpBoxのみ表示）を自動追加して上へ移動。
- 既存Inspectorへの依存が少なく、保守しやすい。

懸念:
- コンポーネントが1つ増えるUX許容が必要。

#### 案C: 現行 `CableGenerator` Inspector内にガイドセクション追加
- 実装が最小。

懸念:
- 「Splinesコンポーネント直前」という厳密要件には一致しない。

### 推奨
厳密に要件を満たすなら **案A**、保守性優先なら **案B**。

---

## 総合結論

- 1) 軸平行投影視点補助: **実装可能**（優先度高・リスク低）
- 2) ホイールでハンドル長調整: **実装可能**（選択仕様の明確化が鍵）
- 3) Splines直前ガイド表示: **実装可能**（実装方式の選定が必要）

いずれも、現行のEditor拡張基盤（`OnSceneGUI`, Spline API, Undo運用）上で段階的に導入可能。

---

## 実装着手時の推奨順
1. 視点補助（影響小・効果大）
2. ホイール調整（仕様確定後）
3. ガイド表示（案A/Bの方針決定後）

