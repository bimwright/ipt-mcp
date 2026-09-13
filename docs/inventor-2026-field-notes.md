# Inventor 2026 — Field Notes tái dùng (trích từ NeonGlay/inventor-mcp)

> **Nguồn:** [NeonGlay/inventor-mcp](https://github.com/NeonGlay/inventor-mcp) — `docs/inventor-api-notes.md` + 2 Agent Skills. License **MIT** (dùng lại/port được, nên giữ attribution).
> **Cách đọc:** tri thức của họ đúc từ **pywin32 dynamic dispatch**. Tài liệu này đã **tách 3 nhóm**:
> - §A **Portable** — fact hình học/API COM, đúng bất kể ngôn ngữ (áp cho C# add-in của ta).
> - §B **Bảng dữ liệu chuẩn** — DIN/ISO, tap pitch (thuần dữ liệu, tái dùng thẳng).
> - §C **pywin32-only** — KHÔNG áp cho C#; chỉ để tham khảo/hiểu bối cảnh.
> ⚠️ Mọi enum/behavior nên **cross-check lại với add-in C#** trước khi tin tuyệt đối — một số thứ add-in của ta có thể đã tự chuẩn hoá (vd đổi đơn vị).

---

## §A. Portable — fact CAD/COM (áp cho ipt-mcp C#)

### A.1 Đơn vị nội bộ = CENTIMET
Mọi hình học qua COM API là **cm**. mm → chia 10 ở mọi nơi. Góc phần lớn là **radian** (trừ vài chỗ — xem Flange). *(Add-in C# của ta có thể đã bọc chuyển đổi — xác nhận lại.)*

### A.2 Enum Inventor 2026 (khác tài liệu chính thức)
```
DocumentTypeEnum
  kPartDocumentObject      = 12290     # KHÔNG phải 12289 như hay bị trích

PartFeatureExtentDirectionEnum
  kPositiveExtentDirection  = 20993
  kNegativeExtentDirection  = 20994
  kSymmetricExtentDirection = 20995

PartFeatureOperationEnum
  kJoinOperation      = 20481
  kCutOperation       = 20482
  kIntersectOperation = 20483
  kSurfaceOperation   = 20484
  kNewBodyOperation   = 20485

DimensionOrientationEnum   # docs nói 40706/40707/40708 — thực tế:
  kHorizontalDim = 19201
  kVerticalDim   = 19202
  kAlignedDim    = 19203

SurfaceTypeEnum (Face.SurfaceType)
  5890=Plane  5891=Cylinder  5892/5893=Cone  5894=Sphere  5895=Torus
```

### A.3 API 2026 đã đổi so với docs cũ
| Feature | Docs cũ / bị removed | Inventor 2026 dùng |
|---|---|---|
| Chamfer | `CreateChamferDefinition` (đã bỏ) | `ChamferFeatures.AddUsingDistance(EdgeCollection, dist_cm)` — dùng **EdgeCollection**, KHÔNG phải ObjectCollection. Cũng có `AddUsingDistanceAndAngle`, `AddUsingTwoDistances` |
| Revolve full 360° | `CreateRevolveDefinition`+`SetAngleExtent` | `RevolveFeatures.AddFull(profile, axisEntity, operation)` |
| Revolve partial | | `AddByAngle(profile, axis, angle_RAD, direction, operation)` |
| Revolve axis | construction line trong sketch | Ưu tiên WorkAxis: `comp.WorkAxes.Item(1/2/3)` = X/Y/Z, khỏi construction line |
| Hole (simple) | `CreateSimpleHoleDef` (không tồn tại 2026) | `HoleFeatures.CreateSketchPlacementDefinition(ObjectCollection<SketchPoint>)` rồi `AddDrilledByThroughAllExtent(pl, dia_or_TapInfo, dir)` / `AddDrilledByDistanceExtent(pl, …, depth_cm, dir)` |
| Hole (parametric) | | `CreateLinearPlacementDefinition(face, edge1, d1_cm, edge2, d2_cm, biasPoint)` — **biasPoint (Point3d) BẮT BUỘC**, disambiguate 4 vị trí khả dĩ |
| Counterbore | | `AddCBoreByDistanceExtent(pl, holeDia, depth, dir, cboreDia, cboreDepth)` — tất cả cm |

### A.4 Tapped hole — TapInfo
```
CreateTapInfo(True, "ISO Metric profile", "M16x2", "6H", True)
```
- ThreadType phải **đúng chính xác** tên sheet trong `Design Data\XLS\en-US\thread.xlsx` (vd `"ISO Metric profile"`).
- Designation: **pitch nguyên bỏ phần thập phân** → `"M16x2"` chứ không `"M16x2.0"`.
- TapInfo dùng được cho cả through-all lẫn distance extent.

### A.5 Quy tắc chống silent-failure (quan trọng cho self-check)
- **Luôn extrude body theo `positive`.** Body extrude `negative` khiến `hole()` through-all **tạo feature nhưng không bỏ vật liệu** (verify bằng volume). Đây là lỗi lặng #1.
- **Hole-placement sketch chỉ được chứa `SketchPoints.Add()`** — 1 construction line trong đó làm `AddDrilledByThroughAllExtent` fail `E_FAIL`.
- **Drill direction (pos/neg) không đoán được từ face normal.** Pattern: tạo → `doc.Update()` → check `MassProperties.Volume` giảm → nếu không, xoá feature, đổi chiều, thử lại.

### A.6 Sheet Metal — bẫy Flange
```
fdef = flf.CreateFlangeDefinition(edgeCollection, angle_RADIANS, distance)
feat = flf.Add(fdef)
feat.Definition.HeightExtent.Distance.Expression = "450 mm"   # ← chiều cao THỰC
doc.Update()
```
- Arg `distance` của `CreateFlangeDefinition` **bị bỏ qua âm thầm** — flange luôn ~25mm. Set chiều cao thực qua `.Distance.Expression` sau đó.
- Angle là **radian**. Truyền `90` → Inventor lưu 90·180/π ≈ 5157°.
- `HeightDatumType` mặc định 75521 = "From Outer Intersection": Distance = chiều cao ngoài nhìn thấy (không trừ thickness).
- **Cut** trên sheet metal: sketch **ON panel face** (không phải work plane) → depth mặc định = Thickness param → parametric. Tránh extrude-cut (hardcode depth).
- **Flat Pattern** là phép thử vàng: unfold được = sheet metal thật; extrude "walls" trông giống nhưng không unfold.

### A.7 Sketch-plane axis mapping
| Plane | sketch X | sketch Y |
|---|---|---|
| XY (WorkPlanes.Item(3)) | world +X | world +Y |
| XZ (Item 2) | **world −X (mirrored!)** | world +Z |
| YZ (Item 1) | world +Y | world +Z |

- Offset plane: `WorkPlanes.AddByPlaneAndOffset(base, offset_cm)`; offset theo pháp tuyến (XY→+Z, XZ→+Y, YZ→+X). Offset-from-XY map thẳng (x→X, y→Y).
- Sketch trên **face**: gốc ở góc face (không phải tâm), trục có thể lật. Verify bằng `sketch.SketchToModelSpace(tg.CreatePoint2d(x_cm,y_cm))`.

### A.8 Topology pitfalls (nền tảng cho identity-based self-check)
- **Edge/face index đánh số lại sau MỌI feature.** Re-find theo midpoint/centroid, **không cache index** qua các feature.
- **Fillet dịch cạnh lân cận** — fillet kéo dài/rút ngắn hàng xóm, midpoint đổi. Re-scan topology sau mỗi fillet.
- **Thứ tự feature vs cut**: vật liệu thêm SAU một cut sẽ **lấp** cut chỗ chồng lấn. Thêm rib dưới hub đã khoan? Dừng rib dưới đáy lỗ, hoặc reorder tree.
- **Đặt tên feature ngay** (`feat.Name = "Bore20"`): Inventor đánh số sketch/feature toàn cục, **không tái dùng số đã xoá** → auto-name "Sketch4" không đáng tin làm tham chiếu.

### A.9 Dimension trong sketch
- `AddDiameter(circle, textPt)` — thẳng.
- `AddTwoPointDistance(p1, p2, orientation, textPt)` — p1/p2 phải là **endpoint của line hoặc projected point**; `SketchPoints.Add()` đứng riêng → `E_UNEXPECTED (0x8000FFFF)`.
- Project gốc để dim từ đó: `sketch.AddByProjectingEntity(comp.WorkPoints.Item(1))`.
- Ưu tiên `GeometricConstraints.AddCoincident(center, projectedAxis)` + `AddSymmetry(l1,l2,axis)` hơn là dimension giá trị 0 → part fully-constrained, rebuild đúng khi đổi 1 kích thước.

---

## §B. Bảng dữ liệu chuẩn DIN/ISO (tái dùng thẳng)

### B.1 DIN 934 / ISO 4032 — Hex Nut
Helper: `circumradius = s/√3`, `apothem = s/2`.

| Thread | s (across flats) | m (height) | Coarse pitch |
|---|---|---|---|
| M5  | 8  | 4.0  | 0.8  |
| M6  | 10 | 5.0  | 1.0  |
| M8  | 13 | 6.5  | 1.25 |
| M10 | 16 | 8.0  | 1.5  |
| M12 | 18 | 10.0 | 1.75 |
| M14 | 21 | 11.0 | 2.0  |
| M16 | 24 | 13.0 | 2.0  |
| M18 | 27 | 15.0 | 2.5  |
| M20 | 30 | 16.0 | 2.5  |
| M22 | 32 | 18.0 | 2.5  |
| M24 | 36 | 19.0 | 3.0  |
| M27 | 41 | 22.0 | 3.0  |
| M30 | 46 | 24.0 | 3.5  |
| M36 | 55 | 29.0 | 4.0  |

### B.2 DIN 933 / ISO 4017 — Hex Bolt (fully threaded), DIN 931 (partial)
| Thread | s (head, across flats) | k (head height) | Lengths |
|---|---|---|---|
| M6  | 10 | 4.0  | 8…200 |
| M8  | 13 | 5.3  | 10…300 |
| M10 | 16 | 6.4  | 16…300 |
| M12 | 18 | 7.5  | 20…300 |
| M16 | 24 | 10.0 | 25…300 |
| M20 | 30 | 12.5 | 30…300 |
| M24 | 36 | 15.0 | 40…300 |
| M30 | 46 | 18.7 | 60…300 |

### B.3 DIN 125 / ISO 7089 — Flat Washer type A
| Thread | d1 (inner) | d2 (outer) | t (thickness) |
|---|---|---|---|
| M5  | 5.3  | 10 | 1.0 |
| M6  | 6.4  | 12 | 1.6 |
| M8  | 8.4  | 16 | 1.6 |
| M10 | 10.5 | 20 | 2.0 |
| M12 | 13.0 | 24 | 2.5 |
| M14 | 15.0 | 28 | 2.5 |
| M16 | 17.0 | 30 | 3.0 |
| M20 | 21.0 | 37 | 3.0 |
| M24 | 25.0 | 44 | 4.0 |
| M30 | 31.0 | 56 | 4.0 |

### B.4 Tap pitch chuẩn (coarse)
| Thread | Pitch | Thread | Pitch |
|---|---|---|---|
| M6  | 1.0  | M16 | 2.0 |
| M8  | 1.25 | M20 | 2.5 |
| M10 | 1.5  | M24 | 3.0 |
| M12 | 1.75 | M30 | 3.5 |
| M14 | 2.0  | M36 | 4.0 |

### B.5 Insight domain — chamfer DIN 934/933 KHÔNG dùng Chamfer feature
Inventor `Chamfer` bào **cùng độ rộng trên 12 cạnh hex** → 12 facet phẳng. Chamfer DIN thật là **một mặt CÔN**. Làm bằng `revolve(axis="Z", operation="cut")` với profile tam giác trên mặt XZ.
→ **Signature self-check:** nut đúng có `Face types: Cone: 2` (đỉnh + đáy). Đây chính là một assertion known-good.

> Lưu ý ren ngoài: Inventor **không render ren xoắn ngoài** qua API MCP này — bolt là "blank stud". Ren để cosmetic thread ở drawing view, không phải trên part 3D.

---

## §C. pywin32-only — KHÔNG áp cho C# add-in (chỉ để hiểu bối cảnh)

- **gencache phá GetActiveObject:** `gencache.EnsureDispatch(...)` tạo `gen_py` cache → sau đó `GetActiveObject` fail `KeyError: '_dispobj_'` cho tới khi xoá `%LOCALAPPDATA%\Temp\gen_py`. (Vấn đề của pywin32 late/early binding — C# dùng interop typed, không dính.)
- **Unicode console:** `python -X utf8` khi print Ø/×/Cyrillic. (N/A cho C#.)
- **`doc.SaveAs(path)` fail `E_INVALIDARG` nếu file đang mở cùng tên** → dùng `doc.Save()` cho re-save. *(Đây là COM-level, CÓ THỂ áp cho C# — nên cross-check trong add-in.)*
- **Transaction:** `app.TransactionManager.StartTransaction(doc, "name")` → `.End()` / `.Abort()` gói nhiều feature thành 1 undo unit, rollback tin cậy. *(Áp được cho C# — nền tảng cho `inventor_transaction` đề xuất.)*

---

## §D. Self-check signatures (known-good để assert tự động)
Ý tưởng: cho autonomous agent một bảng "postcondition kỳ vọng" theo op để so với telemetry.

| Op | Kỳ vọng trên delta/topology |
|---|---|
| extrude join | `ΔV > 0` |
| extrude cut / revolve cut / hole through-all | `ΔV < 0` **và** edge count đổi |
| fillet | face & edge count đổi; `ΔV` nhỏ (thường < 0) |
| chamfer | face & edge count đổi; xuất hiện mặt mới |
| DIN 934 nut hoàn chỉnh | `Cone: 2` trong face types; volume ≈ giá trị chuẩn ± dung sai |
| revolve cut không tác dụng | profile sai phía Z (nằm trong không khí) → lật dấu Y của profile |

---

*Attribution: nội dung §A/§B/§C trích & biên tập từ NeonGlay/inventor-mcp (MIT). §D là khung self-check do team Bimwright tổng hợp.*
