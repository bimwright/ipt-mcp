<!-- mcp-name: io.github.bimwright/ipt-mcp -->

<h1 align="center">ipt-mcp</h1>

<p align="center">
  <a href="https://github.com/bimwright/ipt-mcp/actions/workflows/build.yml"><img src="https://github.com/bimwright/ipt-mcp/actions/workflows/build.yml/badge.svg" alt="build" /></a>
  <a href="LICENSE"><img src="https://img.shields.io/badge/license-Apache%202.0-blue.svg" alt="license" /></a>
  <a href="#サポート対象-inventor-バージョン"><img src="https://img.shields.io/badge/Inventor-2022--2027-F5A300" alt="Inventor 2022-2027" /></a>
  <a href="#ツール一覧"><img src="https://img.shields.io/badge/MCP-111%20tools-6C47FF" alt="MCP tools" /></a>
</p>

<p align="center">
  <a href="README.md">English</a> · <a href="README.vi.md">Tiếng Việt</a> · <a href="README.zh-CN.md">简体中文</a> · 日本語
</p>

---

`ipt-mcp` は、Claude Code および MCP 対応クライアントが **Autodesk Inventor 2022-2027** をローカルで操作できるようにする、オープンソース（[Apache-2.0](LICENSE)）の [Model Context Protocol](https://modelcontextprotocol.io) ゲートウェイです。

エージェントは stdio 経由で MCP を通信します。サーバーはローカルの認証付きトランスポート（TCP または Named Pipe）を介して NDJSON を、バージョン別のインプロセス Inventor アドインに転送します。アドインはすべてのコマンドを Inventor の STA スレッドにマーシャリングし、Inventor API とやり取りします。

モデルはユーザーのマシン上に留まります。

---

## ipt-mcp の概要

2 つのプロセス、1 つのローカルパイプ:

- **`Bimwright.Ipt.Server.exe`** — Claude Code、Cursor、Cline、Codex、または他の stdio MCP クライアントによって起動される .NET 8 MCP stdio サーバーです。**Inventor への参照は一切持たず**、API に依存しないコントラクトファイルのみをコンパイルするため、.NET 8 SDK がインストールされた任意のマシンでビルドおよび実行できます。
- **`Bimwright.Ipt.Plugin.InvNN.dll`** — `Inventor.exe` 内部にロードされる `ApplicationAddInServer` アドインで、TCP または Named Pipe のリスナーを実行し、Inventor のメイン UI（STA）スレッド上でコマンドを実行します。Inventor のバージョンごとに 1 つの薄いシェルを持ち、すべて同じ `src/shared/**` のソースグロブからコンパイルされます。

Revit とは異なり、Inventor には **`ExternalEvent` に相当する機能がありません**。アドインは隠しメッセージオンリーの WinForms コントロール（`InventorStaDispatcher`）を介して STA スレッドに処理をマーシャリングします。詳細な設計は [ARCHITECTURE.md](ARCHITECTURE.md) を参照してください。

---

## サポート対象 Inventor バージョン

| Inventor | ターゲットフレームワーク | トランスポート | 備考 |
|----------|------------------|-----------|-------|
| 2022 | `net48` (.NET Framework 4.8) | TCP | `System.Windows.Forms` を直接参照 |
| 2023 | `net48` (.NET Framework 4.8) | TCP | |
| 2024 | `net48` (.NET Framework 4.8) | TCP | |
| 2025 | `net8.0-windows7.0` | Named Pipe | `UseWindowsForms`、`EnableDynamicLoading` |
| 2026 | `net8.0-windows7.0` | Named Pipe | |
| 2027 | `net10.0-windows7.0` | Named Pipe | .NET 10 SDK が必要。`UseInventorAssemblyContext` を適用 |

- MCP サーバーは 1 つのプロセスであり、**Inventor のバージョンの影響を受けません** — JSON エンベロープを転送するだけです。
- 2022-2024（net48 アドイン）は TCP、2025-2027 は Named Pipe を使用します。Named Pipe により、モダン Windows でのループバックファイアウォールプロンプトを回避します。
- Inventor は 2025 年以降、デスクトップアドイン開発を .NET Framework から移行しました: **2025/2026 は .NET 8、2027 は .NET 10**。（.NET 8 アドインは 2027 でもバイナリ互換ですが、net10 がネイティブターゲットです。）
- すべての場所で **4 桁の西暦**（2022..2027）を使用してください — レガシーバージョンコードは使用しないでください。

> **ステータス: Drawing Phase 1 は実装中です。** Inventor 2027 向け drawing tools を 11 個追加しました。ホスト不要テストとライブ fixture の基本検証は成功しました。完全な受け入れ検証と release gate は保留中です。[検証状況](docs/testing/drawing-phase1.md)。

最終 MCP 結果の UTF-8 出力ガードは 64/256 KiB で警告し、1 MiB を上限とします。大きな読み取りは絞り込みを要求し、完了した書き込みは結果を要約します。再実行しないでください。`--disable-output-guard` でも転送上限は有効です。CLI/JSON/環境変数と既定 36 時間の spill 設定は[テスト文書](docs/testing/drawing-phase1.md)を参照してください。

---

## インストール / MCP クライアントの設定

[GitHub Releases](https://github.com/bimwright/ipt-mcp/releases/latest) から `IptMcp.Setup-*-win-x64.zip` を入手。v0.1.0 は Inventor **2025** と **2027**。展開して `install.ps1`。MCP は `ipt-mcp.exe`。`dotnet tool install -g Bimwright.Ipt.Server` は使わないでください。

`inventor_send_code` はデフォルトで有効です。`--disable-send-code` は code ツールを非表示にし、`BIMWRIGHT_INVENTOR_PLUGIN_DISABLE_SEND_CODE=1` はホストで実行を停止します。read-only モードでは実行できません。

---

## ビルドと開発

Autodesk Inventor のバイナリおよび Inventor SDK は、このリポジトリには**再配布されていません**（[再配布しないもの](#再配布しないもの) を参照）。**サーバーとテスト**のビルドには .NET 8 SDK のみが必要です。**バージョン別アドイン**のビルドには、対応する SDK に加えて Inventor の相互運用参照アセンブリが必要です。

```bash
# サーバー + テスト（サーバーのみ、Inventor 不要 — .NET 8 SDK がインストールされた任意のマシンで動作）:
dotnet build src/IptMcp.sln -c Debug
dotnet test  tests/Bimwright.Ipt.Tests -c Debug

# インストール済みの 2027 相互運用参照を使用したレガシー TFM 互換性チェック:
dotnet build src/plugin-inv24 -c Debug /p:InventorInteropDir="C:\Program Files\Common Files\Autodesk Shared\Extensions 2027\Framework\Interop"
dotnet build src/plugin-inv27 -c Debug   # 実際の 2027 相互運用コンパイル。.NET 10 SDK が必要

# バージョン別アドインには常に Inventor 相互運用参照が必要です。レガシー TFM 互換性
# チェックの場合は、InventorInteropDir にインストール済みの互換性のある相互運用アセンブリを指定してください。
# 実際のリリースビルドでは、対応するバージョンのデフォルトパスを使用します。
```

- **サーバー** は明示的に `shared/Contracts/*` + `shared/Security/*`（+ ToolBaker）のみをインクルードするため、Inventor SDK がなくてもコンパイルできます。
- 各 **アドイン** は `<Compile Include="..\shared\**\*.cs" />` を使用して、API に触れる `Infrastructure`/`Plugin`/`Handlers` を含むすべてをインクルードします。
- デフォルトの相互運用ヒントパスは `C:\Program Files\Common Files\Autodesk Shared\Extensions <year>\Framework\Interop\Autodesk.Inventor.Interop.dll` です。
- 2027 アドインのビルドには **.NET 10 SDK** のインストールが必要です。
- **アドイン DLL をデプロイする前に Inventor を閉じてください** — 閉じていないとファイルがロックされます。

---

## ツール一覧

全機能モードはデフォルトで **111 ツール**（`--toolsets all`）。`--disable-send-code` では **107 ツール**、`--read-only` では **28 ツール**です。MCP 名はすべて `inventor_` で始まり、toolset フィルターで絞り込めます。

`code` を含む全 15 toolset がデフォルトで有効です。`--toolsets <csv>` で絞り込めます。

すべての長さ入力は **mm**、角度は **度** 単位です。アドインが Inventor 内部のセンチメートル/ラジアンに変換します。


### drawing_query (2) / drawing (20) — Inventor 2027

Earlier Inventor 2027 fixture results: [Phase 3 tool behavior](docs/testing/drawing-phase3.md). These records predate v0.2.1 runtime changes; live acceptance must be renewed.

`inventor_get_drawing_info` and `inventor_find_view_geometry` are read-only. Drawing writes:
`inventor_new_drawing`, `inventor_add_sheet`, `inventor_set_title_block`,
`inventor_add_drawing_view`, `inventor_add_section_view`, `inventor_edit_drawing_view`,
`inventor_add_drawing_dimension`, `inventor_add_balloon`, `inventor_export_drawing`,
`inventor_capture_sheet`, `inventor_add_drawing_note`, `inventor_add_drawing_table`,
`inventor_add_drawing_symbol`, `inventor_edit_drawing_annotation`, `inventor_delete_drawing_items`,
`inventor_edit_drawing_table`, `inventor_set_drawing_styles`, `inventor_edit_sheet`, `inventor_sketch_on_view`, `inventor_hide_view_edges`. Captures write PNG files and are excluded from read-only.

[Drawing checks and current limitations](docs/testing/drawing-phase1.md) ·
[Generic live smoke record](docs/benchmarks/drawing-phase1-smoke.json) ·
[Declared read-only inventory](docs/testing/readonly-tools.json).

### meta (3) — サーバーサイドターゲットツール。アドインへのラウンドトリップなし。`--read-only` でも公開

| ツール | 説明 |
|---|---|
| `inventor_list_available_targets` | 検出された稼働中の Inventor アドインターゲットを一覧表示（年、pid、トランスポート、アクティブドキュメント）。 |
| `inventor_get_current_target` | サーバーが現在選択しているターゲットを報告。稼働中でない場合は `NO_TARGET`。 |
| `inventor_switch_target` | ディスクリプタ ID、Inventor のバージョン年、プロセス ID、またはパイプ/セッション名でアクティブターゲットを選択。サーバーサイドのみ。 |

### query (7) — 読み取り専用のドキュメント/ヘルス/モデルプローブとタスク結果報告

| ツール | 説明 |
|---|---|
| `inventor_health` | アクティブなアドインをプローブ: inventor_year、process_id、ドキュメントが開いているか、アクティブドキュメントの種類。 |
| `inventor_report_task_result` | agent が `task_id`、`outcome`（`completed`/`failed`/`cancelled`）、1 行の `summary` で結果を明示的に報告。モデル変更なし。 |
| `inventor_list_open_documents` | 開いているすべてのドキュメントを一覧: タイトル、パス、種類、アクティブかどうか。 |
| `inventor_get_document_info` | アクティブドキュメントのタイトル、完全パス、ドキュメントの種類を取得。 |
| `inventor_list_bodies` | パートのソリッドボディを一覧: id（`body:N`）、名前、volume_mm3、bbox_mm、face_count、生成フィーチャ（created_by）、visible。 |
| `inventor_list_features` | パートのフィーチャをツリー順に一覧: 名前、種類、health、suppressed、body_names。 |
| `inventor_probe_brep` | パートの B-rep をポート口で調査: 内側ループ円エッジを持つ平面 — normal（IsParamReversed 補正済み）、center_mm、port_diameter_mm、面上の全円。 |

### document (10) — ドキュメントライフサイクル（書き込み）

| ツール | 説明 |
|---|---|
| `inventor_new_part` | 新しいパーツドキュメント（.ipt）を作成。テンプレートパスはオプション。 |
| `inventor_new_assembly` | 新しいアセンブリドキュメント（.iam）を作成。テンプレートパスはオプション。 |
| `inventor_open_document` | 完全パスから既存のドキュメントを開き、アクティブにする。 |
| `inventor_save_document` | アクティブドキュメントを保存、または指定されたパスに名前を付けて保存。 |
| `inventor_close_document` | アクティブドキュメントを閉じる。`save=true` で先に保存。 |
| `inventor_set_units` | アクティブドキュメントの長さ単位を設定（mm、cm、m、in、ft）。 |
| `inventor_set_material` | アクティブパーツに名前でマテリアルを割り当て。 |
| `inventor_save_all` | ルートドキュメントを更新し、変更のある参照ドキュメントと一緒にサイレント保存。ファイルごとに saved / read_only / error を報告。`dry_run` 対応。 |
| `inventor_open_documents` | 複数のドキュメントを一度に開く（既定はウィンドウなし）。 |
| `inventor_close_documents` | パス/名前指定、または表示中の全ドキュメントを閉じる（`keep_active`）。保存も可。 |

### parameters (4) — モデルパラメータとユーザーパラメータ（書き込み）

| ツール | 説明 |
|---|---|
| `inventor_list_parameters` | パラメータ（モデル + ユーザー）を一覧: 名前、式、値、単位、種類。 |
| `inventor_get_parameter` | 名前で 1 つのパラメータを取得: 式、値、単位。 |
| `inventor_set_parameter` | 既存のパラメータの式/値を設定し、ドキュメントを更新。 |
| `inventor_create_parameter` | 新しいユーザーパラメータを作成（名前、式、単位）。 |

### properties (4) — iProperties とマスプロパティ（書き込み）

| ツール | 説明 |
|---|---|
| `inventor_get_iproperty` | プロパティセット名とプロパティ名で iProperty 値を取得。 |
| `inventor_set_iproperty` | iProperty 値を設定。 |
| `inventor_get_mass_properties` | 質量（g）、体積（mm³）、表面積（mm²）、重心、境界ボックス。 |
| `inventor_list_iproperty_sets` | iProperty セットを列挙（name、internal_name、プロパティ名。オプションで値）— get/set_iproperty 用の discovery。 |

### sketch (10) — 2D スケッチ形状と拘束（書き込み）

| ツール | 説明 |
|---|---|
| `inventor_create_sketch` | 平面上に 2D スケッチを作成（XY/XZ/YZ、または面/作業平面参照）。 |
| `inventor_project_geometry` | モデルエッジ/頂点（エッジ ID 指定）をアクティブスケッチに投影。 |
| `inventor_draw_line` | (x1,y1) から (x2,y2) へのスケッチ線を描画。 |
| `inventor_draw_circle` | 中心点 + 半径からスケッチ円を描画。 |
| `inventor_draw_rectangle` | 2 点指定のスケッチ矩形を描画。 |
| `inventor_draw_arc` | スケッチ円弧を描画（中心、半径、開始/終了角度）。 |
| `inventor_add_sketch_dimension` | スケッチエンティティに駆動寸法拘束を追加。 |
| `inventor_add_sketch_constraint` | 幾何拘束を追加（一致、平行、接線、…）。 |
| `inventor_draw_text` | フィットテキストボックスを追加（position mm、任意の font_size_mm。rotation_deg は 90 の倍数のみ）。 |
| `inventor_close_sketch` | スケッチの編集を終了（スケッチ編集モードを終了）。 |

### feature (16) — ソリッドフィーチャと作業フィーチャ（書き込み）

| ツール | 説明 |
|---|---|
| `inventor_extrude` | 名前付きスケッチを押し出し（距離、結合/切断/交差、方向）。 |
| `inventor_revolve` | 名前付きスケッチを軸周りに回転（角度、操作）。 |
| `inventor_combine` | ソリッドボディをブール演算（ベース + ツールボディ、結合/切断/交差、keep_tool_bodies）。 |
| `inventor_batch_execute` | 最大 20 個のワイヤコマンドを 1 トランザクションで実行（1 つの undo、エラー時はロールバック）。 |
| `inventor_fillet` | 一定半径のエッジフィレット — `edgeIds` または `edges` セレクタ `{kind:circular, radius_mm?, center_mm?, on_body?, adjacent_surface_types?}`;`matched_edges` を返す。 |
| `inventor_chamfer` | モデルエッジに等距離の面取りを追加。 |
| `inventor_create_work_plane` | 作業平面を作成（オフセット、3 点、接線、または固定原点+軸）。 |
| `inventor_create_work_axis` | 作業軸を作成（2 点、エッジ、平面交差、面法線オフセット）。 |
| `inventor_create_work_point` | {x,y,z}/[x,y,z] mm に固定作業点を作成。コンストラクション点は命名不可（name_applied が報告）。 |
| `inventor_hole` | 決定論的に選択された平面に対して穴あけ/皿穴/ざぐり穴を作成。タップねじメタデータはオプション。 |
| `inventor_circular_pattern` | 指定軸周りにパーツフィーチャを円形パターン（角度あたりの数）。 |
| `inventor_rectangular_pattern` | 1 つまたは 2 つの指定軸に沿ってパーツフィーチャを矩形パターン。 |
| `inventor_loft` | 順序付きスケッチプロファイル群（'SketchName' または 'SketchName:N'）をロフト。オプションでセンターラインスケッチ、closed/merge-tangent-faces。 |
| `inventor_sweep` | スケッチプロファイルをスケッチパスに沿ってスイープ（接続セグメントは自動チェーン）。orientation normal_to_path\|parallel。 |
| `inventor_create_bim_connector` | 円形ポートエッジ上に BIM パイプコネクタを作成（ref は inventor_probe_brep の `circles[].edge`）。kind=pipe、任意で system/flow/connection メタデータ。 |
| `inventor_create_part` | JSON レシピから部品を 1 回の呼び出しで作成：スケッチ（矩形 / 円 / バルジ付きポリライン、内側の穴）→ 押し出し / 穴 / フィレット / 面取り → 材料 + iProperty → サイレント Save-As。エラーはレシピのパスを示し、失敗時は部品を保存せず閉じる。`dry_run` 対応。 |

### export (10) — ビューキャプチャと形状エクスポート（書き込み）

> `output_path` は許可されたルート配下に置く必要があります：ユーザープロファイル、`%TEMP%`、または追加したルート — 例: Inventor マシンで `BIMWRIGHT_INVENTOR_EXPORT_ROOT=D:\Inventor-Exports` を設定（設定後に Inventor と MCP クライアント/サーバーセッションを再起動）。

| ツール | 説明 |
|---|---|
| `inventor_capture_view` | アクティブビューを PNG ファイルとしてキャプチャ（`<export-root>\captures\` または出力パス）。`inline=true` でサイズ制限付き base64 PNG を返す。 |
| `inventor_export_step` | アクティブパーツ/アセンブリを STEP（.stp/.step）にエクスポート。 |
| `inventor_export_stl` | アクティブパーツ/アセンブリを STL（.stl）にエクスポート。 |
| `inventor_export_sat` | アクティブパーツ/アセンブリを ACIS SAT（.sat）にエクスポート — Revit との interop フォーマット。acis_version は 7 がデフォルト（唯一のサポート値）。 |
| `inventor_export_dxf` | 2D DXF をエクスポート。ソース（`sketch` または `flat_pattern`）を指定する必要あり。 |
| `inventor_derive_envelope` | ソースのパーツ/アセンブリから derived part を作成 — envelope/interop パス：`derive_style`、`include_bodies` によるソリッド選択、軽量 `bounding_box` モード。許可ルート下に .ipt を保存。 |
| `inventor_view_fit` | アクティブビューをモデル範囲にズームフィット（キャプチャ前に実行）。 |
| `inventor_set_view_orientation` | 標準カメラ方向（iso/front/top/…）を設定し、マルチアングルキャプチャに対応。 |
| `inventor_set_camera` | カメラを明示的に配置（eye/target mm、up、perspective、extents_mm、fit）— 標準方向が合わない場合に capture_view の前に使用。 |
| `inventor_set_view_state` | デザインビューの有効化/作成、オブジェクト表示（作業フィーチャ等）、セレクタによるオカレンス表示切替。`inventor_capture_view` も同じキーと orientation/camera/fit、複数の `shots` を受け付ける。 |

> エクスポートパスは絶対パスで、許可された出力ルート（ユーザープロファイルまたは temp）の下にある必要があります。

### assembly (9、書き込み) — アセンブリの構成と編集

| ツール | 説明 |
|---|---|
| `inventor_create_design_view` | Copy an assembly design view; exact occurrence visibility/appearance settings, optional activation. Validated on an Inventor 2027 disposable fixture. |
| `inventor_place_occurrence` | コンポーネント（.ipt/.iam）をアクティブアセンブリに配置。初期姿勢と接地はオプション。 |
| `inventor_add_constraint` | 2 つの名前付き参照を拘束（mate/flush/insert/angle）。応答には `health` が含まれるため、常に確認してください。 |
| `inventor_create_imate` | 決定論的面セレクターを使用して、アクティブパーツに名前付き iMate を作成。 |
| `inventor_place_occurrences` | 複数コンポーネントを 1 つの元に戻す単位で配置。姿勢は原点+回転、軸、または 4×4 行列。`lock` = none / grounded / workplanes。 |
| `inventor_delete_occurrences` | セレクタに一致するトップレベルのオカレンスを削除（`dry_run`）。 |
| `inventor_set_occurrence_state` | 表示 / 抑制 / 固定 / 姿勢 / ロックを一括設定。 |
| `inventor_set_appearance` | オカレンスまたはボディに RGB 色やライブラリの外観を適用。 |
| `inventor_reset_appearance` | 外観の上書きを解除。 |

### assembly_query (6、読み取り専用) — 数値セルフチェックバッテリー。`--read-only` でも存続

| ツール | 説明 |
|---|---|
| `inventor_list_interfaces` | ドキュメントまたは 1 つのオカレンスの名前付きインターフェース（iMate、作業フィーチャ、原点形状）を一覧。 |
| `inventor_check_interference` | 干渉解析を実行。ペア数と合計/ペアごとの体積を返す。 |
| `inventor_measure_min_distance` | 2 つのオカレンスまたは名前付き参照間の最小 3D 距離（mm）。 |
| `inventor_get_assembly_bom` | BOM + オカレンスツリー（接地フラグと並進/回転の自由度を含む）。 |
| `inventor_list_constraints` | すべての拘束をタイプ、`health`、抑制フラグ、2 つのオカレンス名とともに読み取り。 |
| `inventor_list_occurrences` | セレクタで絞ったオカレンス一覧（path、file、bbox_mm、transform、表示、材料、外観、質量、体積）。`check_interference` は `set_a` × `set_b`、`measure_min_distance` は `pairs[]` や集合×集合 + `threshold_mm` に対応。 |

### code (4) — C# scripts and code modules

| ツール | 説明 |
|---|---|
| `inventor_send_code` | `inventor_send_code` はデフォルトで有効です。`--disable-send-code` は code ツールを非表示にし、`BIMWRIGHT_INVENTOR_PLUGIN_DISABLE_SEND_CODE=1` はホストで実行を停止します。read-only モードでは実行できません。 |
| `inventor_save_code_module` | 再利用可能な C# ヘルパーモジュールを保存（宣言のみ、保存前にポリシー検査とドライコンパイル）。 |
| `inventor_list_code_modules` | 保存済みモジュールとシグネチャを一覧。 |
| `inventor_delete_code_module` | モジュールを削除。 |

### toolbaker (3、読み取り専用) — サーバーサイドのベイクデータベースのみを操作

| ツール | 説明 |
|---|---|
| `inventor_list_baked_tools` | 検証済みでコンパイル・登録されたすべてのベイク済みツールを一覧。 |
| `inventor_list_bake_suggestions` | 繰り返しワークフローから生成されたアクティブな ToolBaker 提案を一覧。 |
| `inventor_create_bake_issue_draft` | 提案の GitHub Issue ドラフトを作成（送信はしない）。 |

### toolbaker_write (3、書き込み) — ベイク済みツールの実行と提案ライフサイクルの管理

| ツール | 説明 |
|---|---|
| `inventor_run_baked_tool` | 名前と JSON パラメータで登録済みのベイク済みツールを実行。 |
| `inventor_accept_bake_suggestion` | 提案を受け入れ: 検証 + コンパイル + 適用 + 永続化してベイク済みツールとして登録。 |
| `inventor_dismiss_bake_suggestion` | アクティブな提案を却下またはスヌーズ。 |

---

## Toast 通知

Inventor の表示中、コマンド結果は **1 枚のコンパクトな活動カード**に集約され、最新ツールと、数字がロールする **Success / Failed / Capture** カウンターを表示します。Capture は Success の内数で、`inventor_capture_view` の `inline=true` も含みます。操作総数に別途加算する値ではありません。Failed はソフト失敗や batch rollback も含みます。読み取り・書き込みとも強調色は青で、失敗を記録したカードは赤を維持します。このカードは rvt-mcp・dwg-mcp と同じもので、数値が変わってもレイアウトは安定し、優先順位付きスタックやツールごとの toast キューはありません。画像を保存できた capture が成功すると、その**サムネイル**が固定サイズの枠の中央に表示され、サムネイルをクリックするとファイルを開きます。件数はこの target 上でカードが保持される期間のもので、ジョブ全体や特定 MCP client の件数ではありません。`inventor_health` は toast を生成しません。**Agent connected** は状態通知で、カウンターを増やさず、保持中の活動カードやタスク報告を置き換えません。

活動カードは既定で **最後の結果から 20 秒**で消えます。**Status → Toast duration → Apply** で 10、20、30、60 秒を選択でき、`toastIdleSeconds` に保存されます。保存に失敗するとエラーを表示し、以前の時間を維持します。ポインターの移動を伴う実際のホバーで期限を一時停止し、離れると選択した時間を最初から数え直します。新しい時間は次の結果またはポインターが離れた時点から適用されます。静止したカーソルの下にカードが出現しただけではホバーと見なしません。カードをクリックすると **History** を開いてカードを閉じ、**×** はカードを閉じるだけです。カードのタイトルには、branding の設定に関係なくゲートウェイ名と **Inventor の年版**（`ipt-mcp 2027`）が常に表示されます。

ジョブ結果は agent が `inventor_report_task_result` で明示的に送ります。agent/job ごとに一意な `task_id`（1–80 文字）、`outcome`（`completed`/`failed`/`cancelled`）、正確な 1 行の `summary`（1–120 文字）が必要です。報告は **同じ共有スロットを置き換え**、**Agent reported** と表示し、有効期間は **8 秒**です。実際のホバーで一時停止し、離れると数え直します。活動カウンターには加算せず、次のツール結果で新しい活動カードが始まります。無通信時間や単一ツールの成功から完了を推測しません。Inventor API を呼ばずコマンドキューを迂回するため、長い `send_code` がメインスレッドを占有していても報告を受け付けます。`toast_shown` は表示用にカードを保持したかを示し、最小化やモーダルダイアログの解除待ちも含みます。Toasts 設定に従い、server と add-in の両方の更新が必要です。

Toast は **専用 STA UI スレッド上のコードのみで構築した WPF** を使用し、**所有者なし・非アクティブ化（unowned / no-activate）ウィンドウ**として動作します。Inventor のメイン STA から独立し、toast スレッドから Inventor COM を呼びません。コマンド結果の通知はレスポンス返却後に投稿します。フォーカスを奪わず、Inventor の最小化中やモーダルダイアログ表示中は隠れ、**他のアプリが前面でも最前面を維持**します。カードは rvt-mcp・dwg-mcp と同じ固定のライトスタイルで、テーマ設定はなく、カードの背後の画面を読み取ることもありません。

リボンから toast を切り替え：**Bimwright ▸ MCP → Toasts**（Status で診断ダイアログを開きます）。選択は `%LOCALAPPDATA%\Bimwright\ipt-mcp\iptmcp.config.json` の `enableToast` に永続化されます。環境変数 `BIMWRIGHT_INVENTOR_ENABLE_TOAST` は JSON 値を上書きします。廃止された `toastTheme` キーと `BIMWRIGHT_INVENTOR_TOAST_THEME` は無視されます。不正な形式の設定ファイルはそのまま残されます — トグルは上書きを拒否します。

同じリボンの **Toast Brand** は **デフォルト OFF** です。選択は同じファイルの `showBranding` に保存され、次回の Inventor 起動時に復元されます。有効にするとカード下部にワードマーク用の行を確保し、実際のホバーでそこに BIMwright ワードマークをワイプ表示し、離れるとフェードアウトします。**カード出現時の自動ワードマークワイプはありません**。動きは Windows のアニメーション設定に従います。Toast UI のラベルは英語のままで、README の翻訳は UI のローカライズを意味しません。

---

## 安全性

簡潔に言えば、モデルはユーザーのマシン上に留まり、書き込み/危険なツールは制限されます。

- **読み取り専用モード。** `--read-only` は `ReadOnly = true` のツールだけを登録し、ドキュメントやファイルへの書き込みを除外します。parameter/property の参照、view fit、code module 一覧は利用可能です。add-in 側も `BIMWRIGHT_INVENTOR_PLUGIN_READ_ONLY=1` / `BIMWRIGHT_INVENTOR_READ_ONLY=1` を適用します。
- **send_code.** `inventor_send_code` はデフォルトで有効です。`--disable-send-code` は code ツールを非表示にし、`BIMWRIGHT_INVENTOR_PLUGIN_DISABLE_SEND_CODE=1` はホストで実行を停止します。read-only モードでは実行できません。
- **ローカルで認証付きのトランスポート。** TCP はループバックにバインドされ、Named Pipe はローカルマシンスコープです。各セッションごとのディスクリプタにはランダムな認証トークンが含まれますが、MCP メタツールがそれを返すことはありません。
- **サニタイズされたエラー。** モデルに返されるエラーメッセージは、絶対パスやシークレットの漏洩を防ぐためにサニタイズされています。
- **ToolBaker の制御。** デフォルトで ToolBaker は有効化されています。サーバーの起動時に --disable-toolbaker コマンドラインフラグを渡すか、環境変数 BIMWRIGHT_INVENTOR_ENABLE_TOOLBAKER=0 を設定することで、完全に無効化できます。
- **許可されたエクスポートパス。** ファイルエクスポートツールは、output_path が安全なフォルダー（User Profile または Temp ディレクトリ内）を指していることを検証します。環境変数 BIMWRIGHT_INVENTOR_EXPORT_ROOT を設定することで、許可されたルートフォルダーを追加定義できます。
- **エラーとジャーナルの秘密情報マスク。** 引用符付きのキー/値形式の認証情報と `Bearer` トークンは常にマスクされます。さらに 24 文字以上の英数字の連続を秘密情報とみなしてマスクするヒューリスティックがあり、**既定で有効**です。長い識別子（COM 型名、パーツ名）を読める状態にしたい信頼済みマシンでは、ユーザー環境変数 `BIMWRIGHT_INVENTOR_MASK_LONG_TOKENS=0` を設定してください（サーバーとアドインの両方が読み取ります。Inventor と MCP クライアントを再起動）。

**ToolBaker** は、繰り返しのローカルワークフローを個人用の検証済みツールに変換します。提案は `inventor_list_bake_suggestions` で表示され、`inventor_accept_bake_suggestion`（検証 → コンパイル → 適用 → 永続化）で明示的に受け入れると、`inventor_list_baked_tools` / `inventor_run_baked_tool` で呼び出し可能になります。ベイクデータベースと監査ログは `%LOCALAPPDATA%\Bimwright\ipt-mcp\baked\` にローカルに保存されます。詳細は [docs/toolbaker.md](docs/toolbaker.md) および [SECURITY.md](SECURITY.md) を参照してください。

---

## 再配布しないもの

このプロジェクトは、Autodesk Inventor のバイナリや Inventor SDK / 相互運用 DLL を**再配布しません**。出荷されるサーバーと単体テストは、Inventor がインストールされていなくてもビルドおよび実行できます。バージョン別アドインのビルドには、**ローカルの Inventor インストール**、または対応する**相互運用参照アセンブリ**（`Autodesk.Inventor.Interop.dll`）が必要です。これらはデフォルトの Autodesk 共有拡張パス、または明示的な `/p:InventorInteropDir=...` MSBuild プロパティで指定します。Inventor に対するゲートウェイの実行には、ライセンスされた Inventor のインストールが必要です。

---

## bimwright ファミリー

AI アシスタントと BIM・CAD アプリケーションをつなぐオープンソースのツール。

**bimwright** は **BIM** と **wright** を組み合わせた名前です。wright は、ものを作る人や建てる人を表す古い英語で、*shipwright*（船大工）などに使われます。

- [**rvt-mcp**](https://github.com/bimwright/rvt-mcp) — Autodesk® Revit®
- [**dwg-mcp**](https://github.com/bimwright/dwg-mcp) — Autodesk® AutoCAD®
- [**nwd-mcp**](https://github.com/bimwright/nwd-mcp) — Autodesk® Navisworks®
- [**ipt-mcp**](https://github.com/bimwright/ipt-mcp) — Autodesk® Inventor®
- [**bim-wiki**](https://github.com/bimwright/bim-wiki) — ベトナム語優先の BIM 知識ベース

---

## ライセンス

[Apache-2.0](LICENSE)。[LICENSE](LICENSE) を参照してください。

Inventor および Autodesk は Autodesk, Inc. の登録商標です。bimwright は独立したオープンソースプロジェクトであり、Autodesk, Inc. とは提携、スポンサー、または推奨関係にありません。

## 権限と auto mode (Permissions & auto mode)

次の allow list は 28 個の read-only annotation から生成され、実際のサーバーとの一致をテストします。`ipt-mcp` はクライアントの正確な server ID に置き換えてください。`mcp__ipt-mcp__*` などサーバー全体のワイルドカードは使わないでください。`inventor_send_code` には annotation や毎回の確認を強制する metadata はなく、個別許可はユーザーの選択です。許可の保存は実際のクライアントで確認が必要です。

<!-- BEGIN GENERATED READONLY -->
```json
{
  "permissions": {
    "allow": [
      "mcp__ipt-mcp__inventor_check_interference",
      "mcp__ipt-mcp__inventor_create_bake_issue_draft",
      "mcp__ipt-mcp__inventor_find_view_geometry",
      "mcp__ipt-mcp__inventor_get_assembly_bom",
      "mcp__ipt-mcp__inventor_get_current_target",
      "mcp__ipt-mcp__inventor_get_document_info",
      "mcp__ipt-mcp__inventor_get_drawing_info",
      "mcp__ipt-mcp__inventor_get_iproperty",
      "mcp__ipt-mcp__inventor_get_mass_properties",
      "mcp__ipt-mcp__inventor_get_parameter",
      "mcp__ipt-mcp__inventor_health",
      "mcp__ipt-mcp__inventor_list_available_targets",
      "mcp__ipt-mcp__inventor_list_bake_suggestions",
      "mcp__ipt-mcp__inventor_list_baked_tools",
      "mcp__ipt-mcp__inventor_list_bodies",
      "mcp__ipt-mcp__inventor_list_code_modules",
      "mcp__ipt-mcp__inventor_list_constraints",
      "mcp__ipt-mcp__inventor_list_features",
      "mcp__ipt-mcp__inventor_list_interfaces",
      "mcp__ipt-mcp__inventor_list_iproperty_sets",
      "mcp__ipt-mcp__inventor_list_occurrences",
      "mcp__ipt-mcp__inventor_list_open_documents",
      "mcp__ipt-mcp__inventor_list_parameters",
      "mcp__ipt-mcp__inventor_measure_min_distance",
      "mcp__ipt-mcp__inventor_probe_brep",
      "mcp__ipt-mcp__inventor_report_task_result",
      "mcp__ipt-mcp__inventor_switch_target",
      "mcp__ipt-mcp__inventor_view_fit"
    ]
  }
}
```
<!-- END GENERATED READONLY -->

### Runtime settings (v0.2.1)

| Setting | Default | CLI / JSON |
|---|---|---|
| send_code | on | `--enable-send-code` / `--disable-send-code`; `enableSendCode` |
| Call-log files | off | `--enable-call-log` / `--disable-call-log`; `enableCallLog` |
| Toolsets | all | `--toolsets all`; `toolsets` |
| Read-only | off | `--read-only`; `readOnly` |
| Response guard | on | `--enable-output-guard` / `--disable-output-guard`; `enableOutputGuard` |
| Warning / strong warning / budget | 65536 / 262144 / 1048576 bytes | `--output-warning-bytes`, `--output-strong-warning-bytes`, `--output-budget-bytes`; `outputWarningBytes`, `outputStrongWarningBytes`, `outputBudgetBytes` |
| Transport cap | 5000000 bytes | `--max-response-bytes`; `maxResponseBytes` |
| Spill retention | 36 hours | `--spill-retention-hours`; `spillRetentionHours` (invalid values: 36) |

CLI overrides environment, which overrides `--config` JSON. The server's logging switch reaches the plug-in; `BIMWRIGHT_INVENTOR_PLUGIN_DISABLE_CALL_LOG=1` can veto it. History re-runs do not persist call logs. In-memory History is independent. Body caching (`BIMWRIGHT_CACHE_SEND_CODE_BODIES=1`) and body journaling (`BIMWRIGHT_PERSIST_SEND_CODE_BODIES=1`, TTL default 4 hours) are separate opt-ins; journaling also requires call logging. Enabled call logs keep source length/hash, never source bodies. Saved code modules are explicit user-requested storage; credential-like source values are rejected before compilation/storage, without rewriting the code.

`send_code` provides `app` and nullable `doc`, accepts a script body with `return` and optional helper declarations, and imports `System`, `System.Collections.Generic`, `System.Linq`, `Inventor`. Writes to the active document share one undo transaction; errors abort it and warnings are returned. New/closed documents, other documents and external files are outside that rollback scope. Oversized script output includes a file, preview, schema and `mutation_applied: null`; read the file and do not re-run the script. Spill files live under `%LOCALAPPDATA%\Bimwright\ipt-mcp\spill` and fresh files are never evicted by a count cap.

[ベンチマーク記録と検証範囲](docs/benchmarks/README.md)。

[v0.2.1 ライブベンチマーク: Inventor 2027、111 ツール](docs/benchmarks/v0.2.1-inventor-2027.md)。
