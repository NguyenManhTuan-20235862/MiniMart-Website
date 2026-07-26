# Quy ước Giỏ hàng (hai kho lưu trữ, một nghiệp vụ)

Đọc trước khi sửa bất cứ gì liên quan giỏ hàng: `ICartStore`, `CartService`,
`CartController`, `_CartTable.cshtml`, hoặc luồng đăng nhập (vì nó gộp giỏ).

Khách vãng lai cũng phải mua được hàng, nên giỏ hàng có HAI nơi lưu: Session (chưa đăng
nhập) và bảng `Carts`/`CartItems` (đã đăng nhập). Nghiệp vụ chỉ viết MỘT lần.

## ICartStore — abstraction chia đôi nơi lưu trữ
- `ICartStore` (Domain) chỉ có `GetLinesAsync` / `SetQuantityAsync` / `RemoveAsync` /
  `ClearAsync`. `SessionCartStore` ở **Web** (phụ thuộc `HttpContext.Session`),
  `DatabaseCartStore` ở **Infrastructure**. Cùng tiền lệ với
  `WebRootProductImageStorage` phụ thuộc `IWebHostEnvironment`.
- `CartLine` (`Domain/ValueObjects`) chỉ mang `ProductId` + `Quantity`. **Không** snapshot
  giá: giỏ hàng phải hiện giá HIỆN TẠI, snapshot giá là việc của Đơn hàng.
- `SetQuantityAsync` cố ý **không nhận 0** (DB có `CHECK Quantity > 0`). Việc dịch
  "0 = xoá" nằm ở `CartService`, không ở kho.
- Bất đối xứng đã biết: `SessionCartStore` ghi ngay trong `SetQuantityAsync`, còn
  `DatabaseCartStore` chỉ đánh dấu Change Tracker và chờ `SaveChangesAsync`. `CartService`
  luôn gọi `_unitOfWork.SaveChangesAsync()` — với kho Session nó là no-op.
- Hai store phải có **test hợp đồng dùng chung** (abstract base class, hai class con). Đây
  là cách duy nhất khoá được tính thay thế lẫn nhau (Liskov). Test hợp đồng đã bắt được
  bug thật, xem gạch đầu dòng `DbSet.Local` dưới đây.
- Đường ghi của `DatabaseCartStore` phải hỏi **`_context.Carts.Local` TRƯỚC**, chỉ chạm DB
  khi Local rỗng. Bug thật đã xảy ra: hai `SetQuantityAsync` liên tiếp trước một
  `SaveChanges` thì lần thứ hai truy vấn lại DB, `Include(c => c.Items)` nạp lại collection
  từ kết quả truy vấn (DB chưa có dòng nào) và **dòng đang chờ lưu của lần thứ nhất biến
  mất**. `Local` cũng chứa entity trạng thái Added nên tránh luôn việc tạo giỏ thứ hai.

## Factory chọn kho — câu `if (đã đăng nhập)` nằm ĐÚNG một chỗ
- Đăng ký `AddScoped<ICartStore>(sp => currentUser.IsAuthenticated ? db : session)` trong
  `Program.cs`. Controller và `CartService` không hề biết Session hay DB tồn tại.
- **Scoped**, không Singleton (đóng băng quyết định của request đầu tiên cho toàn ứng dụng,
  và biến `DbContext` thành captive dependency) và không Transient (mỗi chỗ inject lại chạy
  lại factory, hai chỗ trong cùng request có thể nhận hai instance).
- `ICurrentUser` (Domain) đọc `ClaimTypes.NameIdentifier` từ `HttpContext.User`. Đây là
  loại lỗi KHÔNG có exception nào báo: chọn nhầm kho thì giỏ vẫn chạy trơn, chỉ là người đã
  đăng nhập mất giỏ sau mỗi lần restart. Phải có test riêng.
- `DatabaseCartStore` **ném `InvalidOperationException`** nếu `ICurrentUser.Id` là null.
  Đây là lỗi lập trình (factory chọn nhầm), phải nổ to thay vì âm thầm ghi vào giỏ userId=0.
- ⚠ Factory chạy **lười**. Với Controller thì luôn sau `UseAuthentication` nên quyết định
  đúng. Nếu sau này có middleware nào resolve `ICartStore` **trước** `UseAuthentication`,
  nó nhận kho Session cho cả người đã đăng nhập — hỏng im lặng.

## Gộp giỏ khi đăng nhập — cái bẫy `SignInAsync`
- `SignInAsync` **chỉ ghi cookie vào response**. Nó KHÔNG cập nhật `HttpContext.User` của
  request đang chạy (`User` chỉ do `UseAuthentication` gán, mà middleware đó đã chạy xong).
  Nên sau `SignInAsync` phải `HttpContext.User = principal;` — thiếu dòng này thì
  `ICurrentUser.Id` vẫn null và **đăng nhập đúng mật khẩu lại nhận trang 500**.
  Đã mutation test: bỏ dòng đó → 7 test đỏ.
- Gộp đặt trong `SignInUserAsync`, KHÔNG ở từng action: cả `Login` và `Register` đều đi qua
  đó nên không thể thêm đường đăng nhập thứ ba mà quên gộp.
- `MergeAsync(nguon, dich)` nhận hai `ICartStore` **tường minh** qua **Keyed DI**
  (`CartStoreKeys`), vì tại thời điểm gộp thì factory chọn sai. Đây là chỗ DUY NHẤT được
  phép bỏ qua factory. Dùng Keyed DI chứ không tiêm thẳng `DatabaseCartStore`: Composition
  Root là `Program.cs`, không phải Controller.
- Đăng ký keyed phải **uỷ nhiệm** sang registration Scoped cũ
  (`(sp, _) => sp.GetRequiredService<DatabaseCartStore>()`), không viết
  `AddKeyedScoped<ICartStore, DatabaseCartStore>()` — kiểu đó là registration riêng nên
  sinh instance thứ hai trong cùng scope.
- Gộp là **TỔNG** hai bên rồi kẹp theo tồn kho, không phải max và không phải ghi đè: người
  dùng đã chủ động thêm ở cả hai nơi nên cả hai ý định đều thật.
- Thứ tự BẮT BUỘC: **lưu giỏ đích TRƯỚC, xoá giỏ nguồn SAU** (cùng logic với thứ tự
  ghi/xoá file ảnh). Lỗi giữa đường thì giỏ nguồn còn nguyên, lần đăng nhập sau gộp lại
  được. Làm ngược là người dùng mất sạch giỏ.
- Gộp thất bại **không được chặn đăng nhập**: cookie đã ghi vào response rồi, ném ra là
  biến một lần đăng nhập THÀNH CÔNG thành trang 500. Bắt và bỏ qua `DuplicateKeyException`
  (đua tạo giỏ lần đầu) — và CHỈ exception đó, lỗi lập trình vẫn phải nổ.
- Đăng xuất KHÔNG xoá cookie Session, nên nếu quên `ClearAsync` thì số lượng **tự nhân đôi
  qua mỗi lần đăng nhập**.

## Chống IDOR bằng cấu trúc, không bằng câu `if`
- Endpoint nhận **`productId`**, TUYỆT ĐỐI không `cartItemId`. `productId` không định danh
  dòng giỏ hàng — nó chỉ là toạ độ *trong giỏ của người gửi request*, và chủ sở hữu đến từ
  cookie đã ký qua `ICurrentUser`, không từ ô nhập nào.
- Vì vậy không tồn tại cách biểu diễn thao tác trái phép. An toàn nhờ **không có đường**,
  không phải nhờ một câu kiểm tra có thể bị quên.
- Vẫn phải có test IDOR, gồm cả một **test cấu trúc** khẳng định request model chỉ có
  `ProductId` + `Quantity`. Sáu test hành vi chứng minh hôm nay an toàn; test cấu trúc tố
  giác lúc có người thêm `CartItemId` — tức lúc lỗ hổng vừa trở thành khả thi.
- Mutation đáng nhớ: đổi `RemoveAsync` từ tra trong `cart.Items` sang
  `_context.CartItems.FirstOrDefaultAsync(i => i.ProductId == productId)`. Chỉ một dòng,
  đọc rất hợp lý, giỏ hàng vẫn chạy trơn — và đó là IDOR kinh điển.

## Schema giỏ hàng
- `UNIQUE(Carts.UserId)`: một người một giỏ. Có khe race (hai request đồng thời cùng tạo
  giỏ lần đầu) → `DuplicateKeyException`, đúng tinh thần "validate ở Service, ràng buộc ở DB".
- `UNIQUE(CartItems.CartId, ProductId)` — **hai cột**, không phải chỉ `ProductId`: cùng một
  sản phẩm ở hai giỏ khác nhau là hợp lệ. Có test riêng khoá điều này.
- `CHECK ([Quantity] > 0)`.
- `CartItems → Products` dùng **Cascade**, cố ý KHÁC `Category → Product` (Restrict). Lý do:
  xoá sản phẩm khỏi shop không được bị chặn chỉ vì có người để nó trong giỏ.
- `Cart`/`CartItem` **chưa có `RowVersion`** — có chủ đích, xem nợ kỹ thuật.

## Controller giỏ hàng
- Một action phục vụ HAI đường: có `X-Requested-With: XMLHttpRequest` → `PartialView`;
  form POST thường → `RedirectToAction` + `TempData` (Post-Redirect-Get). Nhờ vậy giỏ hàng
  chạy đầy đủ **không cần một dòng JavaScript nào**.
- Hết hàng / sản phẩm đã bị xoá / số lượng bị kẹp đều là **kết cục nghiệp vụ**, trả HTTP
  200 kèm thông báo. Trả 500 là biến việc bình thường thành sự cố.
- Số lượng đi qua header `X-Cart-Count` (chữ số ASCII), **thông báo tiếng Việt thì không**:
  giá trị header phải là ASCII/latin1. Thông báo đi trong thân response qua ViewModel.
- Request model phải có **trần số lượng** (`[Range(1, 100)]`): `CartService` cộng dồn số mới
  vào số đang có, nên `quantity = int.MaxValue` làm phép cộng **tràn số** và ra số âm.
- `/Cart/Summary` trả `Json` — ngoại lệ CÓ CHỦ ĐÍCH với quy ước "AJAX thì trả PartialView":
  badge cần một CON SỐ, không cần một khối HTML.
