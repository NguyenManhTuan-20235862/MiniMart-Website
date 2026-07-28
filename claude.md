# MiniMart - ElectroShop

## Kiến trúc
Controller → Service → Repository → EF Core → SQL Server

(Dapper đã được **gỡ**: khai sẵn mà không dùng là một phụ thuộc phải nâng cấp và quét
lỗ hổng để đổi lấy con số không. Quy ước dùng nó vẫn nằm ở `rules/data-access.md` cho
lúc thật sự cần một truy vấn ĐỌC phức tạp.)

## Cấu trúc Solution
- MiniMart.Web             : ASP.NET Core MVC (Controller, View, ViewModel), Composition Root
- MiniMart.Application      : Service layer (IService + business logic)
- MiniMart.Domain           : Entity, Repository interface (IRepository), IUnitOfWork
- MiniMart.Infrastructure   : EF Core DbContext, Repository impl, UnitOfWork
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
| VNPay, chữ ký, `IVnPayService`, `VnPayOptions` | `.claude/rules/payments.md` |
| Bất kỳ test nào | `.claude/rules/testing.md` |
| .editorconfig, Directory.Packages.props, .csproj, CI | `.claude/rules/build.md` |

Skill `.claude/skills/` phụ trách quy trình (tạo Repository/Service, tạo migration,
workflow học-và-làm). File `rules/` phụ trách quy ước. Hai thứ bổ sung nhau, không thay thế.

## Bẫy im lặng — sai mà KHÔNG có exception hay warning nào

Danh sách này ở lại file lõi vì đây đúng là những thứ không thể tự phát hiện lại: code
build được, chạy được, và chỉ sai. Chi tiết + lý do nằm trong file `rules/` tương ứng.

| Viết thế này | Chuyện xảy ra |
|---|---|
| Hai phần tử cùng `name` trong MỘT form (nút submit mang `value` + ô nhập) | Trình duyệt gửi CẢ HAI, model binder cho `int` lấy giá trị **đầu tiên theo thứ tự DOM**. Nút `−` đứng trước ô nhập nên chạy, nút `+` đứng sau nên **không làm gì cả** — cùng một đoạn code, hai kết quả, không exception nào. Mỗi form phải chứa đúng MỘT phần tử cho mỗi tên |
| `<form>` của bộ lọc bao luôn cả lưới sản phẩm | **Form lồng trong form.** Trình duyệt VỨT BỎ thẻ `<form>` bên trong, nút "Thêm vào giỏ" của thẻ ĐẦU TIÊN rơi vào form lọc (`get /`) → bấm chỉ chạy lại bộ lọc, không thêm gì vào giỏ. **Chuỗi HTML server gửi đi hoàn toàn đúng**, nên chỉ test chạy trong trình duyệt thật mới thấy |
| Dropdown có nút bấm mà thiếu `data-bs-auto-close="outside"` | Mặc định Bootstrap đóng menu khi bấm **bên trong** → sập ngay lần bấm đầu, và nhánh JS `if (container.classList.contains('show'))` không bao giờ chạy nên dòng vừa xoá vẫn nằm lại DOM |
| `ToString("N0")` thay `MoneyFormat.ToMoneyText()` | Máy en-US in `111,000`, máy vi-VN in `111.000` |
| `asp-for` cho `byte[] RowVersion` **thiếu `type="hidden"`** | Render ra `"System.Byte[]"`, concurrency biến mất (có `type="hidden"` thì lại đúng — đã đo) |
| Server sửa một giá trị mà quên `ModelState.Remove` | `asp-for` vẫn render giá trị BẬY của người dùng |
| `SetExpectedRowVersion` ghi `CurrentValue` | Không phát hiện xung đột, lost update |
| Thiếu `enctype="multipart/form-data"` | `IFormFile` luôn null |
| `PartialView` → `View` | Response kèm cả layout, trang lồng trong trang |
| Chuỗi tự chế thay `ClaimTypes.Role` | `IsInRole` và `[Authorize(Roles=…)]` luôn false |
| `new ClaimsIdentity(claims)` thiếu tham số 2 | `IsAuthenticated` = false dù đủ claims |
| `UseRateLimiter()` trước `UseRouting()` | Không giới hạn gì cả |
| Giữ `UseExceptionHandler` **cùng** `GlobalExceptionMiddleware` | Lớp trong bắt trước → middleware tự viết không chạy ở Production, nhưng vẫn chạy ở Development: thứ được test không phải thứ chạy thật |
| Thiếu `HttpContext.User = principal` sau `SignInAsync` | `ICurrentUser.Id` null → giỏ hàng đổ |
| `type="text"` cho ô số ở form lọc | vi-VN gõ `1.000.000` → bind thành `null` |
| `Skip`/`Take` không có tie-breaker trong `OrderBy` | Bản ghi trùng/mất giữa hai trang |
| Sai tên layout trong `_ViewStart` | Build qua, nổ khi mở trang |
| `Task.WhenAll` nhiều `await` dùng chung `DbContext` | "A second operation was started" |
| Factory chọn nhầm kho giỏ hàng | Giỏ vẫn chạy, chỉ là mất dữ liệu |
| Thiếu **`ParseLimitsInInvariantCulture`** ở `[Range]` tiền | Máy vi-VN **ném ArgumentException** → HTTP 500 (`ConvertValueInInvariantCulture` KHÔNG đủ) |
| Đặt tên input list bằng `foreach` hoặc dùng Id làm chỉ số | Binder dừng ở chỗ đứt quãng, âm thầm bỏ hết dòng còn lại |
| `disabled` ô nhập của dòng đang hỏng trong bảng sửa hàng loạt | Trình duyệt KHÔNG gửi input disabled → chỉ số đứt quãng → binder bỏ mọi dòng sau, mà response vẫn báo "đã lưu". Dùng `readonly` |
| Đổi tên một `data-*` mà JS đang `querySelector` | `null` → JS ngừng chạy, không lỗi nào |
| Bọc node JS cần cập nhật trong `@if` | Lần đầu cần cập nhật thì không có node |
| Xét nhánh PartialView trước nhánh JSON | Client nhận HTML, `response.json()` ném |
| `Stock -= n` mà cột không phải concurrency token | **Oversell** — đã đo: bán 10 khi có 5 |
| Chạm nhiều dòng không theo thứ tự cố định | Deadlock, chỉ xuất hiện dưới tải |
| Nhận `userId` từ form thay vì `ICurrentUser` | IDOR: đặt đơn / đọc đơn dưới tên người khác |
| Trả **403** thay vì **404** cho đơn của người khác | 403 xác nhận "đơn số 42 có tồn tại", mà Id tuần tự nên đoán được |
| `Include(o => o.Items)` rồi `Sum` trong C# ở trang danh sách | Màn hình hiện **đúng** con số, chỉ là kéo về mọi dòng đơn của cả trang — chỉ test đếm lệnh SQL bắt được |
| Quên `builder.Ignore()` cho property tính toán | Migration sinh cột không bao giờ được ghi |
| Đọc lại `product.Price` khi tính tổng đơn | Tổng đơn lệch khỏi tổng các dòng |
| `type="number"` cho ô số điện thoại | Mất số `0` đứng đầu, chặn luôn dấu `+` |
| Quên nạp lại property `[BindNever]` khi render lại form | Trang hiện ra rỗng, không lỗi nào |
| Địa chỉ giao hàng trỏ FK sang bảng `Addresses` | Sửa sổ địa chỉ là đơn CŨ đổi theo |
| Thêm `VnPay:HashSecret` vào `appsettings.json` | App chạy **tốt hơn**, test xanh, bí mật vào Git vĩnh viễn |
| Bind Options mà quên `.ValidateOnStart()` | Options tạo lười → cấu hình sai lộ ra ở request thanh toán đầu tiên |
| Ghép chuỗi-để-ký và query string bằng **hai** đoạn code | `%20` vs `+` → VNPay từ chối, log hai bên trông y hệt nhau |
| `vnp_Amount` để `decimal` thay vì ép `long` | Máy vi-VN in `125000000,00` — máy dev en-US không tái hiện |
| `vnp_CreateDate` gửi giờ UTC | Lệch 7 tiếng → VNPay coi lệnh đã hết hạn |
| Ghi nhận thanh toán ở **Return URL** thay vì IPN | Khách đóng tab = tiền đã trừ mà đơn vĩnh viễn "chưa trả" |
| Đọc `vnp_ResponseCode` trước khi kiểm `vnp_SecureHash` | Tin dữ liệu chưa xác thực — ai cũng tự gõ được `?vnp_ResponseCode=00` |
| Bỏ đối chiếu `vnp_Amount` vì "chữ ký đã hợp lệ rồi" | Đơn 10 triệu được đánh dấu đã trả bằng 10 nghìn |
| Trả mã lỗi IPN cho một giao dịch **thất bại** | VNPay tưởng ta chưa nhận được → gửi lại mãi |
| `defaultValue: ""` mà EF sinh cho cột enum-as-string | Dòng cũ mang giá trị không hợp lệ, nổ lúc đọc lên |
| `ExecuteUpdate` cho đường ghi có `RowVersion` | Nó KHÔNG đi qua Change Tracker nên **không tự kẹp** token vào `WHERE` — Optimistic Concurrency biến mất, build sạch |
| Dapper multi-exec để cập nhật nhiều dòng | Chỉ trả **tổng** số dòng — biết có dòng hỏng mà không biết dòng nào, nên không nêu được tên sản phẩm |
| Quên `using` cho `SqlConnection` tự tạo (Dapper) | Connection không về pool → cạn pool → **`Timeout expired`** ở một truy vấn chẳng liên quan, và **chỉ dưới tải** |
| `using` cho connection **MƯỢN** từ `_context.Database.GetDbConnection()` | Đóng connection của chính `DbContext` giữa chừng → mọi lệnh EF sau đó đổ, transaction đang mở bị huỷ |
| Ghép dòng form với entity theo **vị trí** thay vì theo `Id` | Giá của sản phẩm này rơi vào sản phẩm khác; cả hai vẫn là số hợp lệ |
| Ở nhánh "bỏ qua dòng xung đột" mà vẫn gán `Price`/`Stock` trước khi `continue` | EF vẫn sinh UPDATE → khớp 0 dòng → **cả batch revert**, trong khi thông báo vẫn nói "đã lưu 1 sản phẩm" |
| Sau khi lưu THÀNH CÔNG MỘT PHẦN, chỉ nạp `RowVersion` mới cho dòng vướng | Lần Lưu sau báo xung đột ở đúng những dòng người dùng vừa ghi thành công |
| Hai dòng cùng `Id` trong một lần cập nhật hàng loạt | Identity map cho ra MỘT object → dòng sau đè dòng trước, báo "đã cập nhật 2 sản phẩm" |
| Server sửa giá trị của một tham số action (kẹp `page`) mà quên `ModelState.Remove` | `asp-for` render lại giá trị THÔ, và lần submit sau gửi lại đúng giá trị bậy đó |
| Helper đăng nhập trong test chấp nhận **200** | Đăng nhập THẤT BẠI cũng là 200 (render lại form) → test chạy tiếp không cookie, đỏ ở chỗ chẳng liên quan |
| `max="@Model.Stock"` cho ô nhập số lượng ở trang chi tiết | In bảng tồn kho của **toàn shop** vào HTML ở mọi lượt xem. Trần đúng là `100` (trần của `AddToCartRequest`); giới hạn thật do `CartService` kẹp |
| Bọc **cả thẻ sản phẩm** trong `<a>` | `<form>` lồng trong `<a>` là HTML không hợp lệ — trình duyệt tự sửa cây DOM và nút "Thêm vào giỏ" hỏng theo kiểu khó đoán |
| Regex non-greedy `<a…>(.*?)</a>` để kiểm thẻ lồng nhau | Khớp tới `</a>` **đầu tiên** nên không bao giờ thấy `<form>` bên trong — test xanh mà không chứng minh gì. Regex không đếm được cấu trúc lồng; phải đếm độ sâu |
| Test bóc `value="…"` từ HTML mà **không `HtmlDecode`** | Base64 chứa `+` bị render thành `&#x2B;` → POST lại không giải mã được → RowVersion null → **không xung đột nào bị phát hiện**; đỏ NGẪU NHIÊN, trông như flaky hạ tầng |

## Môi trường
- .NET SDK 10, SQL Server 2025 Express, instance `SQLEXPRESS`.
- Connection string ở `appsettings.json`, dùng `Trusted_Connection=True` nên không có
  mật khẩu. Nếu chuyển sang SQL Authentication thì BẮT BUỘC dùng User Secrets.
- **Bí mật VNPay nằm ở User Secrets**, không ở `appsettings.json`. Máy mới phải chạy:
  `dotnet user-secrets set "VnPay:TmnCode" "<ma>" --project MiniMart.Web` và tương tự cho
  `VnPay:HashSecret`. Thiếu là ứng dụng **từ chối khởi động** (`ValidateOnStart`) và mọi
  integration test đỏ. Xem `.claude/rules/build.md`.
- `dotnet-ef` cài global.
- sqlcmd để kiểm tra DB trực tiếp:
  `"C:\Program Files\Microsoft SQL Server\Client SDK\ODBC\180\Tools\Binn\SQLCMD.EXE" -S "localhost\SQLEXPRESS" -E -d MiniMart -C -Q "<sql>"`

## Lệnh hay dùng
- dotnet build
- dotnet test
- dotnet format --verify-no-changes   ← là một dạng test, phải sạch
- dotnet run --project MiniMart.Web --launch-profile http
- ./scripts/test-vnpay-ipn.ps1 -OrderId &lt;id&gt; -Reset   ← giả lập VNPay gọi IPN vào app đang chạy
- dotnet run --project scripts/SeedDuLieuMau -- --xac-nhan   ← **XOÁ SẠCH** rồi nạp lại dữ liệu mẫu
- pwsh MiniMart.Tests/bin/Debug/net10.0/playwright.ps1 install chromium   ← **máy mới chạy MỘT lần**
- dotnet ef migrations add <TenMigration> -p MiniMart.Infrastructure -s MiniMart.Web
- dotnet ef database update -p MiniMart.Infrastructure -s MiniMart.Web

`SeedDuLieuMau` giữ lại MỌI tài khoản `Role = Admin` và xoá phần còn lại, rồi nạp
5 danh mục × 10 sản phẩm, 10 khách (`khach01`…`khach10` / `Khach@2026`), mỗi khách
2–3 đơn, mỗi đơn 1–5 món. Thiếu `--xac-nhan` thì nó chỉ IN RA kế hoạch kèm chuỗi kết
nối rồi thoát — chốt này có vì sai lầm đắt nhất của script loại này không phải chạy
nhầm lúc mà là chạy đúng lúc trên **nhầm database**. Hạt ngẫu nhiên cố định nên chạy
lại cho ra đúng bộ dữ liệu cũ. Nó nằm TRONG solution có chủ đích: script chạm mọi
entity nên việc CI build nó là một phép kiểm nhất quán miễn phí — đổi tên một property
mà quên script là lỗi build, không phải một script hỏng âm thầm.

## Lưu ý đặc biệt
- Products.RowVersion dùng cho Optimistic Concurrency, không được xóa/sửa kiểu dữ liệu này khi generate code.
- Xoá Category dùng `DeleteBehavior.Restrict`, KHÔNG đổi sang Cascade.
- `CartItems → Products` dùng `Cascade` — cố ý KHÁC Category, đừng "sửa cho nhất quán".
- Cột tiền tệ luôn là `decimal` với `HasPrecision(18, 2)`, không dùng double/float.

## Nợ kỹ thuật đã biết (cố ý chưa làm, đừng "sửa" nửa vời)

### Hoãn có chủ đích — KHÔNG làm cho tới khi điều kiện đủ
- `Cart`/`CartItem` **chưa có `RowVersion`**, và chưa cần: giỏ hàng là dữ liệu của MỘT
  người, không có hai người cùng sửa một giỏ. Trường hợp duy nhất là một người mở hai tab,
  và ở đó "ghi sau thắng" đúng ý muốn của họ. Việc cần khoá thật là **trừ tồn kho lúc đặt
  hàng** — nhưng khoá trên `Products.RowVersion` (đã có), không phải trên giỏ.
- Nhánh **`PartialView` của ba endpoint ghi giỏ hàng** (`X-Requested-With` mà không kèm
  `Accept: application/json`) hiện **không có client nào dùng** — dropdown dùng nhánh JSON,
  trang `/Cart` dùng nhánh PRG. Giữ lại vì nó là đường tự nhiên nếu sau này trang `/Cart`
  cần cập nhật cả bảng bằng AJAX, và nó có test. Đừng xoá mà cũng đừng tưởng nó đang chạy.

### Nợ thật, chưa trả
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
- **Địa chỉ giao hàng phải gõ lại mỗi lần đặt.** Chưa có sổ địa chỉ (chọn nhanh từ các lần
  trước), và cũng chưa tự điền từ đơn gần nhất. Cố ý: `Order` đã snapshot đủ 3 cột nên thêm
  sổ địa chỉ về sau không phải sửa gì ở luồng đặt hàng — đúng tinh thần hoãn tới lúc cần.
- **VNPay đã đủ luồng đầu-cuối** (nút Checkout → ký URL → cổng → Return → IPN → `Paid`),
  nhưng **chưa chạy thật được**: khoá sandbox trong User Secrets vẫn là placeholder, và
  VNPay không gọi IPN tới `localhost` được (cần ngrok). Còn thiếu: **thanh toán lại**
  (bị chặn bởi việc `vnp_TxnRef = OrderId` mà VNPay từ chối `TxnRef` đã dùng), **đối
  soát định kỳ** khi IPN mất hẳn, và `Order` **chưa lưu phương thức thanh toán**. Xem
  `.claude/rules/payments.md`.
- ~~Bộ test dùng chung một hạn mức rate limit~~ → **KHÔNG CÓ THẬT, đã đo và bác bỏ.**
  Mục này từng ghi rằng vì `RemoteIpAddress` luôn `null` trong `WebApplicationFactory`
  nên cả bộ test chung một hạn mức. Sai: mỗi test class tạo `WebApplicationFactory`
  riêng → **host riêng, DI container riêng, limiter riêng**, nên bảng partition cũng
  tách rời — việc partition key đều là `"unknown"` không nối chúng lại với nhau. Đo
  bằng hai factory cùng hạ hạn mức xuống 2: đốt sạch hạn mức của A rồi mới gọi B, kết
  quả `A = [200,200,429,429]` còn `B = [200,200]`. Thêm nữa, `EnvironmentName` trong
  test là `Development` nên hạn mức thật là **1000/phút** — một class phải đăng nhập
  1000 lần trong một phút mới chạm tới. Những lần đỏ ngẫu nhiên đã bị quy sai cho nó;
  nguyên nhân thật là bug Base64 `+`/`HtmlDecode` (xem `rules/testing.md`).
  📌 Bài học quy trình: một món "nợ kỹ thuật" chưa từng được ĐO cũng là một giả thuyết,
  và giả thuyết viết vào tài liệu thì đọc y hệt sự thật.
- **Sửa hàng loạt vẫn all-or-nothing ở đúng MỘT trường hợp hiếm**: có người ghi vào
  khoảng vài mili giây giữa lệnh đọc và `SaveChanges`. Xung đột thông thường đã bỏ qua
  chọn lọc từng dòng. Chưa tự thử lại vì retry sạch đòi một `DbContext` mới — cùng lý do
  đã hoãn retry cho đua tạo giỏ hàng lần đầu. Xem `.claude/rules/concurrency.md`.
- **Helper đăng nhập trong test bị chép 4 bản** (`AdminCrudTests`, `ProductBulkEditPageTests`,
  `ProductBulkUpdateTests`, `ProductConcurrencyTests`). Ngưỡng gộp của dự án là bản thứ ba
  nên nó đã quá hạn. Ba bản cũ còn chấp nhận **200** là đăng nhập thành công — mà 200 chính
  là đăng nhập THẤT BẠI; chỉ bản trong `ProductBulkUpdateTests` đã siết thành đúng 302.
- **Trang "Đơn hàng của tôi" chưa có nút thanh toán lại.** Đơn `Pending` hiện chỉ nói rõ
  là chưa ghi nhận thanh toán, không có đường trả tiền — bị chặn bởi `vnp_TxnRef = OrderId`.
  Cố ý không hiện nút bấm vào ra lỗi.
- **`Order` chưa có `Status`** — cố ý. Thêm cột trạng thái trước khi biết đơn có những trạng
  thái nào là đoán, cùng lý do đã hoãn API transaction cho tới đúng lúc cần.

### Đã trả (giữ lại để không ai "sửa" ngược)
- ~~Hai file JS không có test chạy thật~~ → đã làm ở Phase 11 bằng **Playwright (bản .NET)**.
  16 test chạy trong Chromium thật, cùng một lệnh `dotnet test`. Máy mới phải chạy một lần:
  `pwsh MiniMart.Tests/bin/Debug/net10.0/playwright.ps1 install chromium`.
  📌 Ngay lần chạy đầu tiên nó tìm ra **hai lỗi thật mà 591 test cũ không thấy** — xem hai
  dòng đầu bảng "bẫy im lặng". Đó là bằng chứng chạy thật cho lý do phase này đáng làm.
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
- ~~Trang "Đơn hàng của tôi"~~ → đã làm ở Phase 9. Danh sách dùng read model riêng
  (`OrderSummary`) chiếu ngay trong truy vấn; đừng "đơn giản hoá" thành `Include(o => o.Items)`,
  xem `.claude/rules/data-access.md`.
- ~~Trang chi tiết sản phẩm~~ → đã làm ở Phase 10. `/Product/Details/{id}` giữ route MẶC
  ĐỊNH; đừng "làm đẹp URL" thành `/Product/5` bằng attribute route — nó tắt route mặc định
  cho chính action đó và làm dự án có hai kiểu URL. Xem `.claude/rules/web.md`.
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
