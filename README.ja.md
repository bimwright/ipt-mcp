<!-- mcp-name: io.github.bimwright/ipt-mcp -->

<h1 align="center">ipt-mcp</h1>

<p align="center">
  <a href="https://github.com/bimwright/ipt-mcp/actions/workflows/build.yml"><img src="https://github.com/bimwright/ipt-mcp/actions/workflows/build.yml/badge.svg" alt="build" /></a>
  <a href="LICENSE"><img src="https://img.shields.io/badge/license-Apache%202.0-blue.svg" alt="license" /></a>
  <a href="#supported-inventor-versions"><img src="https://img.shields.io/badge/Inventor-2022--2027-F5A300" alt="Inventor 2022-2027" /></a>
  <a href="#tool-surface"><img src="https://img.shields.io/badge/MCP-59%20tools-6C47FF" alt="MCP tools" /></a>
</p>

<p align="center">
  English · <a href="README.vi.md">Tiếng Việt</a>
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

> **ステータス: 検証済み。** フェーズ 1〜3 は完了し、健全です（59 の MCP ツール、サーバーおよびテストは Inventor がインストールされていなくてもビルド可能）。Inventor API ハンドラーは実際の Inventor セッションに対して動作確認済みです。本番モデルで使用する前に、ご自身のテンプレートでテストすることをお勧めします。

---

## インストール / MCP クライアントの設定

サーバーはプレーンな stdio MCP プロセスです。ビルドされた `Bimwright.Ipt.Server.exe`（または `dotnet run`）をクライアントから指定し、Inventor 内にアドインをロードしてください。

典型的な `.mcp.json`（または同等のクライアント設定）のエントリ:

```json
{
  "mcpServers": {
    "ipt-mcp": {
      "command": "D:\\path\\to\\src\\server\\bin\\Debug\\net8.0\\Bimwright.Ipt.Server.exe",
      "args": []
    }
  }
}
```

アドインの発見は自動で行われます。実行中の各 Inventor アドインは、インスタンスごとのディスクリプタを `%LOCALAPPDATA%\Bimwright\ipt-mcp\inventor-<year>-<pid>.json` に書き込みます。サーバーはこれらをスキャンし、停止済み/古いディスクリプタをベストエフォートで削除して接続します。MCP ターゲット一覧ツールはディスクリプタの `auth_token` をマスクします。複数の Inventor が起動している可能性がある場合は、`inventor_list_available_targets` を呼び出してから `inventor_switch_target` を使用してください。

---

## ビルドと開発

Autodesk Inventor のバイナリおよび Inventor SDK は、このリポジトリには**再配布されていません**（[再配布しないもの](#not-redistributed) を参照）。**サーバーとテスト**のビルドには .NET 8 SDK のみが必要です。**バージョン別アドイン**のビルドには、対応する SDK に加えて Inventor の相互運用参照アセンブリが必要です。

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

すべてのプラットフォームツールセットが有効な場合の全面は **59 ツール**です。すべての MCP 公開名は `inventor_` プレフィックスが付きます。ツールはツールセットクラスにグループ化されており、`--toolsets sketch,feature` および `--read-only` によって登録を制御できるため、性能の低いモデルでも無効なツールが表示されることはありません。

デフォルトで有効なツールセット: `meta`、`query`、`document`、`parameters`、`properties`、`sketch`、`feature`、`export`、`assembly`、`assembly_query`、`toolbaker`、`toolbaker_write`。
デフォルトで無効: `code`（`send_code` 脱出ハッチ — オプトインのみ）。

すべての長さ入力は **mm**、角度は **度** 単位です。アドインが Inventor 内部のセンチメートル/ラジアンに変換します。

### meta (3) — サーバーサイドターゲットツール。アドインへのラウンドトリップなし。`--read-only` でも公開

| ツール | 説明 |
|---|---|
| `inventor_list_available_targets` | 検出された稼働中の Inventor アドインターゲットを一覧表示（年、pid、トランスポート、アクティブドキュメント）。 |
| `inventor_get_current_target` | サーバーが現在選択しているターゲットを報告。稼働中でない場合は `NO_TARGET`。 |
| `inventor_switch_target` | ディスクリプタ ID、Inventor のバージョン年、プロセス ID、またはパイプ/セッション名でアクティブターゲットを選択。サーバーサイドのみ。 |

### query (3) — 読み取り専用のドキュメント/ヘルスプローブ

| ツール | 説明 |
|---|---|
| `inventor_health` | アクティブなアドインをプローブ: inventor_year、process_id、ドキュメントが開いているか、アクティブドキュメントの種類。 |
| `inventor_list_open_documents` | 開いているすべてのドキュメントを一覧: タイトル、パス、種類、アクティブかどうか。 |
| `inventor_get_document_info` | アクティブドキュメントのタイトル、完全パス、ドキュメントの種類を取得。 |

### document (7) — ドキュメントライフサイクル（書き込み）

| ツール | 説明 |
|---|---|
| `inventor_new_part` | 新しいパーツドキュメント（.ipt）を作成。テンプレートパスはオプション。 |
| `inventor_new_assembly` | 新しいアセンブリドキュメント（.iam）を作成。テンプレートパスはオプション。 |
| `inventor_open_document` | 完全パスから既存のドキュメントを開き、アクティブにする。 |
| `inventor_save_document` | アクティブドキュメントを保存、または指定されたパスに名前を付けて保存。 |
| `inventor_close_document` | アクティブドキュメントを閉じる。`save=true` で先に保存。 |
| `inventor_set_units` | アクティブドキュメントの長さ単位を設定（mm、cm、m、in、ft）。 |
| `inventor_set_material` | アクティブパーツに名前でマテリアルを割り当て。 |

### parameters (4) — モデルパラメータとユーザーパラメータ（書き込み）

| ツール | 説明 |
|---|---|
| `inventor_list_parameters` | パラメータ（モデル + ユーザー）を一覧: 名前、式、値、単位、種類。 |
| `inventor_get_parameter` | 名前で 1 つのパラメータを取得: 式、値、単位。 |
| `inventor_set_parameter` | 既存のパラメータの式/値を設定し、ドキュメントを更新。 |
| `inventor_create_parameter` | 新しいユーザーパラメータを作成（名前、式、単位）。 |

### properties (3) — iProperties とマスプロパティ（書き込み）

| ツール | 説明 |
|---|---|
| `inventor_get_iproperty` | プロパティセット名とプロパティ名で iProperty 値を取得。 |
| `inventor_set_iproperty` | iProperty 値を設定。 |
| `inventor_get_mass_properties` | 質量（g）、体積（mm³）、表面積（mm²）、重心、境界ボックス。 |

### sketch (9) — 2D スケッチ形状と拘束（書き込み）

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
| `inventor_close_sketch` | スケッチの編集を終了（スケッチ編集モードを終了）。 |

### feature (9) — ソリッドフィーチャと作業フィーチャ（書き込み）

| ツール | 説明 |
|---|---|
| `inventor_extrude` | 名前付きスケッチを押し出し（距離、結合/切断/交差、方向）。 |
| `inventor_revolve` | 名前付きスケッチを軸周りに回転（角度、操作）。 |
| `inventor_fillet` | モデルエッジに一定半径のフィレットを追加。 |
| `inventor_chamfer` | モデルエッジに等距離の面取りを追加。 |
| `inventor_create_work_plane` | 作業平面を作成（オフセット、3 点、または接線）。 |
| `inventor_create_work_axis` | 作業軸を作成（2 点、エッジ、平面交差、面法線オフセット）。 |
| `inventor_hole` | 決定論的に選択された平面に対して穴あけ/皿穴/ざぐり穴を作成。タップねじメタデータはオプション。 |
| `inventor_circular_pattern` | 指定軸周りにパーツフィーチャを円形パターン（角度あたりの数）。 |
| `inventor_rectangular_pattern` | 1 つまたは 2 つの指定軸に沿ってパーツフィーチャを矩形パターン。 |

### export (6) — ビューキャプチャと形状エクスポート（書き込み）

| ツール | 説明 |
|---|---|
| `inventor_capture_view` | アクティブビューをサイズ制限付き base64 PNG としてキャプチャ、または出力パスに書き込み。 |
| `inventor_export_step` | アクティブパーツ/アセンブリを STEP（.stp/.step）にエクスポート。 |
| `inventor_export_stl` | アクティブパーツ/アセンブリを STL（.stl）にエクスポート。 |
| `inventor_export_dxf` | 2D DXF をエクスポート。ソース（`sketch` または `flat_pattern`）を指定する必要あり。 |
| `inventor_view_fit` | アクティブビューをモデル範囲にズームフィット（キャプチャ前に実行）。 |
| `inventor_set_view_orientation` | 標準カメラ方向（iso/front/top/…）を設定し、マルチアングルキャプチャに対応。 |

> エクスポートパスは絶対パスで、許可された出力ルート（ユーザープロファイルまたは temp）の下にある必要があります。

### assembly (3、書き込み) — 座標ではなく関係によってアセンブリを構成

| ツール | 説明 |
|---|---|
| `inventor_place_occurrence` | コンポーネント（.ipt/.iam）をアクティブアセンブリに配置。初期姿勢と接地はオプション。 |
| `inventor_add_constraint` | 2 つの名前付き参照を拘束（mate/flush/insert/angle）。応答には `health` が含まれるため、常に確認してください。 |
| `inventor_create_imate` | 決定論的面セレクターを使用して、アクティブパーツに名前付き iMate を作成。 |

### assembly_query (5、読み取り専用) — 数値セルフチェックバッテリー。`--read-only` でも存続

| ツール | 説明 |
|---|---|
| `inventor_list_interfaces` | ドキュメントまたは 1 つのオカレンスの名前付きインターフェース（iMate、作業フィーチャ、原点形状）を一覧。 |
| `inventor_check_interference` | 干渉解析を実行。ペア数と合計/ペアごとの体積を返す。 |
| `inventor_measure_min_distance` | 2 つのオカレンスまたは名前付き参照間の最小 3D 距離（mm）。 |
| `inventor_get_assembly_bom` | BOM + オカレンスツリー（接地フラグと並進/回転の自由度を含む）。 |
| `inventor_list_constraints` | すべての拘束をタイプ、`health`、抑制フラグ、2 つのオカレンス名とともに読み取り。 |

### code (1) — オプトイン脱出ハッチ（デフォルトで OFF）

| ツール | 説明 |
|---|---|
| `inventor_send_code` | **危険、オプトインのみ。** C# スニペットをインプロセスで `Inventor.Application` に対して実行。サーバーとアドインの両方がオプトインしない限り無効（それ以外の場合は `SEND_CODE_DISABLED`）。禁止 API（ファイル/プロセス/ネットワーク/環境）は拒否。 |

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

## 安全性

簡潔に言えば、モデルはユーザーのマシン上に留まり、書き込み/危険なツールは制限されます。

- **読み取り専用モード。** `--read-only`（または `BIMWRIGHT_INVENTOR_READ_ONLY=1`）は、すべての書き込み可能ツールセット（`document`、`parameters`、`properties`、`sketch`、`feature`、`export`、`assembly`、`code`、`toolbaker_write`）を削除しますが、`meta` + `query` + `assembly_query` + 読み取り専用 `toolbaker` は維持し、**`inventor_switch_target` は公開したままにします**。サーバーは各コマンドエンベロープに読み取り専用モードを送信します。アドインも `BIMWRIGHT_INVENTOR_PLUGIN_READ_ONLY=1` / `BIMWRIGHT_INVENTOR_READ_ONLY=1` を尊重します。強制された読み取り専用下での書き込みコマンドは `READ_ONLY` を返します。
- **send_code の二方向オプトイン。** `inventor_send_code` は**デフォルトで無効**です。サーバー側で `--enable-send-code`（または `BIMWRIGHT_INVENTOR_ENABLE_SEND_CODE=1`）**かつ**アドインプロセス側で `BIMWRIGHT_INVENTOR_PLUGIN_ENABLE_SEND_CODE=1` の**両方**が設定された場合のみ公開されます。それ以外の場合、ディスパッチャーは `SEND_CODE_DISABLED` を返します。禁止 API（ファイル/プロセス/ネットワーク/環境）は拒否されます。
- **ローカルで認証付きのトランスポート。** TCP はループバックにバインドされ、Named Pipe はローカルマシンスコープです。各セッションごとのディスクリプタにはランダムな認証トークンが含まれますが、MCP メタツールがそれを返すことはありません。
- **サニタイズされたエラー。** モデルに返されるエラーメッセージは、絶対パスやシークレットの漏洩を防ぐためにサニタイズされています。

**ToolBaker** は、繰り返しのローカルワークフローを個人用の検証済みツールに変換します。提案は `inventor_list_bake_suggestions` で表示され、`inventor_accept_bake_suggestion`（検証 → コンパイル → 適用 → 永続化）で明示的に受け入れると、`inventor_list_baked_tools` / `inventor_run_baked_tool` で呼び出し可能になります。ベイクデータベースと監査ログは `%LOCALAPPDATA%\Bimwright\ipt-mcp\baked\` にローカルに保存されます。詳細は [docs/toolbaker.md](docs/toolbaker.md) および [SECURITY.md](SECURITY.md) を参照してください。

---

## 再配布しないもの

このプロジェクトは、Autodesk Inventor のバイナリや Inventor SDK / 相互運用 DLL を**再配布しません**。出荷されるサーバーと単体テストは、Inventor がインストールされていなくてもビルドおよび実行できます。バージョン別アドインのビルドには、**ローカルの Inventor インストール**、または対応する**相互運用参照アセンブリ**（`Autodesk.Inventor.Interop.dll`）が必要です。これらはデフォルトの Autodesk 共有拡張パス、または明示的な `/p:InventorInteropDir=...` MSBuild プロパティで指定します。Inventor に対するゲートウェイの実行には、ライセンスされた Inventor のインストールが必要です。

---

## bimwright ファミリー

AEC ツールチェーンのための手作りの MCP ゲートウェイ — 単一のアーキテクチャ、予測可能/監査可能/可逆的:

- [**rvt-mcp**](https://github.com/bimwright/rvt-mcp) — Autodesk® Revit®
- [**dwg-mcp**](https://github.com/bimwright/dwg-mcp) — Autodesk® AutoCAD®
- [**nwd-mcp**](https://github.com/bimwright/nwd-mcp) — Autodesk® Navisworks®
- [**ipt-mcp**](https://github.com/bimwright/ipt-mcp) — Autodesk® Inventor®
- [**bim-wiki**](https://github.com/bimwright/bim-wiki) — ベトナム語優先の BIM 知識ベース

---

## ライセンス

[Apache-2.0](LICENSE)。[LICENSE](LICENSE) を参照してください。

Inventor および Autodesk は Autodesk, Inc. の登録商標です。bimwright は独立したオープンソースプロジェクトであり、Autodesk, Inc. とは提携、スポンサー、または推奨関係にありません。
