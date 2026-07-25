---
name: concurrency-check
description: Dùng cho mọi thao tác trừ tồn kho/đặt hàng trong MiniMart - đảm bảo giải thích rủi ro race condition, chọn đúng chiến lược Optimistic (RowVersion) hay Pessimistic (UPDLOCK), luôn bọc transaction, và xử lý exception đúng theo từng chiến lược.
---

# Concurrency Check - MiniMart

## Khi nào áp dụng
Mọi thao tác ảnh hưởng đến tồn kho hoặc đặt hàng (trừ số lượng `Products`, tạo/xác nhận `Order`, hoặc bất kỳ update nào có thể bị nhiều request ghi đè đồng thời).

## Quy trình bắt buộc (đúng thứ tự)

1. **Giải thích rủi ro race condition cụ thể của đoạn code đang viết**:
   - Kịch bản 2 request cùng đọc số lượng tồn kho, cùng tính toán, cùng ghi đè → "lost update".
   - Chỉ rõ vị trí trong code có thể xảy ra race (khoảng thời gian giữa đọc và ghi).

2. **Chọn chiến lược: Optimistic (RowVersion) hay Pessimistic (UPDLOCK)** - giải thích cả 2 và lý do chọn:
   - **Optimistic (dùng `Products.RowVersion`)**:
     - Cách hoạt động: đọc kèm RowVersion, khi `SaveChanges()` EF Core tự thêm điều kiện `WHERE RowVersion = @original` vào câu UPDATE; nếu có request khác đã ghi trước, 0 row bị ảnh hưởng → EF Core ném `DbUpdateConcurrencyException`.
     - Phù hợp khi: xung đột hiếm xảy ra, không muốn khóa DB lâu, ưu tiên throughput (VD: cập nhật thông tin sản phẩm).
   - **Pessimistic (dùng `UPDLOCK` qua Dapper + transaction)**:
     - Cách hoạt động: `SELECT ... WITH (UPDLOCK, ROWLOCK)` trong transaction để khóa row ngay khi đọc, request khác phải chờ đến khi transaction hiện tại commit/rollback.
     - Phù hợp khi: xung đột xảy ra thường xuyên, thao tác cần chắc chắn tuần tự (VD: trừ tồn kho lúc checkout, tránh oversell khi nhiều người mua cùng lúc sản phẩm sắp hết hàng).
   - Nêu rõ lý do chọn 1 trong 2 cho tình huống cụ thể đang xử lý.

3. **Code luôn kèm transaction**:
   - Optimistic: bọc trong transaction nếu có nhiều thao tác ghi liên quan (VD: trừ tồn kho + tạo Order phải cùng thành công hoặc cùng rollback).
   - Pessimistic: bắt buộc có transaction (Dapper `IDbTransaction` hoặc EF Core `BeginTransactionAsync`) để giữ lock cho đến khi xử lý xong.

4. **Xử lý exception đúng theo chiến lược đã chọn** (không dùng chung một kiểu cho cả 2 nhánh):
   - **Optimistic** → luôn `catch (DbUpdateConcurrencyException)`: báo lỗi rõ ràng cho người dùng (VD: "Sản phẩm vừa được cập nhật bởi người khác, vui lòng thử lại"), có thể reload dữ liệu mới nhất và cho phép thử lại.
   - **Pessimistic** → KHÔNG catch `DbUpdateConcurrencyException` (EF Core không ném exception này ở nhánh UPDLOCK vì xung đột được chặn bằng lock chứ không phát hiện sau khi ghi). Thay vào đó xử lý:
     - `SqlException` với lock timeout (error 1222) nếu có set `LOCK_TIMEOUT`.
     - Deadlock (error 1205) nếu transaction phức tạp có thể gây deadlock với transaction khác - nên có retry logic khi gặp deadlock.

## Checklist trước khi hoàn thành
- [ ] Đã giải thích rủi ro race condition cụ thể.
- [ ] Đã giải thích cả Optimistic và Pessimistic, nêu lý do chọn 1.
- [ ] Code có transaction.
- [ ] Exception handling khớp với chiến lược đã chọn (không mặc định catch `DbUpdateConcurrencyException` cho nhánh Pessimistic).
