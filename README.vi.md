# inventor-mcp

> **Trạng thái: Giai đoạn 1 (khung sườn + bộ khung runtime).** Chưa sẵn sàng cho production. README này là bản nháp; phần tổng quan đầy đủ, bảng phiên bản hỗ trợ, hướng dẫn cài đặt/phát triển và danh sách công cụ Giai đoạn 1 sẽ được viết ở Giai đoạn 4.

Cổng [Model Context Protocol](https://modelcontextprotocol.io) mã nguồn mở ([Apache-2.0](LICENSE)) cho phép Claude Code và mọi client hỗ trợ MCP điều khiển **Autodesk Inventor 2022-2027**.

## Tổng quan

Một MCP server .NET 8 giao tiếp NDJSON qua kênh cục bộ có xác thực (TCP cho 2022-2024, Named Pipe cho 2025-2027) tới add-in Inventor chạy trong tiến trình cho từng phiên bản. Add-in đẩy mọi lệnh lên luồng STA của Inventor và trả về phong bì JSON.

Xem [`CLAUDE.md`](CLAUDE.md) để biết bản tóm tắt kiến trúc và [`ARCHITECTURE.md`](ARCHITECTURE.md) để biết thiết kế.

## Phiên bản hỗ trợ

| Inventor | Runtime | Kênh truyền |
|----------|---------|-------------|
| 2022-2024 | .NET Framework 4.8 | TCP |
| 2025-2026 | .NET 8 | Named Pipe |
| 2027 | .NET 10 | Named Pipe |

## Build & test (chỉ server, không cần Inventor)

```bash
dotnet build src/InventorMcp.sln -c Debug
dotnet test  tests/Bimwright.Inventor.Tests -c Debug
```

## An toàn

- `inventor_send_code` **mặc định tắt** — bật bằng `--enable-send-code` cùng cờ môi trường của add-in.
- `--read-only` ẩn mọi công cụ có khả năng ghi.
- Xem [`docs/toolbaker.md`](docs/toolbaker.md) và [`SECURITY.md`](SECURITY.md).

## Không phân phối lại

Dự án này **không** phân phối lại các tệp nhị phân của Autodesk hay Inventor SDK. Bạn cần có Inventor được cấp phép để build add-in hoặc chạy cổng kết nối với Inventor.

## Giấy phép

[Apache-2.0](LICENSE) © Khoa Le.
