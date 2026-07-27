# Quy ước tầng dữ liệu — Repository, Service, EF Core

Đọc file này trước khi sửa `MiniMart.Domain`, `MiniMart.Application`,
`MiniMart.Infrastructure`, hoặc trước khi viết bất kỳ truy vấn nào.

## Validation — đặt ở đâu
Tiêu chí phân loại, không phải cảm tính:
- **Data Annotation** cho ràng buộc thuộc về BẢN THÂN giá trị: bắt buộc nhập, độ dài,
  khoảng số, định dạng. Đánh giá được mà không cần biết gì ngoài chính giá trị đó.
- **Service** cho mọi ràng buộc cần một trong ba thứ: truy vấn DB, `async`, hoặc
  dependency từ DI. Lý do `ValidationAttribute.IsValid` có chữ ký ĐỒNG BỘ — query DB
  trong đó buộc phải `.GetAwaiter().GetResult()`, gây thread-pool starvation.
- Ví dụ đã áp dụng: `CategoryId` có `[Range(1, ...)]` để hỏi "đã chọn chưa", còn
  "danh mục đó có tồn tại không" nằm ở `ProductService.BaoDamDanhMucTonTaiAsync`.
- Cột tiền tệ: dùng overload `[Range(typeof(decimal), "0.01", "...")]` — overload
  `Range(0.01, ...)` nhận `double` nên làm tròn nhị phân trước khi so với `decimal`.
- **QUY ƯỚC ĐÃ SỬA — trước đây file này ghi SAI.** Phải bật **CẢ HAI** cờ culture, vì
  chúng chi phối hai việc khác nhau:

  | Cờ | Chi phối |
  |---|---|
  | `ParseLimitsInInvariantCulture` | parse hai chuỗi **CẬN** (`"0.01"`, `"999999999"`) |
  | `ConvertValueInInvariantCulture` | chuyển đổi **GIÁ TRỊ** đang được kiểm |

  Bản cũ chỉ đặt `ConvertValueInInvariantCulture`, nên hai chuỗi cận vẫn được parse theo
  `CurrentCulture`. **Đã đo trực tiếp dưới vi-VN**: chỉ `ConvertValue` → `IsValid` **ném
  `ArgumentException`** (không phải "parse ra số khác" như tài liệu cũ mô tả), tức form
  Admin trả **HTTP 500** trên máy vi-VN; chỉ `ParseLimits` → chạy đúng. Ba nơi đã bị:
  `ProductFormViewModel.Price`, `ProductFilter.MinPrice/MaxPrice`,
  `ProductBulkUpdateDto.Price` — đã vá cả ba.
- Có `[Theory]` liệt kê **mọi** cột tiền khẳng định cả hai cờ; thêm cột tiền mới thì
  thêm một dòng vào đó.
- ⚠ Khi viết test cho việc này: `RangeAttribute` **cache** phép chuyển đổi chuỗi→số ở
  lần `IsValid` **đầu tiên**. Test ép `CurrentCulture` rồi gọi `Validator.TryValidateObject`
  sẽ **xanh giả** nếu một test khác trong cùng tiến trình đã chạy trước dưới culture khác.
  Phải lấy **instance mới** của attribute rồi mới gọi `IsValid`, hoặc tốt hơn là khẳng
  định thẳng vào thuộc tính cờ.
- Ràng buộc liên quan HAI thuộc tính (`minPrice > maxPrice`) dùng `IValidatableObject`,
  không phải attribute trên một property. Nó chạy SAU khi từng property đã hợp lệ nên
  không phải kiểm tra null hay kiểu dữ liệu lại.

## Unit of Work
- `SaveChangesAsync` nằm ở **IUnitOfWork**, TUYỆT ĐỐI không thêm lại vào Repository.
  Lý do: mọi Repository dùng chung một DbContext (Scoped), nên SaveChanges trên
  một Repository thực chất commit thay đổi của tất cả — tên gọi sẽ nói dối.
- Repository chỉ `AddAsync` / `Remove` / truy vấn. Service gọi `_unitOfWork.SaveChangesAsync()`.

## Đặt tên method truy vấn trong Repository
- `GetByIdAsync` → `AsNoTracking`, dùng cho đường **đọc** (hiển thị).
- `GetForUpdateAsync` → **có tracking**, bắt buộc dùng cho đường **sửa**.
  Entity AsNoTracking sửa xong gọi SaveChanges sẽ không lưu gì, và mất luôn
  RowVersion gốc nên không phát hiện được xung đột.
- Truy vấn trả về entity có navigation property phải `Include` đầy đủ.

## Truy vấn có lọc và phân trang
- Bộ lọc tuỳ chọn: dựng `IQueryable` rồi `if (x.HasValue) query = query.Where(...)`.
  Nhiều `Where` chồng nhau được dịch thành MỘT câu SQL với `AND`, không phải nhiều lần
  truy vấn. Chỉ `ToListAsync`/`CountAsync` mới thực sự chạm DB.
- `Skip`/`Take` BẮT BUỘC đi kèm `OrderBy` **có tie-breaker duy nhất** (`ThenBy(p => p.Id)`).
  Thiếu tie-breaker thì bản ghi trùng khoá sắp xếp có thể xuất hiện ở hai trang liền nhau
  trong khi bản ghi khác biến mất.
- Phương thức phân trang trả `PagedResult<T>` (có `TotalCount`), không trả `List<T>`:
  giao diện cần tổng số bản ghi để biết còn trang sau hay không. `TotalCount` đếm theo
  BỘ LỌC, chưa phân trang — hai query trên cùng một `IQueryable`.
- Luôn kẹp `page` và `pageSize` đến từ query string (`page < 1` → 1, `pageSize` clamp
  tối đa 100) để `?pageSize=999999` không kéo sập server.
- EF Core **nhúng thẳng hằng số** vào SQL và chỉ tham số hoá giá trị đến từ **biến**.
  Viết literal trong query làm mỗi giá trị sinh một câu SQL riêng, phá vỡ tái sử dụng
  execution plan. Luôn truyền qua biến.
- Dùng `ToQueryString()` để xem SQL mà không chạy query; dùng nó trong test để khoá
  các tính chất quan trọng (có `OFFSET/FETCH`, có tham số hoá, có tie-breaker).
- Đường **ghi** phải hỏi Change Tracker (`DbSet.Local`) TRƯỚC khi truy vấn DB, khi có
  nhiều thao tác trước một `SaveChanges`. Xem `.claude/rules/cart.md` — đây là chỗ đã
  phát sinh một bug thật.

## Validate nghiệp vụ — luôn làm ở HAI nơi
Quy tắc chung của dự án: **validate ở Service để có thông báo tử tế, ràng buộc ở DB để có sự thật.**
- Kiểm tra ở Service luôn có khe TOCTOU (giữa lúc kiểm tra và lúc lưu).
- Vì vậy mọi quy tắc quan trọng phải có ràng buộc DB đi kèm: unique index,
  check constraint, hoặc foreign key.
- Ví dụ đã áp dụng: Username (unique index), Stock/Price >= 0 (check constraint),
  không xoá Category còn Product (FK Restrict), `UNIQUE(CartItems.CartId, ProductId)`.

## Dịch exception theo tầng
- Infrastructure biết EF Core và mã lỗi SQL Server, dịch sang exception CHUNG
  (`DuplicateKeyException`) — đặt trong `MiniMart.Common/Exceptions`.
- Application diễn giải exception chung đó thành exception NGHIỆP VỤ theo ngữ cảnh
  (`UsernameAlreadyExistsException`, `CategoryNameAlreadyExistsException`).
- Application và Web KHÔNG được `using Microsoft.EntityFrameworkCore`.
- **QUY ƯỚC ĐÃ ĐỔI**: `DbUpdateConcurrencyException` trước đây "cố ý không bọc lại".
  Nay `UnitOfWork` **bọc nó thành `ConcurrencyConflictException`**. Lý do đổi: khi tầng
  Web bắt đầu xử lý xung đột, quy ước cũ tự mâu thuẫn — Web phải bắt một exception của
  EF Core, mà Web lại không được `using Microsoft.EntityFrameworkCore`. Không thể giữ
  cả hai quy ước, nên bỏ cái yếu hơn.
- `ConcurrencyConflictException` gộp cả hai nguyên nhân: RowVersion lệch **và** bản ghi
  đã bị xoá. Cả hai đều là "0 dòng khớp WHERE", và với người dùng đều là "người khác
  đã thay đổi" — phân biệt ở tầng exception không mang lại gì.
- `NotFoundException` mang theo `EntityName`, và Controller PHẢI dùng nó để phân biệt:
  thiếu chính đối tượng đang thao tác → `NotFound()`; thiếu đối tượng được tham chiếu
  → `AddModelError` vào đúng ô nhập rồi render lại form.

## Snapshot: giỏ hàng KHÔNG chốt, đơn hàng chốt TẤT CẢ
- `CartItem` cố ý không snapshot gì — giỏ hàng phải hiện giá HIỆN TẠI.
- `OrderDetail` snapshot cả `UnitPrice` **và** `ProductName`. Snapshot cả tên chứ không chỉ
  giá: shop đổi tên sản phẩm thì đơn cũ phải hiện đúng cái tên khách đã thấy lúc mua.
- `Order.TotalAmount` lưu ra cột riêng dù tính lại được: đây là con số ràng buộc với khách,
  nó phải là dữ liệu chứ không phải kết quả một phép tính có thể đổi khi code đổi.
- Tổng đơn tính từ giá **đã snapshot**, không đọc lại `product.Price` — hai chỗ đọc giá là
  hai cơ hội để tổng đơn lệch khỏi tổng các dòng.
- `LineTotal` là thuộc tính tính toán, phải `builder.Ignore(...)`. Thiếu dòng đó thì EF Core
  coi nó là cột và migration sinh ra một cột không bao giờ được ghi.

## DeleteBehavior: cùng một khoá ngoại, hai câu trả lời khác nhau
Tiêu chí là **dữ liệu tạm hay bản ghi lịch sử**, không phải "cho nhất quán":

| Khoá ngoại | Hành vi | Vì sao |
|---|---|---|
| `Category → Product` | Restrict | Xoá danh mục không được âm thầm xoá sạch sản phẩm |
| `CartItem → Product` | **Cascade** | Giỏ là dữ liệu tạm; hàng ngừng bán phải tự biến khỏi mọi giỏ |
| `CartItem → Cart` | Cascade | Dòng giỏ không tồn tại độc lập |
| `OrderDetail → Product` | **Restrict** | Đơn hàng là bản ghi TÀI CHÍNH; Cascade là để lịch sử tự sửa lại chính nó |
| `OrderDetail → Order` | Cascade | Dòng đơn không tồn tại độc lập |
| `Order → User` | Restrict | Đơn phải sống lâu hơn tài khoản đặt nó |

- Hệ quả cố ý: **sản phẩm đã từng được đặt thì không xoá được nữa**; tài khoản đã đặt hàng
  cũng vậy. Việc đúng với hàng ngừng bán là đặt tồn kho về 0, không phải xoá.
- Mọi ràng buộc Restrict phải có **thông báo tử tế** ở Service (`CategoryHasProductsException`,
  `ProductHasOrdersException`), theo khuôn: kiểm TRƯỚC khi xoá, khoá ngoại là bảo đảm cuối.
- `UnitOfWork` dịch mã lỗi SQL **547** thành `ReferenceConstraintException` để bịt khe
  TOCTOU (có người vừa đặt hàng giữa lúc kiểm và lúc lưu). Không có bước dịch này thì
  trường hợp hiếm đó cho ra HTTP 500 kèm thông báo của EF Core.
- Đã mutation test: **bỏ lệnh kiểm ở Service thì không test nào đỏ** — vì nhánh dịch 547 tạo
  ra ĐÚNG cùng một exception. Đó là tính chất tốt, không phải lỗ hổng test: lệnh kiểm là
  đường đẹp, khoá ngoại mới là thứ bảo đảm.

## Vòng đời connection: vì sao EF Core không cần lo, còn Dapper thì có

**Trạng thái hiện tại (đã rà soát): dự án CHƯA dùng Dapper ở đâu cả.** Package được khai
báo sẵn ở `Directory.Packages.props` + `MiniMart.Infrastructure.csproj` nhưng không một
file `.cs` nào `using Dapper`. Mục này viết trước để câu Dapper ĐẦU TIÊN không phải câu
đi tìm quy ước.

### EF Core — không phải "DI dọn hộ", mà là hai cơ chế chồng lên nhau
1. **EF tự mở và đóng connection quanh TỪNG câu lệnh.** `DbContext` không giữ connection
   mở suốt scope; nó mở trước khi chạy lệnh và đóng ngay sau, trừ khi có transaction
   tường minh đang mở hoặc ai đó tự gọi `OpenAsync()`.
2. **`DbContext` đăng ký Scoped**, mà DI container theo dõi mọi service Scoped implement
   `IDisposable` và gọi `DisposeAsync` khi scope kết thúc (cuối request). Nên kể cả khi
   còn thứ gì đang mở, nó vẫn được trả lại.

Vì vậy `using` quanh `DbContext` là **sai** trong code ứng dụng: nó dispose một object mà
container vẫn đang sở hữu và sẽ dispose lần nữa. Trong test thì `scope.Dispose()` mới là
ranh giới đúng.

### Dapper — không có cơ chế nào trong hai cơ chế trên
Dapper là **extension method trên `IDbConnection`**. Nó không tạo connection, không sở
hữu connection, không biết DI tồn tại. Toàn bộ vòng đời là việc của người gọi:

```csharp
await using var connection = new SqlConnection(_chuoiKetNoi);   // TA tạo -> TA dispose
var rows = await connection.QueryAsync<Row>(sql, thamSo);
```

- `await using` chứ không `using`: `SqlConnection` có `DisposeAsync`, và bản đồng bộ chặn
  luồng khi trả connection về pool.
- **Không `Open()` thủ công** — Dapper tự mở nếu connection đang đóng, và tự đóng lại đúng
  trạng thái ban đầu.

### Hỏng thế nào nếu quên
`Dispose()` **không** đóng kết nối TCP — nó **trả connection về pool**. Quên dispose là
connection bị giữ vĩnh viễn ngoài pool. Pool mặc định 100; khi cạn thì request tiếp theo
chờ 30 giây rồi ném:

> `Timeout expired. The timeout period elapsed prior to obtaining a connection from the pool.`

Lỗi nổ ở **một truy vấn chẳng liên quan gì** tới chỗ rò rỉ, và **chỉ xuất hiện dưới tải** —
nên nó qua được toàn bộ test và mọi lần thử tay. Cùng họ với các bẫy im lặng khác của dự án.

### ⚠ Ngoại lệ: connection MƯỢN của EF thì TUYỆT ĐỐI không dispose
Khi cần Dapper chạy **chung transaction** với EF (xem `concurrency.md`):

```csharp
var connection = _context.Database.GetDbConnection();      // MƯỢN - KHÔNG using
var transaction = _context.Database.CurrentTransaction?.GetDbTransaction();
var rows = await connection.QueryAsync<Row>(sql, thamSo, transaction);
```

Bọc `using` ở đây là đóng connection của chính `DbContext` giữa chừng: mọi lệnh EF sau đó
trong cùng request đổ, và transaction đang mở bị huỷ. Quy tắc không phải "luôn `using`" mà
là **dispose thứ mình TẠO RA, không dispose thứ mình MƯỢN**.

### Vẫn giữ nguyên phân công
Dapper dành cho **truy vấn ĐỌC phức tạp**; đường ghi đã có EF lo (Change Tracker tự kẹp
`RowVersion` vào `WHERE`, tự sắp thứ tự chống deadlock, tự batch). Xem bảng so sánh ba
cách ở `concurrency.md`.

## Đăng ký DI
- Không viết `AddScoped` rời rạc trong `Program.cs`. Dùng extension method
  `AddApplication()` (Application) và `AddInfrastructure(configuration)` (Infrastructure).
- Repository / Service / UnitOfWork: **Scoped** (đồng bộ với DbContext).
- Chỉ dùng Singleton cho thứ không giữ state và không phụ thuộc gì Scoped
  (ví dụ `IPasswordHasher<User>`), tránh captive dependency.
- Service phụ thuộc `HttpContext` hoặc `IWebHostEnvironment` thuộc tầng **Web**, đăng ký
  trực tiếp trong `Program.cs`, không nằm trong `AddApplication`/`AddInfrastructure`.
  Đã áp dụng: `WebRootProductImageStorage`, `SessionCartStore`, `HttpContextCurrentUser`.
