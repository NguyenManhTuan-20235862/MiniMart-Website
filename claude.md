# MiniMart - ElectroShop

## Kiến trúc
Controller → Service → Repository → EF Core/Dapper → SQL Server

## Cấu trúc Solution
- MiniMart.Web             : ASP.NET Core MVC (Controller, View, ViewModel), Composition Root
- MiniMart.Application      : Service layer (IService + business logic)
- MiniMart.Domain           : Entity, Repository interface (IRepository), IUnitOfWork
- MiniMart.Infrastructure   : EF Core DbContext, Repository impl, UnitOfWork, Dapper
- MiniMart.Common           : Helper, Constants, Custom Exception
- MiniMart.Tests            : xUnit + Moq (unit test) và WebApplicationFactory (integration test)

## Quy ước code (luôn áp dụng)
- Controller KHÔNG chứa business logic, chỉ gọi Service.
- IRepository đặt ở Domain (Domain khai báo thứ nó CẦN từ tầng lưu trữ).
- IService đặt ở Application, cùng tầng với implementation của nó. Domain giữ thuần nghiệp vụ.
- Service phụ thuộc IRepository (Domain), không phụ thuộc trực tiếp Infrastructure (DIP).
- Đặt tên: IProductRepository / ProductRepository, IProductService / ProductService.
- Toàn bộ thao tác DB dùng async/await, mọi method public nhận CancellationToken.
- `SaveChangesAsync` CHỈ nằm ở `IUnitOfWork`, không bao giờ ở Repository.
- Application và Web KHÔNG được `using Microsoft.EntityFrameworkCore`.
- Form dùng **ViewModel riêng**, không bind thẳng Entity (chống over-posting).
- Mọi POST có `[ValidateAntiForgeryToken]`. Xoá phải là POST, không dùng GET.
- Tên test viết bằng tiếng Việt không dấu, mô tả hành vi mong đợi.
- **Mutation test bắt buộc**: sau khi viết test, cố tình phá code để xác nhận test đỏ.
  Test xanh chưa chứng minh được gì.

## Quy ước chi tiết — ĐỌC FILE TƯƠNG ỨNG TRƯỚC KHI SỬA CODE

Đây không phải tài liệu tham khảo tuỳ chọn. Mỗi file chứa những quyết định đã cân nhắc
kỹ kèm lý do, và nhiều trong số đó là loại **sai mà không có lỗi nào báo**. Sửa code
trong một vùng mà chưa đọc file của vùng đó là gần như chắc chắn vi phạm quy ước.

| Sắp sửa gì | Đọc trước |
|---|---|
| Domain / Application / Infrastructure, hoặc viết truy vấn | `.claude/rules/data-access.md` |
| Bất kỳ file nào trong MiniMart.Web (Controller, View, JS, upload) | `.claude/rules/web.md` |
| Giỏ hàng, hoặc luồng đăng nhập (nó gộp giỏ) | `.claude/rules/cart.md` |
| Luồng Edit của Product, UnitOfWork, bất cứ gì chạm RowVersion | `.claude/rules/concurrency.md` |
| AccountController, cấu hình cookie/authorization, UserService | `.claude/rules/auth.md` |
| Bất kỳ test nào | `.claude/rules/testing.md` |
| .editorconfig, Directory.Packages.props, .csproj, CI | `.claude/rules/build.md` |

Skill `.claude/skills/` phụ trách quy trình (tạo Repository/Service, tạo migration,
workflow học-và-làm). File `rules/` phụ trách quy ước. Hai thứ bổ sung nhau, không thay thế.

## Bẫy im lặng — sai mà KHÔNG có exception hay warning nào

Danh sách này ở lại file lõi vì đây đúng là những thứ không thể tự phát hiện lại: code
build được, chạy được, và chỉ sai. Chi tiết + lý do nằm trong file `rules/` tương ứng.

| Viết thế này | Chuyện xảy ra |
|---|---|
| `ToString("N0")` thay `MoneyFormat.ToMoneyText()` | Máy en-US in `111,000`, máy vi-VN in `111.000` |
| `asp-for` cho `byte[] RowVersion` | Render ra `"System.Byte[]"`, concurrency biến mất |
| `SetExpectedRowVersion` ghi `CurrentValue` | Không phát hiện xung đột, lost update |
| Thiếu `enctype="multipart/form-data"` | `IFormFile` luôn null |
| `PartialView` → `View` | Response kèm cả layout, trang lồng trong trang |
| Chuỗi tự chế thay `ClaimTypes.Role` | `IsInRole` và `[Authorize(Roles=…)]` luôn false |
| `new ClaimsIdentity(claims)` thiếu tham số 2 | `IsAuthenticated` = false dù đủ claims |
| `UseRateLimiter()` trước `UseRouting()` | Không giới hạn gì cả |
| Thiếu `HttpContext.User = principal` sau `SignInAsync` | `ICurrentUser.Id` null → giỏ hàng đổ |
| `type="text"` cho ô số ở form lọc | vi-VN gõ `1.000.000` → bind thành `null` |
| `Skip`/`Take` không có tie-breaker trong `OrderBy` | Bản ghi trùng/mất giữa hai trang |
| Sai tên layout trong `_ViewStart` | Build qua, nổ khi mở trang |
| `Task.WhenAll` nhiều `await` dùng chung `DbContext` | "A second operation was started" |
| Factory chọn nhầm kho giỏ hàng | Giỏ vẫn chạy, chỉ là mất dữ liệu |
| Thiếu `ConvertValueInInvariantCulture` ở `[Range]` tiền | Máy vi-VN parse `"0.01"` sai |
| Đổi tên một `data-*` mà JS đang `querySelector` | `null` → JS ngừng chạy, không lỗi nào |
| Bọc node JS cần cập nhật trong `@if` | Lần đầu cần cập nhật thì không có node |
| Xét nhánh PartialView trước nhánh JSON | Client nhận HTML, `response.json()` ném |
| `Stock -= n` mà cột không phải concurrency token | **Oversell** — đã đo: bán 10 khi có 5 |
| Chạm nhiều dòng không theo thứ tự cố định | Deadlock, chỉ xuất hiện dưới tải |
| Nhận `userId` từ form thay vì `ICurrentUser` | IDOR: đặt đơn / đọc đơn dưới tên người khác |
| Quên `builder.Ignore()` cho property tính toán | Migration sinh cột không bao giờ được ghi |
| Đọc lại `product.Price` khi tính tổng đơn | Tổng đơn lệch khỏi tổng các dòng |

## Môi trường
- .NET SDK 10, SQL Server 2025 Express, instance `SQLEXPRESS`.
- Connection string ở `appsettings.json`, dùng `Trusted_Connection=True` nên không có
  mật khẩu. Nếu chuyển sang SQL Authentication thì BẮT BUỘC dùng User Secrets.
- `dotnet-ef` cài global.
- sqlcmd để kiểm tra DB trực tiếp:
  `"C:\Program Files\Microsoft SQL Server\Client SDK\ODBC\180\Tools\Binn\SQLCMD.EXE" -S "localhost\SQLEXPRESS" -E -d MiniMart -C -Q "<sql>"`

## Lệnh hay dùng
- dotnet build
- dotnet test
- dotnet format --verify-no-changes   ← là một dạng test, phải sạch
- dotnet run --project MiniMart.Web --launch-profile http
- dotnet ef migrations add <TenMigration> -p MiniMart.Infrastructure -s MiniMart.Web
- dotnet ef database update -p MiniMart.Infrastructure -s MiniMart.Web

## Lưu ý đặc biệt
- Products.RowVersion dùng cho Optimistic Concurrency, không được xóa/sửa kiểu dữ liệu này khi generate code.
- Xoá Category dùng `DeleteBehavior.Restrict`, KHÔNG đổi sang Cascade.
- `CartItems → Products` dùng `Cascade` — cố ý KHÁC Category, đừng "sửa cho nhất quán".
- Cột tiền tệ luôn là `decimal` với `HasPrecision(18, 2)`, không dùng double/float.

## Nợ kỹ thuật đã biết (cố ý chưa làm, đừng "sửa" nửa vời)

### Hoãn có chủ đích — KHÔNG làm cho tới khi điều kiện đủ
- **Trang chi tiết sản phẩm chưa làm**, nên `/Product` trả 404 (chỉ có action `LoadMore`).
  Đây là **tính năng chưa làm**, không phải nợ kỹ thuật — không có gì hỏng, chỉ là chưa có.
- `Cart`/`CartItem` **chưa có `RowVersion`**, và chưa cần: giỏ hàng là dữ liệu của MỘT
  người, không có hai người cùng sửa một giỏ. Trường hợp duy nhất là một người mở hai tab,
  và ở đó "ghi sau thắng" đúng ý muốn của họ. Việc cần khoá thật là **trừ tồn kho lúc đặt
  hàng** — nhưng khoá trên `Products.RowVersion` (đã có), không phải trên giỏ.
- Nhánh **`PartialView` của ba endpoint ghi giỏ hàng** (`X-Requested-With` mà không kèm
  `Accept: application/json`) hiện **không có client nào dùng** — dropdown dùng nhánh JSON,
  trang `/Cart` dùng nhánh PRG. Giữ lại vì nó là đường tự nhiên nếu sau này trang `/Cart`
  cần cập nhật cả bảng bằng AJAX, và nó có test. Đừng xoá mà cũng đừng tưởng nó đang chạy.

### Nợ thật, chưa trả
- **Hai file JS KHÔNG có test chạy thật** (`home-load-more.js`, `cart-dropdown.js`) — dự án
  chưa có headless browser (Playwright). Kiểm chứng được: HTML mà endpoint trả về, hình
  dạng JSON, header `X-Next-Page`/`X-Cart-Count`, sự tồn tại của từng hook `data-*`
  (integration test), cú pháp JS (`node --check`). **Chưa** kiểm chứng được: `fetch` chạy
  thật, `insertAdjacentHTML`/`textContent` cập nhật đúng node, cờ chặn double-click, cờ
  `canDungLai` của dropdown, việc gỡ node khi xoá dòng. Đây là khoảng trống test lớn nhất
  còn lại; thêm Playwright đáng một phase riêng.
- **CI chưa từng chạy thật.** File `.github/workflows/ci.yml` đã viết nhưng chỉ xác nhận
  được phần chạy được ở local (`dotnet format --verify-no-changes` sạch, `dotnet test` xanh).
  Service container SQL Server, `sqlcmd` trong image `mssql/server:2022-latest`, và
  `dotnet ef database update` trên CI đều **chưa có bằng chứng** — phải push rồi xem lần
  chạy đầu, đừng tin file YAML chỉ vì nó trông đúng.
- Xung đột concurrency mới xử lý cho **Product**. `Category` chưa có `RowVersion` nên sửa
  danh mục đồng thời vẫn ghi đè lẫn nhau. Chưa cấp bách vì danh mục ít bị sửa.
- Rate limit dùng bộ nhớ **trong tiến trình**: chạy nhiều instance thì mỗi instance có hạn
  mức riêng, tổng hạn mức nhân lên theo số instance. Cần Redis khi scale ngang.
- **Session giỏ hàng cũng nằm trong bộ nhớ tiến trình** — cùng loại hạn chế. Restart server
  là mất giỏ của mọi khách vãng lai; chạy nhiều instance thì mỗi instance một giỏ khác nhau.
  Cùng một lần thêm Redis giải quyết được cả hai.
- Đua tạo giỏ lần đầu (`DuplicateKeyException` từ `UNIQUE(Carts.UserId)`) hiện chỉ hiện
  "Vui lòng thử lại", chưa **thử lại thật**. Thử lại sạch sẽ đòi một `DbContext` mới nên
  không làm được trong cùng request. Cửa sổ race rất hẹp (chỉ request đầu tiên của một tài
  khoản mới) nên chưa đáng đổi lấy độ phức tạp.
- **Đặt hàng chưa có retry khi xung đột.** Optimistic Concurrency chống oversell tuyệt đối,
  nhưng 10 người bấm cùng lúc khi còn 5 hàng thì chỉ **1** đơn thành công — 9 người còn lại
  nhận "vui lòng cập nhật giỏ hàng" dù kho vẫn còn 4. Đã đo bằng test. Cần retry (hoặc đổi
  sang Pessimistic `UPDLOCK`) khi có flash sale; chưa đáng làm bây giờ. Xem
  `.claude/rules/concurrency.md`.
- **Transaction tường minh trong `CheckoutAsync` hiện chưa chịu lực.** Mọi thao tác ghi đi
  qua MỘT `SaveChangesAsync`, mà EF Core đã tự bọc mỗi `SaveChanges` trong transaction ngầm —
  mutation test xác nhận bỏ nó đi thì cả 5 integration test vẫn xanh. Giữ lại vì nó đúng
  ngay khi có `SaveChanges` thứ hai (bản ghi thanh toán), nhưng **đừng tưởng nó đang bảo vệ
  atomicity hôm nay**.
- **Chưa có trang "Đơn hàng của tôi".** Đã có `IOrderService.GetMyOrderAsync` cho một đơn,
  chưa có danh sách. Index `(UserId, CreatedAt DESC)` trên `Orders` đã dựng sẵn cho việc đó.
- **`Order` chưa có `Status`** — cố ý. Thêm cột trạng thái trước khi biết đơn có những trạng
  thái nào là đoán, cùng lý do đã hoãn API transaction cho tới đúng lúc cần.

### Đã trả (giữ lại để không ai "sửa" ngược)
- ~~Round-trip RowVersion~~ → đã làm, xem `.claude/rules/concurrency.md`.
- ~~3 lỗ hổng auth~~ → đã vá cả ba, xem `.claude/rules/auth.md`.
- ~~`Directory.Packages.props`, `.editorconfig`, CI~~ → đã có, xem `.claude/rules/build.md`.
- ~~Validate `minPrice`/`maxPrice`~~ → đã làm bằng Data Annotation + `IValidatableObject`
  trên `ProductFilter`, KHÔNG phải ở Service: ràng buộc này không cần DB, không cần async,
  không cần DI — đúng tiêu chí ở `.claude/rules/data-access.md`.
- ~~Giỏ hàng cho khách vãng lai~~ → đã làm bằng `ICartStore` + factory Scoped, xem
  `.claude/rules/cart.md`. Đừng "đơn giản hoá" bằng cách bắt đăng nhập mới mua được.
- ~~Chống IDOR trên giỏ hàng~~ → đã làm bằng cấu trúc (`productId`, không `cartItemId`), có
  7 test và 3 mutation. Đừng thêm `cartItemId` vào request model "cho tiện".
- ~~`IUnitOfWork` chưa có API transaction~~ → đã thêm **đúng lúc** làm nghiệp vụ đặt hàng,
  như điều kiện đã đặt ra từ đầu. `ITransaction` ở Domain bọc `IDbContextTransaction`.
- ~~Trừ tồn kho chống oversell~~ → đã làm bằng Optimistic trên `Products.RowVersion`, có
  test song song trên SQL Server thật. Đừng đổi `IsRowVersion()` — mutation đã chứng minh
  bỏ nó đi là bán 10 món khi chỉ có 5.

## Cách làm việc với tôi (người học)
- Tôi đang học song song, nên MỌI đoạn code Claude Code viết ra đều phải kèm:
  1. Giải thích ngắn gọn: đoạn này làm gì, tại sao chọn cách này.
  2. Chỉ rõ những điểm liên quan trực tiếp đến kiến thức tôi đang học (LINQ, SOLID, DI, EF Core, Auth...).
  3. Nếu có 2 cách làm khác nhau (VD: Optimistic vs Pessimistic locking), giải thích cả 2 và lý do chọn 1.
- Với các phần khó (Transaction, Concurrency, Bulk Update), giải thích Ý TƯỞNG trước khi viết code.
- Cuối mỗi Phase: cập nhật file này (hoặc file `rules/` tương ứng) với pattern/quy ước mới
  phát sinh. Quy ước chi tiết vào `rules/`; chỉ thứ luôn-đúng-mọi-lúc mới thêm vào file lõi.
