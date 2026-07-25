---
name: ef-core-migration
description: Dùng khi Entity trong MiniMart.Domain thay đổi (thêm/sửa/xóa property, quan hệ, index...) - đảm bảo giải thích ảnh hưởng DB, sinh migration đúng cách và review nội dung file migration trước khi update database.
---

# EF Core Migration - MiniMart

## Khi nào áp dụng
Mỗi khi Entity thay đổi: thêm/xóa/sửa property, đổi kiểu dữ liệu, thêm quan hệ (navigation property), thêm index, hoặc thay đổi Fluent API trong `OnModelCreating`.

## Quy trình bắt buộc (đúng thứ tự)

1. **Sửa Entity** trong `MiniMart.Domain` (và Fluent API config trong `MiniMart.Infrastructure` nếu cần).

2. **Giải thích ảnh hưởng đến DB TRƯỚC khi chạy lệnh migration**:
   - Thay đổi này sinh ra cột/bảng/ràng buộc (constraint) gì mới?
   - Đây có phải thay đổi breaking không? (VD: đổi kiểu dữ liệu cột đã có data, thêm cột NOT NULL không có default value, đổi tên property khiến EF hiểu thành Drop+Add cột → mất dữ liệu).
   - Nếu có rủi ro mất dữ liệu hoặc cần data migration thủ công, phải cảnh báo rõ trước khi tiếp tục.

3. **Chạy lệnh sinh migration**:
   ```
   dotnet ef migrations add <TenMigration> -p MiniMart.Infrastructure -s MiniMart.Web
   ```
   - Đặt tên migration mô tả đúng thay đổi (VD: `AddOrderStatusColumn`, `AddProductCategoryIndex`).

4. **Giải thích nội dung file migration vừa sinh ra, TRƯỚC khi update database**:
   - Đọc file `<Timestamp>_<TenMigration>.cs`.
   - Giải thích method `Up()` (thay đổi sẽ áp dụng lên DB) và `Down()` (rollback tương ứng).
   - Chỉ rõ các thao tác nhạy cảm nếu có: `DropColumn`, `AlterColumn`, `DropTable`, `RenameColumn`, thay đổi kiểu dữ liệu.

5. **Chỉ chạy `dotnet ef database update` sau khi người dùng xác nhận đã đọc và đồng ý** với nội dung migration:
   ```
   dotnet ef database update -p MiniMart.Infrastructure -s MiniMart.Web
   ```

## Lưu ý đặc biệt của dự án
- `Products.RowVersion` dùng cho Optimistic Concurrency (kiểu `rowversion`/`timestamp`). KHÔNG được xóa, đổi kiểu dữ liệu, hay để migration tự động động vào cột này mà không hỏi lại người dùng trước. Nếu migration generate ra có thay đổi liên quan đến `RowVersion`, phải dừng lại, giải thích rủi ro, và chờ xác nhận.
