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

## Trừ tồn kho lúc đặt hàng (OrderService.CheckoutAsync)

Race condition cụ thể: `product.Stock -= n` trong C# KHÔNG phải phép trừ nguyên tử ở DB —
nó là `SELECT` rồi `UPDATE SET Stock = <hằng số>`. Hai request cùng đọc `Stock = 1`, cùng
kiểm `1 >= 1`, cùng ghi `0` → bán 2 cái khi chỉ có 1, **không exception nào**.

- Chọn **Optimistic** trên `Products.RowVersion`, KHÔNG Pessimistic (`UPDLOCK`). Hai cách
  đúng như nhau về tính đúng đắn, khác ở cách phản ứng: Pessimistic cho người sau CHỜ,
  Optimistic từ chối sớm. Lý do chọn: `RowVersion` đã có sẵn và đã dùng cho luồng Admin —
  hai mô hình concurrency trên cùng một bảng khó suy luận hơn nhiều so với một mô hình.
- **Giới hạn đã biết và chấp nhận**: 10 người bấm cùng lúc khi còn 5 hàng thì chỉ **1**
  đơn thành công (tất cả đọc cùng `RowVersion`, một người thắng), 9 người nhận "vui lòng
  cập nhật giỏ hàng". Không oversell (đúng) nhưng throughput thấp. Muốn "bán hết đúng 5"
  thì cần **retry** hoặc đổi sang Pessimistic — làm khi có flash sale, không phải bây giờ.
- Sắp xếp dòng giỏ theo **`ProductId` tăng dần** trước khi xử lý. Hai đơn cùng chứa A và B
  mà một bên chạm A→B còn bên kia B→A sẽ deadlock. Mọi transaction chạm nhiều dòng phải
  chạm theo CÙNG một thứ tự.
- Kiểm `Stock < Quantity` ở Service là để có **thông báo tử tế kèm số còn lại**, KHÔNG phải
  thứ chống oversell — giữa lệnh kiểm và `SaveChanges` vẫn còn khe TOCTOU. Chống oversell
  là việc của `RowVersion` + CHECK `Stock >= 0`.
- Application bắt **`ConcurrencyConflictException`**, không phải `DbUpdateConcurrencyException`
  (cấm `using Microsoft.EntityFrameworkCore`). Dịch nó thành `InsufficientStockException`
  kèm TÊN sản phẩm — tra tên từ dictionary đã đọc sẵn, không truy vấn lại trong `catch`.

### Transaction: có, nhưng phải biết nó đang làm gì

`IUnitOfWork.BeginTransactionAsync` được thêm đúng lúc làm nghiệp vụ đặt hàng (hoãn từ đầu
dự án là có chủ đích). `ITransaction` (Domain) bọc `IDbContextTransaction`; `await using` +
`CommitAsync` tường minh, không commit thì Dispose rollback.

**Sự thật đã kiểm chứng bằng mutation test**: bỏ hẳn transaction tường minh thì **cả 5
integration test vẫn xanh**. Lý do là mọi thao tác ghi đi qua **một** `SaveChangesAsync`,
mà EF Core đã tự bọc mỗi `SaveChanges` trong một transaction ngầm. Nên hôm nay transaction
tường minh **chưa phải** thứ tạo ra tính nguyên tử — nó trở thành thứ chịu lực ngay khi có
`SaveChanges` thứ hai (ví dụ thêm bản ghi thanh toán). Giữ nó, nhưng đừng nói nó đang bảo
vệ atomicity.

### Test concurrency phải trông như thế nào
- SQL Server **thật**, `Task.WhenAll`, mỗi "người mua" một **DI scope riêng** (DbContext
  riêng). Dùng chung scope là hai bên nhìn cùng một entity trong bộ nhớ — không xung đột
  nào xảy ra và test xanh vô nghĩa.
- Assert theo **bất biến**, không theo số đơn thành công: `tổng đã bán <= tồn kho ban đầu`,
  `tồn kho >= 0`, `tồn kho còn + đã bán == ban đầu`, và **mọi thất bại phải đúng kiểu**
  `InsufficientStockException`.
- Có một test **tuần tự** làm đối chứng (bán được đúng 5/5). Không có nó thì không phân
  biệt được "thất bại do tranh chấp" với "logic luôn từ chối".
- Bài học từ mutation test: bỏ kiểm `Stock < Quantity` ở Service thì tồn kho **vẫn không
  âm** (CHECK constraint chặn) nên mọi assert về SỐ LƯỢNG vẫn xanh — chỉ **kiểu exception**
  đổi. Không assert kiểu exception thì mutation đó lọt qua toàn bộ test tích hợp.
