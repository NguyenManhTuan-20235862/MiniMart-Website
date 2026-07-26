# Quy ước Concurrency (Optimistic, qua RowVersion)

Đọc trước khi sửa luồng Edit của Product, `UnitOfWork`, hoặc bất cứ gì chạm
`RowVersion`.

Chọn **Optimistic** chứ không Pessimistic (`UPDLOCK`): form web giữ khoá suốt thời gian
người dùng suy nghĩ, ai mở form rồi đi ăn trưa là khoá cả bảng. Optimistic cho phép đọc
tự do và chỉ kiểm tra lúc ghi.

- Luồng sửa BẮT BUỘC round-trip `RowVersion` qua **hidden field**:
  `Edit GET` chụp `product.RowVersion` → view render **Base64** → `Edit POST` gửi lại →
  Service gọi `SetExpectedRowVersion`.
- Không có bước round-trip thì Optimistic Concurrency chỉ bảo vệ vài millisecond giữa
  `GetForUpdateAsync` và `SaveChanges` — còn khoảng cần bảo vệ là vài phút form mở thì
  bỏ trống. Test đã chứng minh: bỏ `SetExpectedRowVersion` → **không exception nào được
  ném** và thay đổi của người khác bị ghi đè (lost update).
- `SetExpectedRowVersion` ghi vào **`OriginalValue`**, KHÔNG phải `CurrentValue`. EF Core
  kẹp `OriginalValue` vào `WHERE`; `CurrentValue` của cột `rowversion` do SQL Server tự
  sinh nên gán vào đó không có tác dụng. Đã mutation test: `CurrentValue` làm 3 test đỏ.
- View render Base64 **thủ công** (`Convert.ToBase64String`), KHÔNG dùng `asp-for`:
  `InputTagHelper` gọi `ToString()` trên `byte[]` và cho ra `"System.Byte[]"` — form vẫn
  submit, model binder không giải mã được, tính năng biến mất trong im lặng.
- Nhận `RowVersion` từ client KHÔNG vi phạm chống over-posting: client chỉ đọc rồi trả
  lại nguyên vẹn. Gửi phiên bản cũ → chính họ nhận lỗi; gửi phiên bản mới → tương đương
  vừa mở lại form. Không có đặc quyền nào giành được.
- Xử lý xung đột nằm ở **Controller**, không ở Service: quyết định "ghi đè hay bỏ" là của
  người dùng. Controller phải (1) nêu **giá trị hiện tại** trong DB, (2) **giữ nguyên** dữ
  liệu người dùng đã nhập, (3) nạp `RowVersion` **MỚI** vào form.
- Thiếu (3) là người dùng mắc kẹt: bấm Lưu bao nhiêu lần cũng xung đột. Có test riêng
  (`Sau_xung_dot_bam_Luu_lan_hai_thi_thanh_cong`) khoá điều này.
- `rowVersion = null` nghĩa là **bỏ qua** kiểm tra — dành cho luồng nội bộ không có form
  (job, seed). Là chủ ý, không phải quên.
