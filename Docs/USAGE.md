# CableGenerator — Usage Guide

## 1. Overview

CableGenerator is a Unity Editor extension that generates procedural meshes along Bezier splines in real time. It is designed primarily for VRChat world creation and can produce cables, pipes, roads, or any other shape with a consistent cross-section extruded along a 3D path.

**Key features:**

- Real-time mesh rebuild whenever the spline is edited
- Fully customizable cross-section via `CableProfile` ScriptableObjects (open/closed extension)
- Scene View interactive handles for control points and tangents
- Spline setup tools: 2-point surface picking, knot subdivision, sag simulation, surface projection
- Automatic lightmap UV (UV1) packing
- One-click baked mesh export as `.asset`
- Full Undo/Redo support for every operation

**Requirements:**

- Unity 2022 or later
- Unity Splines package
- Unity.Mathematics package

---

## 2. Basic Usage

### 2.1 Create a Cable Object

From the Unity menu bar:

```
GameObject > Cable Generator > Create Cable
```

A `Cable` GameObject is created with the following components pre-configured:

| Component | Role |
|---|---|
| `SplineContainer` | Holds the Bezier spline (2 default knots) |
| `MeshFilter` | Receives the generated mesh |
| `MeshRenderer` | Renders the mesh (default material applied) |
| `CableGenerator` | Drives the mesh generation |

### 2.2 Assign a Cross-Section Profile

Select the Cable object and locate the **CableGenerator** Inspector. Set the **断面プロファイル (Cross-Section Profile)** field to a `CableProfile` asset.

To create a profile asset:

```
Right-click in Project window > Create > CableGenerator > Profiles > [Profile Type]
```

No mesh is generated until a profile is assigned.

### 2.3 Edit the Spline Shape

Select the Cable object and open the Scene View. The following handles appear:

- **Colored spheres** — one per control point (knot). Color indicates tangent mode (see §3.3).
- **Blue / orange sphere handles** extending from each knot — tangent (In / Out).
- **Yellow polyline** — spline preview.
- **[N] / [N]▶ / ◀[N] labels** — knot index displayed above each knot. The start knot shows **[N]▶** and the end knot shows **◀[N]**.

| Action | How |
|---|---|
| Move a control point | Drag the Position Handle (XYZ arrows) at the knot |
| Move a tangent | Drag the blue (In) or orange (Out) sphere |
| Select a knot | Click the colored sphere in Scene View |
| Multi-select knots | Hold **Shift** and click additional spheres |

### 2.4 Add and Remove Control Points

In the **SplineContainer** Inspector, use the **制御点リスト (Control Point List)**:

- **＋ 制御点を追加** — adds a knot after the currently selected one. If a middle knot is selected, the new knot is inserted at the midpoint between that knot and the next. If the last knot is selected, the new knot is placed 2 m in the outgoing tangent direction.
- **×** on any row — deletes that knot (requires at least 2 knots; button is disabled when only 2 remain).

### 2.5 Adjust Mesh Quality

| Parameter | Where | Effect |
|---|---|---|
| 分割数（滑らかさ） | CableGenerator Inspector | Ring segment count along the spline (2–256). Higher = smoother curves. |
| UVタイリング | CableGenerator Inspector | V-axis tiling of UV0. Increase to repeat the texture more often along the cable. |

### 2.6 Export a Baked Mesh

When the cable shape is finalized, export it for VRChat:

1. Expand **エクスポート** in the CableGenerator Inspector.
2. (Optional) Click **...** to choose a folder inside `Assets/`. If left blank the default output folder is used.
3. Click **メッシュを保存 (.asset)**.

What happens:

- The mesh is saved as a `.asset` file.
- A new `<Name>_cable_baked` GameObject is created at the same position/rotation/scale with the baked mesh and the same materials.
- The original Cable object is tagged `EditorOnly` and deactivated — it will be excluded from VRChat builds automatically.

---

## 3. UI Reference

### 3.1 CableGenerator Inspector

#### Cross-Section Profile *(always visible)*

| Element | Description |
|---|---|
| 断面プロファイル | The `CableProfile` asset that defines the mesh cross-section. Required. |
| 分割数（滑らかさ） | Number of cross-section rings along the spline (range: 2–256). |
| UVタイリング | UV0 V-axis tiling multiplier. |
| メッシュを再生成 | Forces an immediate full mesh rebuild. |

---

#### 2点選択でスプライン配線 *(foldable)*

| Element | Description |
|---|---|
| コライダーを付与 | Adds a temporary `MeshCollider` to every eligible object in the scene so they can be targeted by raycasts (2-point picking and surface projection). Objects are processed in batches; a progress bar is shown while processing. When colliders are already attached, the button label changes to **コライダーを再付与**. |
| コライダーを削除 | Removes all temporary colliders added by the button above. |
| ハンドルの長さ | Rate slider. Drag right/left to grow/shrink all tangent handles proportionally to their segment lengths. Release to stop. Current scale shown in metres. |
| 2点選択でSplineを設定 | Enters 2-point picking mode. Click two collider surfaces in Scene View; a 2-knot spline is created between those points, oriented along the surface normals. **Target meshes must have a Collider (MeshCollider recommended).** |
| やり直し *(picking mode)* | Clears the first picked point to start over. |
| キャンセル *(picking mode)* | Exits picking mode without any change. |

> **Warning:** Box, Capsule, and Sphere colliders may produce incorrect surface normals. Use MeshCollider for accurate results.

---

#### ノットの細分化・等分 *(foldable)*

| Element | Description |
|---|---|
| 追加ノットモード | Tangent mode (`AutoSmooth` / `Mirrored` / `Broken`) applied to all newly inserted knots. |
| 始点-終点 分割数 | Number of evenly-spaced divisions to create between the first and last knot. |
| 始点-終点を等分してノット再配置 | Removes all interior knots and places new ones along the straight line from first to last, at equal intervals. The first and last knots are preserved. |
| 全区間を細分化してノット追加 | Inserts one new knot at the mid-curve point of every existing segment. Existing knots are preserved. |

---

#### ノットを面にスナップ配置 *(foldable)*

Projects one or more knots onto scene geometry via raycasting.

| Element | Description |
|---|---|
| 対象ノット Index | Index of the knot to project (single-knot mode). |
| Multi-select | Hold **Shift** and click knot spheres in Scene View to add them to the selection. The label shows which indices are selected. |
| 投影方向 | World-space direction vector of the raycast. |
| 方向をローカル扱い | When ON, the direction is interpreted in the object's local space. |
| 最大距離 | Maximum raycast distance (metres). |
| 面オフセット | Offset applied along the hit normal to lift the knot slightly above the surface (avoids z-fighting). |
| LayerMask | Layers included in the raycast. |
| 指定ノットを面へ投影 | Fires rays from the selected knot(s) and moves them to the hit positions. Reports results below the button. When multiple knots are selected via Shift+click, the label changes to **選択した N ノットを面へ投影**. |

---

#### ケーブルたわみ設定 *(foldable)*

Simulates gravity sag by inserting intermediate knots.

| Element | Description |
|---|---|
| たわみノットを挿入 | Inserts one sag knot at the midpoint of each segment. The total knot count roughly doubles. |
| 降下距離 | How far the sag knots drop downward (0–5 m). Adjustable in real time after insertion. |
| Mirrored ハンドル | When ON, sag knots use `Mirrored` tangent mode (symmetric handles). When OFF, `AutoSmooth`. |
| ハンドルの長さ | Handle length of sag knots. Active only when **Mirrored ハンドル** is ON. |

> **Note:** Sag adjustment mode ends if you undo, or otherwise alter the spline structure. Click **たわみノットを挿入** again to re-enter it.

---

#### アタッチメント *(foldable)*

Appears only when the SplineContainer has a spline.

| Element | Description |
|---|---|
| ノット N にアタッチメントを追加 | Creates a child GameObject named `Attachment_KnotN` with a `CableKnotAttachment` component. The child follows knot N's world position. |

The created child object's **CableKnotAttachment** component exposes the following fields:

| Field | Description |
|---|---|
| knotIndex | Index of the knot to follow. |
| prefab | **Prefab or FBX model asset** to instantiate at the knot. Accepts both `.prefab` files and non-prefabbed `.fbx` model assets. Assign by dragging from the Project window. |
| positionOffset | Position offset from the knot in local space. |
| rotationOffset | Additional rotation in Euler angles. |
| scale | Scale of the spawned instance. Each axis (X, Y, Z) can be set independently, so you can stretch or squash the attached object along any specific axis. |

> **Note:** `.prefab` files are instantiated with a maintained Prefab connection. `.fbx` model assets (and other non-Prefab assets) are instantiated directly without a Prefab connection.

---

#### エクスポート *(foldable)*

| Element | Description |
|---|---|
| 保存先フォルダ | Target folder path inside `Assets/`. Empty = default output folder. |
| ... | Opens a folder picker dialog. |
| メッシュを保存 (.asset) | Saves the mesh asset, creates a baked GameObject, and disables the original cable object. |

---

### 3.2 SplineContainer Inspector

When a `SplineContainer` belongs to a `CableGenerator` object, it uses a custom UI. A **カスタムUIを使用** toggle at the top switches to Unity's default Splines UI.

#### Stats Bar

| Element | Description |
|---|---|
| 制御点: N | Current knot count. |
| 全長: X.XX m | Spline arc length in world units. |
| ループ | Closes or opens the spline into/from a loop. |
| ↕ 反転 | Reverses the spline direction (swaps start and end, flips all tangents). |
| ⟺ 均等配置 | Redistributes all knots evenly along the existing arc length. Start and end positions are preserved. |

---

#### Control Point List

One row per knot.

| Control | Description |
|---|---|
| [N] / [N] スタート / [N] エンド | Click to select this knot. Selected knot is highlighted in the Scene View. |
| Tangent mode dropdown | **自動** = AutoSmooth, **スムーズ** = Mirrored, **コーナー** = Broken |
| × | Delete this knot (disabled when only 2 remain). |
| ＋ 制御点を追加 | Adds a knot after the currently selected one. |

**Knot sphere color by tangent mode (Scene View):**

| Color | Mode |
|---|---|
| Cyan | AutoSmooth (自動) |
| Green | Mirrored (スムーズ) |
| Yellow | Continuous |
| Red | Broken (コーナー) |

---

#### Selected Knot Detail

| Element | Description |
|---|---|
| Global / Local | Toggles the display space for rotation and tangent angles. **Position is always displayed in world space** regardless of this toggle. |
| ◀ / ▶ | Navigate to the previous / next knot. |
| 位置 | Editable position. Always displayed in **world space** regardless of the Global / Local toggle. |
| 回転・接線（詳細） | Always-visible section. In AutoSmooth (**自動**) mode, a help message is shown and all controls are inactive. Controls become active in **スムーズ** (Mirrored) or **コーナー** (Broken) mode. |

**Inside 回転・接線:**

| Element | Description |
|---|---|
| 回転 X / Y / Z | Knot rotation as Euler angles. **0** resets the axis; **−** / **+** nudge by 15°. |
| ハンドル長さ（同期） | *(Mirrored / Continuous)* Single rate slider that sets both TangentIn and TangentOut length simultaneously. |
| タンジェントOut 角度 | *(Broken)* Direction of the outgoing tangent. |
| タンジェントOut 長さ | *(Broken)* Length of the outgoing tangent (rate slider). |
| タンジェントIn 角度 | *(Broken)* Direction of the incoming tangent (independent from Out). |
| タンジェントIn 長さ | *(Broken)* Length of the incoming tangent (rate slider). |

---

### 3.3 Scene View Handles Summary

| Element | Description |
|---|---|
| Colored sphere + XYZ arrows | Control point. Drag arrows to move; click sphere to select; Shift+click to multi-select. |
| Blue sphere + line | TangentIn handle. Drag to adjust the incoming curve. |
| Orange sphere + line | TangentOut handle. Drag to adjust the outgoing curve. |
| Yellow polyline | 64-step spline preview. |
| [N] / [N]▶ / ◀[N] label above knot | Knot index. The start knot shows **[N]▶** and the end knot shows **◀[N]**. Yellow text, larger font = currently selected. |

---

## 4. Q&A

**Q: The mesh does not appear after creating the Cable object.**  
A: A `CableProfile` asset must be assigned in the **断面プロファイル** field. No mesh is generated without it.

**Q: 2-point picking does not register my clicks.**  
A: The target meshes must have a Collider component. Open the target mesh's Inspector and add a `MeshCollider`. Primitive colliders (Box, Capsule, Sphere) work but may produce inaccurate orientations.

**Q: The sag sliders stopped working.**  
A: Sag-adjustment mode exits whenever the spline structure changes (undo, adding/removing knots, etc.). Click **たわみノットを挿入** again to re-enter it.

**Q: How do I create a custom cross-section shape?**  
A: Subclass `CableProfile` in the `CableGeneratorRuntime` namespace, implement `GetVertices()`, `GetNormals()`, and `GetUCoords()`, and add `[CreateAssetMenu]`. No changes to `CableGenerator` or the Inspector are needed.

**Q: Can I make a closed loop (e.g., a ring)?**  
A: Yes. In the **SplineContainer** Inspector, enable the **ループ** toggle in the Stats Bar. The spline will close, and the mesh will connect the last segment back to the first.

**Q: The mesh has a twist at sharp bends.**  
A: This can happen when the spline tangent flips direction at a tight corner. Try adding an intermediate knot near the bend, then manually adjust its rotation in the **ノット詳細** panel to remove the twist.

**Q: How do I undo an operation?**  
A: Every operation — moving knots, adding/removing knots, projection, sag insertion, export — is registered with Unity's Undo system. Use **Ctrl+Z** (Windows) or **Cmd+Z** (Mac).

**Q: The baked mesh and the original cable both appear after export.**  
A: After export, the original object is tagged `EditorOnly` and set inactive. It will not be included in VRChat builds. If it is still visible in Edit mode, check the Hierarchy — the object should show as inactive (greyed out).

**Q: The lightmap UV (UV1) appears stretched.**  
A: UV1 is auto-packed from the profile perimeters and spline length using a shelf-packing algorithm. For very high aspect-ratio cables, packing may not be ideal. As a workaround, manually unwrap and bake UV1 in an external DCC tool after exporting the mesh asset.

**Q: Can I assign an FBX file directly to the attachment prefab field?**  
A: Yes. The `prefab` field on `CableKnotAttachment` accepts both `.prefab` files and non-prefabbed `.fbx` model assets. Drag the FBX from the Project window and drop it onto the field. `.prefab` files are instantiated with a Prefab connection preserved; `.fbx` model assets are instantiated directly without one.

**Q: Can I drive the cable shape from a script at runtime?**  
A: Yes. `CableGenerator` listens to `Spline.Changed` events, so modifying the `SplineContainer` programmatically will trigger an automatic rebuild. The component uses `[ExecuteAlways]`, so it also responds in Edit mode.
