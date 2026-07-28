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
- View render Base64 **thủ công** (`Convert.ToBase64String`), KHÔNG dùng `asp-for`.
  **Điều kiện của cái bẫy đã được ĐO lại và tinh tế hơn mô tả cũ:**

  | Cách viết | Kết quả |
  |---|---|
  | `<input type="hidden" asp-for="RowVersion" />` | Base64 **đúng** |
  | `<input asp-for="RowVersion" />` | **`"System.Byte[]"`** |

  `DefaultHtmlGenerator.GenerateHidden` có nhánh riêng mã hoá `byte[]` sang Base64, còn
  đường input thường gọi `ToString()`. Nghĩa là code dùng `asp-for` kèm `type="hidden"`
  vẫn chạy đúng — cho tới khi ai đó bỏ `type="hidden"` đi, và lúc đó concurrency biến
  mất trong im lặng. Viết tay thì kết quả không phụ thuộc chi tiết đó.
  Chiều ngược lại (POST) model binder tự giải mã Base64 → `byte[]`, nên chỉ chiều
  **render** mới phải làm tay.
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

**Đã đo bằng ba mutation, và kết luận tinh tế hơn "có transaction là an toàn":**

| Mutation | Kết quả | Nghĩa là |
|---|---|---|
| Bỏ transaction tường minh (giữ 1 `SaveChanges`) | tất cả xanh | Với hình dạng code HIỆN TẠI nó dư: EF Core đã tự bọc mỗi `SaveChanges` trong transaction ngầm |
| `SaveChanges` trong vòng lặp (giữ transaction) | tất cả xanh | Transaction ĐÃ cứu: A bị ghi thật rồi bị rollback khi B ném |
| `SaveChanges` trong vòng lặp + bỏ transaction | **đỏ: tồn kho A còn 8 thay vì 10** | Đơn "nửa vời" thật — trừ kho mà không có đơn |

Nên phát biểu đúng là: transaction tường minh **hôm nay không phải** thứ tạo ra tính nguyên
tử (một `SaveChanges` đã tự nguyên tử), nhưng nó là **lưới an toàn** biến một refactor sai
kinh điển — "lưu từng món cho chắc" — từ bug dữ liệu thành chuyện không xảy ra. Đó là lý do
giữ nó, chứ không phải vì nó đang gánh atomicity.

### Xoá giỏ hàng nằm TRONG transaction, không phải sau commit

Câu hỏi "side effect phải đặt sau khi commit chứ?" là quy tắc thật, nhưng nó áp cho thứ
**không rollback được** (gửi mail, gọi webhook, ghi Redis, xoá file). Giỏ hàng của người đã
đăng nhập nằm ở **cùng database, cùng transaction** nên nó không phải side effect — nó là
một phần của cùng một thay đổi dữ liệu. Vì vậy `_cartStore.ClearAsync` đứng **trước**
`SaveChangesAsync`, và `DELETE CartItems` đi chung batch với `INSERT Order`.

Đã mutation test (chuyển `ClearAsync` xuống sau `CommitAsync`): **5 test đỏ**. Cơ chế trực
tiếp là `DatabaseCartStore` chỉ đánh dấu Change Tracker, mà sau `Commit` không còn
`SaveChanges` nào → giỏ **không bao giờ được xoá**. Nhưng kể cả khi thêm một `SaveChanges`
thứ hai thì vẫn sai, vì nó mở ra một cửa sổ có trạng thái quan sát được:

| Thứ tự | Chết giữa chừng thì sao |
|---|---|
| Xoá giỏ **trong** transaction (hiện tại) | Không có trạng thái trung gian — hoặc có đơn và giỏ rỗng, hoặc không có gì |
| Xoá giỏ **sau** commit | Đơn đã tạo, tiền đã tính, **giỏ vẫn đầy** → người dùng bấm đặt lần nữa, đơn trùng |
| Xoá giỏ **trước** transaction | **Mất giỏ mà không có đơn** — tệ nhất |

⚠ Điều này đúng vì `ICartStore` tại `/Checkout` **luôn** là `DatabaseCartStore`
(`[Authorize]`). Nếu sau này cho khách vãng lai đặt hàng thì kho là `SessionCartStore`, mà
`ClearAsync` của nó **ghi ngay lập tức, ngoài mọi transaction** — lúc đó vị trí hiện tại
thành "xoá giỏ trước khi biết đơn có lưu được không", và rollback DB không hoàn lại Session.
Chỉ khi đó câu hỏi "sau commit" mới trở thành đúng, và chỉ đúng cho kho không giao dịch.

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

### Giỏ nhiều món, một món hỏng: hai đường rất khác nhau
Giỏ A(1), B(2), C(3) xử lý theo `ProductId` tăng dần, B là món hỏng:

- **Thiếu hàng thấy được lúc đọc** → ném ở giữa vòng lặp, **vượt qua** `AddAsync` và
  `SaveChangesAsync`. `A.Stock` đã bị trừ nhưng CHỈ trong Change Tracker; C chưa được chạm.
  Không có gì để rollback vì không có gì từng được ghi. Cái bảo vệ ở đây là "`SaveChanges`
  là thứ duy nhất biết ghi, và nó không được gọi".
- **Thiếu hàng xuất hiện giữa đọc và ghi** → cả 3 qua được lệnh kiểm, `SaveChanges` gửi một
  batch (3 UPDATE + INSERT Order + 3 INSERT OrderDetail + 3 DELETE CartItem). `RowVersion`
  của B lệch → UPDATE của B khớp 0 dòng → cả batch bị revert, gồm cả A và C. Đây mới là
  rollback theo nghĩa đen.

⚠ **Change Tracker vẫn BẨN sau khi ném ở đường 1** (`A.Stock` đang là 8 trong bộ nhớ). Hôm
nay vô hại vì Controller bắt exception rồi redirect. Nhưng thêm bất kỳ `SaveChanges` nào sau
đó trong cùng scope — ví dụ "ghi log lần đặt hàng thất bại" — là **A bị trừ kho mà không có
đơn nào**, im lặng. Muốn log thì phải làm ở scope khác.

📌 `CartItems → Products` là **Cascade**, nên xoá sản phẩm khỏi shop sẽ tự gỡ nó khỏi mọi
giỏ. Hệ quả: nhánh `NotFoundException` trong `CheckoutAsync` gần như không tới được từ giỏ
DB — chỉ tới được qua khe race rất hẹp (sản phẩm bị xoá đúng giữa `GetLinesAsync` và
`GetManyForUpdateAsync`). Một test viết sai vì tưởng nhánh đó dễ chạm đã đỏ và phải sửa lại
theo hành vi thật: đặt hàng đi tiếp bình thường với các món còn lại.

## Sửa hàng loạt (BulkUpdatePriceStockAsync) — ba cách, và vì sao chọn cách thứ ba

Bài toán khác hẳn "giảm giá 10% cả danh mục": **mỗi dòng một giá trị riêng** và **mỗi
dòng một `RowVersion` riêng**. Ba cách đã cân nhắc:

| | (a) `ExecuteUpdate` | (b) Dapper, 1 transaction | (c) Change tracking + 1 `SaveChanges` |
|---|---|---|---|
| Giá trị khác nhau mỗi dòng | **N lệnh** — `SET` là hằng số cho cả câu | 1 lệnh multi-exec | 1 batch |
| Round-trip cho 20 dòng | **20** | 1 (+1 nếu cần đọc trước) | **1** (đã đo) |
| `RowVersion` vào `WHERE` | **phải tự viết** | phải tự viết | **EF tự thêm** |
| Biết dòng NÀO hỏng | có (rows-affected từng lệnh) | **không** — multi-exec chỉ trả TỔNG | có (`ex.Entries`) |
| Dòng không sửa | vẫn UPDATE → **xung đột oan** | vẫn UPDATE → xung đột oan | **không sinh UPDATE** |
| Thứ tự chạm dòng (deadlock) | tự sắp | tự sắp | **EF tự sắp theo khoá chính** (đã đo) |
| Nguyên tử | tự mở transaction | tự mở transaction | transaction ngầm của `SaveChanges` |

Đã đo bằng `LogTo` trên SQL Server thật (chạm 4 entity, đổi giá trị của 3):

```sql
-- (c): MỘT command, ba câu UPDATE, RowVersion tự vào WHERE
UPDATE [Products] SET [Price] = @p0, [Stock] = @p1
OUTPUT INSERTED.[RowVersion]
WHERE [Id] = @p2 AND [RowVersion] = @p3;
... (x3, KHÔNG có câu thứ tư)
```

- **`ExecuteUpdate` KHÔNG tự kẹp `RowVersion` vào `WHERE`** — nó không đi qua Change
  Tracker nên không biết gì về concurrency token. Quên viết tay là Optimistic Concurrency
  biến mất trong im lặng, `dotnet build` sạch, mọi test cũ vẫn xanh.
- `ExecuteUpdate` cũng **chạy ngay lập tức**, ngoài `SaveChanges` và ngoài mọi transaction
  chưa mở tường minh, và **không cập nhật entity đang được theo dõi** — hai nguồn "code
  đọc lên giá trị cũ" rất khó tìm.
- Dapper multi-exec (`ExecuteAsync(sql, danhSach)`) trả về **tổng** số dòng bị ảnh hưởng.
  19/20 nghĩa là có một dòng hỏng, nhưng **không biết dòng nào** — mà thông báo hữu ích
  cho người dùng bắt buộc phải nêu tên sản phẩm. Muốn biết thì phải bỏ multi-exec và
  quay lại vòng lặp, tức mất đúng lý do đã chọn Dapper.
- Dùng Dapper ở đây còn phải tự nối `DbConnection` + `DbTransaction` của `DbContext` để
  hai bên chung một transaction. Quy ước dự án: **Dapper cho truy vấn ĐỌC phức tạp**,
  không cho đường ghi đã có EF lo.

### Tính chất quyết định: dòng không sửa thì không sinh UPDATE
Gán một property bằng đúng giá trị đang có **không** đánh dấu `Modified` (EF so với
`OriginalValue`). Hệ quả trên màn hình 20 dòng: sửa 1 dòng thì chỉ 1 dòng đó cần "còn
nguyên vẹn". Người khác đổi **tên** một sản phẩm khác trên cùng trang không làm hỏng lần
lưu, dù `RowVersion` của dòng đó đã đổi. Với (a) và (b) thì mọi dòng đều bị UPDATE nên
dòng không ai chạm vào cũng đủ sức chặn cả lần lưu — có test riêng khoá điều này
(`Dong_KHONG_sua_gi_thi_phien_ban_cu_van_luu_duoc`).

### Thành công một phần — CÓ CHỦ Ý, và khác hẳn CheckoutAsync
Dòng nào lệch `RowVersion` thì bị **bỏ qua** và báo lại; các dòng còn lại **vẫn được
ghi**. Đây là quyết định **nghiệp vụ**, không phải chi tiết kỹ thuật, và nó ngược với
`CheckoutAsync` trên cùng một cơ chế `RowVersion`:

| | Đặt hàng | Sửa hàng loạt |
|---|---|---|
| Người dùng | Khách, đã rời màn hình | Admin, đang nhìn màn hình |
| Nửa vời nghĩa là | Khách trả tiền cho đơn không đúng thứ họ đặt | "18 dòng đã lưu, 2 dòng cần xem lại" |
| Kết luận | **Huỷ cả đơn** | **Giữ 18, báo 2** |

⚠ Đừng "sửa cho nhất quán" theo bất kỳ chiều nào. Hai câu trả lời khác nhau vì hai câu
hỏi khác nhau.

### Ba lớp, mỗi lớp bịt một cửa sổ khác nhau — bỏ lớp nào cũng sai
| Lớp | Điều kiện | Bịt cửa sổ | Cho ra |
|---|---|---|---|
| 0 | `Price` và `Stock` gửi lên **bằng** giá trị trong DB | — | Không ghi, và **không báo xung đột** |
| 1 | `RowVersion` gửi lên **khác** `RowVersion` vừa đọc | rộng (vài phút bảng mở) | Bỏ qua **chọn lọc** dòng đó, biết tên + giá trị hiện tại |
| 2 | `SetExpectedRowVersion` → `WHERE RowVersion = @original` | hẹp (đọc → ghi, vài ms) | Bảo đảm thật |

- **Lớp 0 không phải tối ưu, nó là ĐÚNG/SAI.** Bảng chỉ có hai ô Giá và Tồn kho, nên
  người khác đổi **tên** một sản phẩm làm `RowVersion` dòng đó nhảy trong khi không có
  gì ta định ghi bị ảnh hưởng. Thiếu lớp 0 thì Admin mở bảng, không sửa gì, bấm Lưu, và
  nhận một danh sách "xung đột" toàn dòng họ chưa từng chạm vào — cách nhanh nhất để dạy
  người dùng bỏ qua thông báo.
- **Lớp 1 một mình là TOCTOU** — đúng thứ `data-access.md` cấm. Nó chỉ là "đường đẹp".
- **Lớp 2 một mình không bỏ qua chọn lọc được**: EF ném cho cả batch, không cứu được 18
  dòng còn lại. Nó là "sự thật", không phải "thông báo tử tế".
- ⚠ **Ở nhánh lớp 1 TUYỆT ĐỐI không chạm entity.** Gán `Price`/`Stock` rồi mới `continue`
  là EF vẫn sinh câu UPDATE cho nó, câu đó khớp 0 dòng, và **cả batch bị revert** — tức
  quay về đúng all-or-nothing mà yêu cầu này muốn bỏ. Không có gì trong thông báo tố
  giác điều đó: người dùng vẫn thấy "đã lưu 1 sản phẩm" trong khi không dòng nào được ghi.

### Trường hợp hiếm vẫn là tất-cả-hoặc-không-gì-cả
Nếu ai đó ghi đúng vào **cửa sổ hẹp** thì lớp 2 nổ ở `SaveChanges` và cả batch bị bỏ.
Không tự thử lại: thử lại sạch sẽ đòi một `DbContext` mới nên không làm được trong cùng
request (cùng lý do đã hoãn retry cho đua tạo giỏ hàng lần đầu). Controller nói rõ
"không có thay đổi nào được ghi, bấm Lưu lần nữa".

### Vẫn KHÔNG mở transaction tường minh
Vẫn **có** transaction — EF tự bọc mỗi `SaveChanges`. Cái đổi là **đơn vị nguyên tử**:
nó bao "các dòng sống sót sau lớp 0 và lớp 1", không phải "mọi dòng người dùng gửi lên".
Đó chính là ngữ nghĩa được yêu cầu. Dòng bị bỏ qua không nằm trong đó vì nó không sinh
lệnh ghi nào.

### Controller phải nạp `RowVersion` mới cho TOÀN BẢNG, không chỉ dòng vướng
Đây là điểm dễ sai nhất của việc cho phép thành công một phần: **dòng vừa lưu XONG cũng
đã có phiên bản mới**. Giữ phiên bản cũ cho chúng thì lần bấm Lưu tiếp theo báo xung đột
ở đúng những dòng mà chính người dùng vừa ghi thành công — một vòng lặp không lối ra và
rất khó hiểu. Đã mutation test: chỉ nạp cho dòng vướng → 1 đỏ.

Thông báo phải nói đủ **ba** điều: bao nhiêu dòng ĐÃ lưu (thiếu là Admin tưởng cả lần
bấm Lưu vô ích và bấm lại), dòng nào bị bỏ qua + người kia đã đổi thành **giá trị gì**,
và làm gì tiếp.

### Hai bẫy im lặng riêng của đường bulk
- **Ghép item với entity theo VỊ TRÍ** thay vì theo `Id`: giá của sản phẩm này rơi vào
  sản phẩm khác, và cả hai vẫn là số hợp lệ nên không có gì tố giác. Test phải cố ý
  **đảo thứ tự** danh sách gửi vào so với thứ tự repository trả về.
- **Hai dòng cùng `Id`** trong một lần gọi: identity map cho ra MỘT object nên dòng sau
  đè dòng trước, không exception nào, và người dùng thấy "đã cập nhật 2 sản phẩm".
  Service phải chặn tường minh.

### Bài học mutation
| Mutation | Kết quả |
|---|---|
| Bỏ chặn `Id` trùng | 1 đỏ |
| Ghép theo vị trí thay vì theo `Id` | 1 đỏ (chỉ unit test — integration không bắt được vì DB trả về đúng thứ tự) |
| **Chạm entity rồi mới `continue`** ở nhánh xung đột | **4 đỏ** |
| Bỏ lớp 0 (dòng không sửa cũng bị coi là xung đột) | 1 đỏ |
| Bỏ lớp 2 (`SetExpectedRowVersion`) | **1 đỏ, và CHỈ unit test** — xem cảnh báo dưới |
| Controller chỉ nạp `RowVersion` mới cho dòng vướng | 1 đỏ |
| Controller không nạp lại `Name` (`[BindNever]`) | 2 đỏ |
| Controller redirect thay vì render lại khi ModelState hỏng | 1 đỏ |
| `SaveChanges` **trong vòng lặp** (bản all-or-nothing trước đó) | 5 đỏ |

⚠ **Lớp 2 gần như vô hình với test hành vi.** Bỏ `SetExpectedRowVersion` đi thì mọi
integration test trên SQL Server thật vẫn xanh — cửa sổ hẹp quá nhỏ để test nào chạm
tới. Chỉ **unit test với Moq** (`Verify(SetExpectedRowVersion, ...)`) bắt được. Cùng
hình dạng với bài học ở `payments.md`: an toàn thật nằm ở chỗ không test hành vi nào
với tới, nên phải có test khẳng định thẳng vào **cơ chế**.

⚠ Bài học đắt nhất (từ bản all-or-nothing, vẫn đúng): bản đầu của test tính nguyên tử
xếp **dòng hỏng ĐỨNG TRƯỚC** dòng tốt, và mutation "`SaveChanges` trong vòng lặp" **vẫn
xanh** — dòng hỏng ném ngay vòng đầu nên dòng tốt chưa kịp ghi, tức đúng kết quả mà code
đúng cho ra. Chỉ khi **dòng tốt đi trước** thì mutation mới để lại dấu vết. Với code
đúng thì thứ tự không quan trọng chút nào (EF sắp lại theo khoá chính); nó chỉ quan
trọng với phiên bản sai — và đó chính là việc của test.
