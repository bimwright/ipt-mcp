<!-- mcp-name: io.github.bimwright/ipt-mcp -->

<p align="center">
  <img src="https://raw.githubusercontent.com/bimwright/.github/master/assets/logos/ipt-mcp.png" alt="ipt-mcp" width="180" />
</p>

<h1 align="center">ipt-mcp</h1>

<p align="center">
  <a href="https://github.com/bimwright/ipt-mcp/actions/workflows/build.yml"><img src="https://github.com/bimwright/ipt-mcp/actions/workflows/build.yml/badge.svg" alt="build" /></a>
  <a href="LICENSE"><img src="https://img.shields.io/badge/license-Apache%202.0-blue.svg" alt="license" /></a>
  <a href="#支持的-inventor-版本"><img src="https://img.shields.io/badge/Inventor-2022--2027-F5A300" alt="Inventor 2022-2027" /></a>
  <a href="#工具面"><img src="https://img.shields.io/badge/MCP-84%20or%2088%20tools-6C47FF" alt="MCP tools" /></a>
</p>

<p align="center">
  <a href="README.md">English</a> · <a href="README.vi.md">Tiếng Việt</a> · 简体中文 · <a href="README.ja.md">日本語</a>
</p>

---

`ipt-mcp` 是一个开源（[Apache-2.0](LICENSE)）的 [Model Context Protocol](https://modelcontextprotocol.io) gateway，让 Claude Code —— 以及任何 MCP-capable client —— 在本地驱动 **Autodesk Inventor 2022-2027**。

Agent 通过 stdio 说 MCP。Server 通过一个本地、经过认证的 transport（TCP 或 Named Pipe）向每个 Inventor 版本对应的进程内 add-in 发送 NDJSON。Add-in 把每条命令 marshals 到 Inventor 的 STA thread 上，并与 Inventor API 通信。

你的模型留在你的机器上。

---

## ipt-mcp 是什么

两个进程，一条本地管道：

- **`Bimwright.Ipt.Server.exe`** —— 一个 .NET 8 MCP stdio server，由 Claude Code、Cursor、Cline、Codex 或其他 stdio MCP client 启动。它**没有 Inventor 引用**；它只 compile 与 API 无关的 contract 文件，因此只要有 .NET 8 SDK，就能在任何机器上 build 并运行。
- **`Bimwright.Ipt.Plugin.InvNN.dll`** —— 一个 `ApplicationAddInServer` add-in，加载在 `Inventor.exe` 内部，运行一个 TCP 或 Named-Pipe listener，并在 Inventor 主 UI（STA）thread 上执行命令。每个 Inventor 年份一个薄 shell，全部从同一份 `src/shared/**` source glob compile。

与 Revit 不同，Inventor **没有 `ExternalEvent`** 等价物。Add-in 通过一个隐藏的、仅消息的 WinForms control（`InventorStaDispatcher`）把工作 marshals 到 STA thread 上。完整设计见 [ARCHITECTURE.md](ARCHITECTURE.md)。

---

## 支持的 Inventor 版本

| Inventor | Target Framework | Transport | 备注 |
|----------|------------------|-----------|------|
| 2022 | `net48` (.NET Framework 4.8) | TCP | 直接引用 `System.Windows.Forms` |
| 2023 | `net48` (.NET Framework 4.8) | TCP | |
| 2024 | `net48` (.NET Framework 4.8) | TCP | |
| 2025 | `net8.0-windows7.0` | Named Pipe | `UseWindowsForms`、`EnableDynamicLoading` |
| 2026 | `net8.0-windows7.0` | Named Pipe | |
| 2027 | `net10.0-windows7.0` | Named Pipe | 需要 .NET 10 SDK；支持 `UseInventorAssemblyContext` |

- MCP server 是一个独立进程，**不受 Inventor 版本影响** —— 它只转发 JSON envelope。
- 2022-2024 用 TCP（net48 add-in）；2025-2027 用 Named Pipe —— Named Pipe 避免了现代 Windows 上的 loopback-firewall 弹窗。
- Inventor 从 2025 起把桌面 add-in 开发从 .NET Framework 上移开：**2025/2026 用 .NET 8，2027 用 .NET 10**。 （.NET 8 add-in 在 2027 上仍二进制兼容，但 net10 是原生目标。）
- 全程使用**4-digit calendar years**（2022..2027）—— 永远不要使用 legacy 版本号。

> **状态：已验证。** Phase 1-3 已完成且全绿（默认 84 个 MCP tools，启用 send_code 时为 88 个；server + tests 在没有 Inventor 时也能 build），Inventor-API handlers 已在真实 Inventor session 中验证。和往常一样，在你的 production models 上信任它之前，请先用你自己的 templates 测试。

---

## 安装 / 接入 MCP Client

从 [GitHub Releases](https://github.com/bimwright/ipt-mcp/releases/latest) 下载 `IptMcp.Setup-*-win-x64.zip`。v0.1.0 含 Inventor **2025** 与 **2027**。解压后 `install.ps1`，MCP 指向 `ipt-mcp.exe`。不要 `dotnet tool install -g Bimwright.Ipt.Server`。

发现文件：`%LOCALAPPDATA%\Bimwright\ipt-mcp\inventor-<year>-<pid>.json`。`inventor_send_code` 需双侧 opt-in——见 [安全](#安全)。

---

## 构建与开发

Autodesk Inventor 二进制文件和 Inventor SDK 在本仓库中**不重新分发**（见 [不重新分发](#不重新分发)）。Build **server 和 tests** 只需要 .NET 8 SDK；build 一个**按版本的 add-in** 需要匹配的 SDK 加上 Inventor interop reference assembly。

```bash
# Server + tests（仅 server；不需要 Inventor —— 只要有 .NET 8 SDK 的机器即可运行）：
dotnet build src/IptMcp.sln -c Debug
dotnet test  tests/Bimwright.Ipt.Tests -c Debug

# 使用已安装的 2027 interop reference 做 legacy TFM 兼容性检查：
dotnet build src/plugin-inv24 -c Debug /p:InventorInteropDir="C:\Program Files\Common Files\Autodesk Shared\Extensions 2027\Framework\Interop"
dotnet build src/plugin-inv27 -c Debug   # 真实 2027 interop compile；需要 .NET 10 SDK

# 按版本的 add-in 始终需要 Inventor interop reference。若做 legacy TFM 兼容性
# 检查，把 InventorInteropDir 指向某个已安装的兼容 interop；真实 release build
# 使用对应年份的默认路径。
```

- **Server** 只显式 include `shared/Contracts/*` + `shared/Security/*`（+ ToolBaker），因此在没有 Inventor SDK 时也能 compile。
- 每个 **add-in** 使用 `<Compile Include="..\shared\**\*.cs" />` 拉入所有内容，包括触碰 API 的 `Infrastructure`/`Plugin`/`Handlers`。
- 默认 interop hint 路径是 `C:\Program Files\Common Files\Autodesk Shared\Extensions <year>\Framework\Interop\Autodesk.Inventor.Interop.dll`。
- Build add-in 2027 需要安装 **.NET 10 SDK**。
- **部署 add-in DLL 前请关闭 Inventor**，否则它会被锁住。

---

## 工具面

当所有平台 toolsets 都启用时，完整 surface 默认是 **84 个 tools**（12 个 default-on toolsets；`code` 关闭），启用 send_code toolset 时为 **88 个**（`inventor_send_code` + 3 个 code module tools）。每个面向 MCP 的名字都带有前缀 `inventor_`。Tools 按 toolset class 分组；`--toolsets sketch,feature` 和 `--read-only` 控制哪些被注册，这样弱模型就不会看到被禁用的 tools。

默认启用的 toolsets：`meta`、`query`、`document`、`parameters`、`properties`、`sketch`、`feature`、`export`、`assembly`、`assembly_query`、`toolbaker`、`toolbaker_write`。
默认关闭：`code`（即 `send_code` escape hatch —— 仅 opt-in）。

所有长度输入单位为 **mm**，角度单位为 **degrees**；add-in 会转换成 Inventor 内部的 centimetres/radians。

### meta (3) —— server-side 的 target tools，不 round-trip 到 add-in；在 `--read-only` 下仍然暴露

| Tool | 描述 |
|---|---|
| `inventor_list_available_targets` | 列出检测到的 live Inventor add-in targets（year、pid、transport、active document）。 |
| `inventor_get_current_target` | 报告 server 当前选中的 target，若没有 live 的则返回 `NO_TARGET`。 |
| `inventor_switch_target` | 按 descriptor id、Inventor year、process id 或 pipe/session 名选择 active target。仅 server-side。 |

### query (7) —— 只读的 document/health/model 探测及任务结果报告

| Tool | 描述 |
|---|---|
| `inventor_health` | 探测 active add-in：inventor_year、process_id、是否有 document 打开、active document type。 |
| `inventor_report_task_result` | agent 用 `task_id`、`outcome`（`completed`/`failed`/`cancelled`）及单行 `summary` 显式报告任务结果；不修改模型。 |
| `inventor_list_open_documents` | 列出所有打开的 documents：标题、路径、类型，以及哪个是 active。 |
| `inventor_get_document_info` | 获取 active document 的标题、完整路径和 document type。 |
| `inventor_list_bodies` | 列出 part 的 solid bodies：id（`body:N`）、name、volume_mm3、bbox_mm、face_count、生成 feature（created_by）、visible。 |
| `inventor_list_features` | 按 tree 顺序列出 part 的 features：name、type、health、suppressed、body_names。 |
| `inventor_probe_brep` | 调查 part 的 B-rep 端口：带 inner-loop 圆形边的平面 — normal（已按 IsParamReversed 校正）、center_mm、port_diameter_mm、面上所有圆。 |

### document (10) —— document 生命周期（write）

| Tool | 描述 |
|---|---|
| `inventor_new_part` | 新建一个 part document（.ipt）；可选 template 路径。 |
| `inventor_new_assembly` | 新建一个 assembly document（.iam）；可选 template 路径。 |
| `inventor_open_document` | 从完整路径打开已有 document 并设为 active。 |
| `inventor_save_document` | 保存 active document，或 Save-As 到给定路径。 |
| `inventor_close_document` | 关闭 active document；`save=true` 会先保存。 |
| `inventor_set_units` | 设置 active document 的长度单位（mm、cm、m、in、ft）。 |
| `inventor_set_material` | 按名称给 active part 分配一个 material。 |
| `inventor_save_all` | 更新根文档并静默保存它及所有有改动的被引用文档；逐文件报告 saved / read_only / error；支持 `dry_run`。 |
| `inventor_open_documents` | 一次打开多个文档（默认不开窗口）。 |
| `inventor_close_documents` | 按路径/名称关闭文档，或关闭所有可见文档（`keep_active`）；可先保存。 |

### parameters (4) —— model 与 user parameters（write）

| Tool | 描述 |
|---|---|
| `inventor_list_parameters` | 列出 parameters（model + user）：名称、表达式、值、单位、种类。 |
| `inventor_get_parameter` | 按名称获取一个 parameter：表达式、值、单位。 |
| `inventor_set_parameter` | 设置一个已有 parameter 的表达式/值，然后更新 document。 |
| `inventor_create_parameter` | 新建一个 user parameter（名称、表达式、单位）。 |

### properties (4) —— iProperties 与 mass properties（write）

| Tool | 描述 |
|---|---|
| `inventor_get_iproperty` | 按 property-set 和 property 名称获取一个 iProperty 值。 |
| `inventor_set_iproperty` | 设置一个 iProperty 值。 |
| `inventor_get_mass_properties` | 质量（g）、体积（mm³）、表面积（mm²）、质心、包围盒。 |
| `inventor_list_iproperty_sets` | 列出 iProperty 集合（name、internal_name、属性名；可选值）—— get/set_iproperty 的 discovery。 |

### sketch (10) —— 2D 草图几何与约束（write）

| Tool | 描述 |
|---|---|
| `inventor_create_sketch` | 在一个平面上创建 2D 草图（XY/XZ/YZ 或一个 face/work-plane 引用）。 |
| `inventor_project_geometry` | 把 model edges/vertices（按 edge ids）投影进 active 草图。 |
| `inventor_draw_line` | 画一条草图线段，从 (x1,y1) 到 (x2,y2)。 |
| `inventor_draw_circle` | 画一个草图圆，由圆心 + 半径确定。 |
| `inventor_draw_rectangle` | 画一个两点草图矩形。 |
| `inventor_draw_arc` | 画一个草图圆弧（圆心、半径、起始/结束角度）。 |
| `inventor_add_sketch_dimension` | 给草图图元添加一个驱动尺寸约束。 |
| `inventor_add_sketch_constraint` | 添加一个几何约束（coincident、parallel、tangent 等）。 |
| `inventor_draw_text` | 添加一个 fitted 文本框（position mm，可选 font_size_mm；rotation_deg 仅支持 90 的倍数）。 |
| `inventor_close_sketch` | 结束草图编辑（退出 sketch edit 模式）。 |

### feature (16) —— 实体与工作特征（write）

| Tool | 描述 |
|---|---|
| `inventor_extrude` | 拉伸一个命名草图（距离、join/cut/intersect、方向）。 |
| `inventor_revolve` | 把一个命名草图绕一个轴旋转（角度、operation）。 |
| `inventor_combine` | 对实体做布尔运算（base + tool bodies，join/cut/intersect，keep_tool_bodies）。 |
| `inventor_batch_execute` | 在一个事务中运行最多 20 条 wire commands（单次 undo，出错回滚）。 |
| `inventor_fillet` | 添加等半径边倒圆 — `edgeIds` 或 `edges` 选择器 `{kind:circular, radius_mm?, center_mm?, on_body?, adjacent_surface_types?}`；返回 `matched_edges`。 |
| `inventor_chamfer` | 给 model edges 添加等距倒角。 |
| `inventor_create_work_plane` | 创建一个工作平面（offset、three_points、tangent 或 fixed 原点+轴）。 |
| `inventor_create_work_axis` | 创建一个工作轴（two_points、edge、plane_intersection、normal_to_face_through_point）。 |
| `inventor_create_work_point` | 在 {x,y,z}/[x,y,z] mm 处创建固定工作点；construction point 无法命名（name_applied 会报告）。 |
| `inventor_hole` | 在确定性选择的平面面上钻通孔/沉孔/埋头孔；可选 tapped-thread 元数据。 |
| `inventor_circular_pattern` | 绕一个命名轴圆形阵列 part features（在某一角度上 count）。 |
| `inventor_rectangular_pattern` | 沿一个或两个命名轴矩形阵列 part features。 |
| `inventor_loft` | 按有序 sketch profiles（'SketchName' 或 'SketchName:N'）做 loft；可选 centerline sketch、closed/merge-tangent-faces。 |
| `inventor_sweep` | 把 sketch profile 沿 sketch path 扫掠（相连段自动串链）；orientation normal_to_path\|parallel。 |
| `inventor_create_bim_connector` | 在圆形 port edge 上创建 BIM pipe connector（ref 来自 inventor_probe_brep 的 `circles[].edge`）；kind=pipe，可选 system/flow/connection 元数据。 |
| `inventor_create_part` | 用 JSON recipe 一次调用建完整零件：草图（矩形 / 圆 / 带 bulge 圆弧的多段线、内部孔洞）→ 拉伸 / 孔 / 圆角 / 倒角 → 材料 + iProperties → 静默另存为。错误指出 recipe 路径，失败时零件不保存直接关闭；支持 `dry_run`。 |

### export (10) —— 视图捕获与几何导出（write）

> `output_path` 必须位于允许的根目录下：user profile、`%TEMP%`，或自行添加的根目录 —— 例如在运行 Inventor 的机器上设置 `BIMWRIGHT_INVENTOR_EXPORT_ROOT=D:\Inventor-Exports`（设置后需重启 Inventor 和 MCP 客户端/服务器会话）。

| Tool | 描述 |
|---|---|
| `inventor_capture_view` | 把 active view 捕获为 PNG 文件（写入 `<export-root>\captures\` 或输出路径）；`inline=true` 返回受限 base64 PNG。 |
| `inventor_export_step` | 把 active part/assembly 导出为 STEP（.stp/.step）。 |
| `inventor_export_stl` | 把 active part/assembly 导出为 STL（.stl）。 |
| `inventor_export_sat` | 把 active part/assembly 导出为 ACIS SAT（.sat）—— 用于 Revit interop 的格式；acis_version 默认 7（唯一支持的值）。 |
| `inventor_export_dxf` | 导出 2D DXF；必须声明 source（`sketch` 或 `flat_pattern`）。 |
| `inventor_derive_envelope` | 从源 part/assembly 创建 derived part —— envelope/interop 路径：`derive_style`、`include_bodies` 选择 solid、轻量 `bounding_box` 模式；.ipt 保存到允许的 root 下。 |
| `inventor_view_fit` | 把 active view 缩放适配到 model extents（捕获前运行）。 |
| `inventor_set_view_orientation` | 设置一个标准相机方向（iso/front/top 等），用于多角度捕获。 |
| `inventor_set_camera` | 显式设置相机（eye/target mm、up、perspective、extents_mm、fit）—— 标准方向不合适时在 capture_view 前使用。 |
| `inventor_set_view_state` | 激活/创建设计视图、切换对象可见性（工作特征、草图等）、按 selector 显示/隐藏 occurrence。`inventor_capture_view` 也接受这些键以及 orientation/camera/fit 和多个 `shots`。 |

> 导出路径必须是绝对路径，且位于允许的 output root 之下（用户 profile 或 temp）。

### assembly (8, write) —— 组合与编辑装配体

| Tool | 描述 |
|---|---|
| `inventor_place_occurrence` | 把一个 component（.ipt/.iam）放入 active assembly；可选初始 pose + grounded。 |
| `inventor_add_constraint` | 约束两个命名引用（mate/flush/insert/angle）；response 携带 `health` —— 务必检查它。 |
| `inventor_create_imate` | 使用确定性面选择器在 active part 上编写一个命名 iMate。 |
| `inventor_place_occurrences` | 在一个撤销步骤中放置多个组件；姿态为原点+旋转、坐标轴或 4×4 矩阵；`lock` = none / grounded / workplanes。 |
| `inventor_delete_occurrences` | 删除 selector 匹配的顶层 occurrence（`dry_run`）。 |
| `inventor_set_occurrence_state` | 批量设置可见 / 抑制 / 固定 / 姿态 / 锁定。 |
| `inventor_set_appearance` | 为 occurrence 或零件实体设置 RGB 颜色或库外观。 |
| `inventor_reset_appearance` | 移除外观覆盖。 |

### assembly_query (6, read-only) —— 数值自检一组；在 `--read-only` 下存活

| Tool | 描述 |
|---|---|
| `inventor_list_interfaces` | 列出 doc 或一个 occurrence 的命名接口（iMates、工作特征、origin 几何）。 |
| `inventor_check_interference` | 运行干涉分析；返回 pair 数量以及总/每对体积。 |
| `inventor_measure_min_distance` | 两个 occurrences 或命名引用之间的最小 3D 距离（mm）。 |
| `inventor_get_assembly_bom` | BOM + occurrence 树，带 grounded 标志以及 translation/rotation 自由度。 |
| `inventor_list_constraints` | 读回每个约束的 type、`health`、suppressed 标志以及两个 occurrence 名称。 |
| `inventor_list_occurrences` | 按 selector 列出 occurrence 及所选字段（path、file、bbox_mm、transform、可见性、材料、外观、质量、体积）。`check_interference` 支持 `set_a` × `set_b`，`measure_min_distance` 支持 `pairs[]` 或集合×集合 + `threshold_mm`。 |

### code (4) —— opt-in 的 escape hatch（默认关闭）

| Tool | 描述 |
|---|---|
| `inventor_send_code` | **危险，仅 opt-in。** 在进程内针对 `Inventor.Application` 执行一段 C# 代码片段。除非 server 和 add-in 都 opt-in，否则禁用（否则返回 `SEND_CODE_DISABLED`）；被禁的 API（file/process/network/environment）会被拒绝。 |
| `inventor_save_code_module` | 保存可复用的 C# helper module（仅声明；保存前做策略检查和 dry-compile）。 |
| `inventor_list_code_modules` | 列出已保存的 module 及其签名。 |
| `inventor_delete_code_module` | 删除 module。 |

### toolbaker (3, read-only) —— 纯粹操作 server-side 的 bake 数据库

| Tool | 描述 |
|---|---|
| `inventor_list_baked_tools` | 列出所有已验证、已编译、已注册的 baked tools。 |
| `inventor_list_bake_suggestions` | 列出来自重复 workflow 的 active ToolBaker 建议。 |
| `inventor_create_bake_issue_draft` | 为一个建议创建 GitHub issue 草稿（不提交）。 |

### toolbaker_write (3, write) —— 运行 baked tools 并管理建议生命周期

| Tool | 描述 |
|---|---|
| `inventor_run_baked_tool` | 按名称携带 JSON 参数执行一个已注册的 baked tool。 |
| `inventor_accept_bake_suggestion` | 接受一个建议：validate + compile + apply + persist 为一个 baked tool。 |
| `inventor_dismiss_bake_suggestion` | 驳回或暂缓一个 active 建议。 |

---

## Toast 通知

当 Inventor 可见时，命令结果更新到 **一张紧凑的活动卡片**，显示最新 tool 和带数字滚动效果的 **Success / Failed / Capture** 计数器。Capture 是 Success 的子集，包含 `inventor_capture_view` 的 `inline=true` 调用，不应再加到操作总数中。Failed 包含软失败和 batch rollback。读写操作都使用蓝色强调色；记录过失败的卡片保持红色强调色。数值变化时布局保持稳定，没有缩略图卡片、优先级堆叠或逐 tool 的 toast 队列。计数仅覆盖当前 target 上该卡片的生命周期，不代表整个任务或特定 MCP client。`inventor_health` 不产生 toast。**Agent connected** 只是状态通知，不增加计数，也不替换保留中的活动卡片或任务报告。

活动卡片在 **最后一个结果之后 20 秒**消失。由指针移动确认的真实悬停暂停计时，离开后重新开始完整的 20 秒。卡片出现在静止光标下不算悬停。点击卡片打开 **History** 并关闭卡片；**×** 只关闭卡片。页脚始终显示 **Inventor 年份**，不受 branding 开关影响。

agent 必须显式调用 `inventor_report_task_result` 报告工作结果：每项 agent/job 使用唯一 `task_id`（1–80 字符），`outcome` 为 `completed`/`failed`/`cancelled`，`summary` 为真实的单行总结（1–120 字符）。报告 **替换同一个共享卡位**，标注 **Agent reported**，显示时限为 **8 秒**；真实悬停暂停，离开后重新计时。报告不增加活动计数，下一个 tool 结果开始新的活动卡片。不会从空闲时间或单个 tool 成功推断任务完成。报告绕过 Inventor 命令队列，不调用 Inventor API，因此长时间 `send_code` 占用主线程时仍可接收。`toast_shown` 表示是否已保留卡片供显示，也包括等待 Inventor 取消最小化或关闭模态对话框的情况。报告遵守 Toasts 开关，需同时更新 server 和 add-in。

Toast 使用 **专用 STA UI 线程上的纯代码 WPF**，窗口 **无 owner 且不激活（unowned / no-activate）**，独立于 Inventor 主 STA，toast 线程不调用 Inventor COM。命令结果通知在响应返回后投递。Toast 不抢焦点，Inventor 最小化或打开模态对话框时隐藏，**即使其他应用位于前台也保持置顶**。自动调色板从屏幕采样并避开已绘制的卡片自身；采样仅保留在内存中，不写盘、不记日志。

通过功能区开关 toast：**Bimwright ▸ MCP → Toasts**（Status 打开诊断对话框）。该选择持久化到 `%LOCALAPPDATA%\Bimwright\ipt-mcp\iptmcp.config.json` 的 `enableToast`；`toastTheme` 接受 `auto`（默认）、`light` 或 `dark`。环境变量 `BIMWRIGHT_INVENTOR_ENABLE_TOAST` 和 `BIMWRIGHT_INVENTOR_TOAST_THEME` 会覆盖 JSON 值。格式错误的配置文件不会被改动——开关拒绝覆盖它。

同一功能区的 **Toast Brand** **默认关闭，仅当前会话有效**，不会持久化。启用后为活动标题添加 `IPT-MCP - ` 前缀，并在真实悬停时以擦拭动画显示 BIMwright 字标，离开时淡出。**卡片出现时不会自动播放字标擦拭动画**。动效遵守 Windows 动画设置。Toast UI 标签仍为英文；README 翻译不代表已实现 UI 本地化。

---

## 安全

简短版：你的模型留在你的机器上，write/dangerous tools 都被 gate 住。

- **只读模式。** `--read-only`（或 `BIMWRIGHT_INVENTOR_READ_ONLY=1`）移除每一个 write-capable toolset（`document`、`parameters`、`properties`、`sketch`、`feature`、`export`、`assembly`、`code`、`toolbaker_write`），但保留 `meta` + `query` + `assembly_query` + 只读的 `toolbaker`，并**保留 `inventor_switch_target` 暴露**。Server 在每个 command envelope 中发送只读模式；add-in 也支持 `BIMWRIGHT_INVENTOR_PLUGIN_READ_ONLY=1` / `BIMWRIGHT_INVENTOR_READ_ONLY=1`。在强制只读下执行 write 命令会返回 `READ_ONLY`。
- **send_code 双面 opt-in。** `inventor_send_code` **默认禁用**。只有当**两个** gate 都设置时才会暴露：server 端 `--enable-send-code`（或 `BIMWRIGHT_INVENTOR_ENABLE_SEND_CODE=1`）**且** add-in 进程 `BIMWRIGHT_INVENTOR_PLUGIN_ENABLE_SEND_CODE=1`。否则 dispatcher 返回 `SEND_CODE_DISABLED`。被禁的 API（file/process/network/environment）会被拒绝。
- **本地、经过认证的 transport。** TCP 绑定 loopback；Named Pipe 的作用域是 local-machine。每个 per-session descriptor 都带一个随机 auth token，但 MCP meta tools 永远不会返回它。
- **Sanitized errors。** 返回给 model 的错误信息会被 sanitize，避免泄露绝对路径/密钥。
- **ToolBaker 控制。** 默认启用 ToolBaker。你可以通过在启动 server 时传入 --disable-toolbaker 命令行标志，或者设置环境变量 BIMWRIGHT_INVENTOR_ENABLE_TOOLBAKER=0 来完全禁用它。
- **允许的导出路径。** 文件导出工具会验证 output_path 是否指向安全文件夹（位于 User Profile 或 Temp 目录下）。你可以通过设置环境变量 BIMWRIGHT_INVENTOR_EXPORT_ROOT 来定义额外允许的根文件夹。

**ToolBaker** 把重复的本地 workflow 变成个人、已验证的工具：建议通过 `inventor_list_bake_suggestions` 浮现，你用 `inventor_accept_bake_suggestion` 显式接受一个（validate → compile → apply → persist），accept 后的工具就可以通过 `inventor_list_baked_tools` / `inventor_run_baked_tool` 调用。Bake 数据库和 audit log 都位于本地 `%LOCALAPPDATA%\Bimwright\ipt-mcp\baked\`。见 [docs/toolbaker.md](docs/toolbaker.md) 和 [SECURITY.md](SECURITY.md)。

---

## 不重新分发

本项目**不**重新分发 Autodesk Inventor 二进制文件或 Inventor SDK / interop DLLs。发布的 server 和单元测试在没有 Inventor 时也能 build 并运行。Build 按版本的 add-ins 需要一个**本地 Inventor 安装**或匹配的 **interop reference assemblies**（`Autodesk.Inventor.Interop.dll`），通过默认的 Autodesk shared-extensions 路径或显式的 `/p:InventorInteropDir=...` MSBuild 属性提供。在 Inventor 上运行 gateway 需要一个已授权的 Inventor 安装。

---

## bimwright 家族

连接 AI 助手与 BIM、CAD 应用的开源工具。

**bimwright** 这个名字由 **BIM** 和 **wright** 组成。wright 是英语中表示制作者或建造者的旧词，如 *shipwright*（造船工）。

- [**rvt-mcp**](https://github.com/bimwright/rvt-mcp) —— Autodesk® Revit®
- [**dwg-mcp**](https://github.com/bimwright/dwg-mcp) —— Autodesk® AutoCAD®
- [**nwd-mcp**](https://github.com/bimwright/nwd-mcp) —— Autodesk® Navisworks®
- [**ipt-mcp**](https://github.com/bimwright/ipt-mcp) —— Autodesk® Inventor®
- [**bim-wiki**](https://github.com/bimwright/bim-wiki) —— 越南语优先的 BIM 知识库

---

## 许可证

[Apache-2.0](LICENSE)。见 [LICENSE](LICENSE)。

Inventor 和 Autodesk 是 Autodesk, Inc. 的注册商标。bimwright 是一个独立的 open-source 项目，与 Autodesk, Inc. 无关联、无赞助、无背书。

---

<p align="center">
  一个 <a href="https://github.com/bimwright">bimwright</a> 项目。
</p>
