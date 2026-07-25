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

## Quy ước code
- Controller KHÔNG chứa business logic, chỉ gọi Service.
- IRepository đặt ở Domain (Domain khai báo thứ nó CẦN từ tầng lưu trữ).
- IService đặt ở Application, cùng tầng với implementation của nó. Domain giữ thuần nghiệp vụ.
- Service phụ thuộc IRepository (Domain), không phụ thuộc trực tiếp Infrastructure (DIP).
- Đặt tên: IProductRepository / ProductRepository, IProductService / ProductService.
- Toàn bộ thao tác DB dùng async/await, mọi method public nhận CancellationToken.

### Validation — đặt ở đâu
Tiêu chí phân loại, không phải cảm tính:
- **Data Annotation** cho ràng buộc thuộc về BẢN THÂN giá trị: bắt buộc nhập, độ dài,
  khoảng số, định dạng. Đánh giá được mà không cần biết gì ngoài chính giá trị đó.
- **Service** cho mọi ràng buộc cần một trong ba thứ: truy vấn DB, `async`, hoặc
  dependency từ DI. Lý do `ValidationAttribute.IsValid` có chữ ký ĐỒNG BỘ — query DB
  trong đó buộc phải `.GetAwaiter().GetResult()`, gây thread-pool starvation.
- Ví dụ đã áp dụng: `CategoryId` có `[Range(1, ...)]` để hỏi "đã chọn chưa", còn
  "danh mục đó có tồn tại không" nằm ở `ProductService.BaoDamDanhMucTonTaiAsync`.
- Cột tiền tệ: dùng `[Range(typeof(decimal), "0.01", "...", ConvertValueInInvariantCulture = true)]`.
  Overload `Range(0.01, ...)` nhận `double` nên làm tròn nhị phân; thiếu
  `ConvertValueInInvariantCulture` thì máy locale vi-VN parse `"0.01"` sai.

### Unit of Work
- `SaveChangesAsync` nằm ở **IUnitOfWork**, TUYỆT ĐỐI không thêm lại vào Repository.
  Lý do: mọi Repository dùng chung một DbContext (Scoped), nên SaveChanges trên
  một Repository thực chất commit thay đổi của tất cả — tên gọi sẽ nói dối.
- Repository chỉ `AddAsync` / `Remove` / truy vấn. Service gọi `_unitOfWork.SaveChangesAsync()`.

### Đặt tên method truy vấn trong Repository
- `GetByIdAsync` → `AsNoTracking`, dùng cho đường **đọc** (hiển thị).
- `GetForUpdateAsync` → **có tracking**, bắt buộc dùng cho đường **sửa**.
  Entity AsNoTracking sửa xong gọi SaveChanges sẽ không lưu gì, và mất luôn
  RowVersion gốc nên không phát hiện được xung đột.
- Truy vấn trả về entity có navigation property phải `Include` đầy đủ.

### Truy vấn có lọc và phân trang
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

### Validate nghiệp vụ — luôn làm ở HAI nơi
Quy tắc chung của dự án: **validate ở Service để có thông báo tử tế, ràng buộc ở DB để có sự thật.**
- Kiểm tra ở Service luôn có khe TOCTOU (giữa lúc kiểm tra và lúc lưu).
- Vì vậy mọi quy tắc quan trọng phải có ràng buộc DB đi kèm: unique index,
  check constraint, hoặc foreign key.
- Ví dụ đã áp dụng: Username (unique index), Stock/Price >= 0 (check constraint),
  không xoá Category còn Product (FK Restrict).

### Dịch exception theo tầng
- Infrastructure biết EF Core và mã lỗi SQL Server, dịch sang exception CHUNG
  (`DuplicateKeyException`) — đặt trong `MiniMart.Common/Exceptions`.
- Application diễn giải exception chung đó thành exception NGHIỆP VỤ theo ngữ cảnh
  (`UsernameAlreadyExistsException`, `CategoryNameAlreadyExistsException`).
- Application và Web KHÔNG được `using Microsoft.EntityFrameworkCore`.
- `DbUpdateConcurrencyException` cố ý không bị bọc lại — mỗi nghiệp vụ xử lý riêng.
- `NotFoundException` mang theo `EntityName`, và Controller PHẢI dùng nó để phân biệt:
  thiếu chính đối tượng đang thao tác → `NotFound()`; thiếu đối tượng được tham chiếu
  → `AddModelError` vào đúng ô nhập rồi render lại form.

### Đăng ký DI
- Không viết `AddScoped` rời rạc trong `Program.cs`. Dùng extension method
  `AddApplication()` (Application) và `AddInfrastructure(configuration)` (Infrastructure).
- Repository / Service / UnitOfWork: **Scoped** (đồng bộ với DbContext).
- Chỉ dùng Singleton cho thứ không giữ state và không phụ thuộc gì Scoped
  (ví dụ `IPasswordHasher<User>`), tránh captive dependency.

## Quy ước tầng Web
- Form dùng **ViewModel riêng**, không bind thẳng Entity (chống over-posting).
- Mọi POST có `[ValidateAntiForgeryToken]`. Xoá phải là POST, không dùng GET.
- Sau POST thành công luôn `RedirectToAction` (Post-Redirect-Get); thông báo qua `TempData`.
- Khi `ModelState` không hợp lệ và render lại form: **nạp lại dropdown/SelectList**,
  vì `<select>` không gửi danh sách lựa chọn lên server.
- Controller bắt exception nghiệp vụ và chuyển thành `ModelState.AddModelError`
  hoặc `TempData`, không tự quyết định nghiệp vụ.
- Area Admin: controller đặt tên `ProductController`, KHÔNG phải `AdminProductController`
  (Area đã cung cấp tiền tố `/Admin/`). Route area phải đăng ký trước route default.

### Razor view và form lọc
- Razor view **được phép nhận thẳng entity**, không bắt buộc DTO. Ba lý do dùng DTO
  cho JSON đều không áp dụng: không serialize nên không có vòng lặp; chỉ trường nào
  được `@` mới ra HTML; Razor được biên dịch nên đổi tên property là lỗi build.
  Đừng áp dụng máy móc "lúc nào cũng phải DTO".
- Form lọc/tìm kiếm dùng **GET**, không POST: kết quả nằm trong URL nên bookmark,
  chia sẻ link và bấm Back/Refresh đều hoạt động.
- Sau khi lọc phải giữ `selected` trong dropdown, nếu không người dùng mất dấu
  mình đang xem gì.
- Hiển thị **trạng thái** còn/hết hàng, KHÔNG hiện con số tồn kho ra HTML.
- Nhiều lệnh `await` trong cùng một action phải **nối tiếp**, không gói vào
  `Task.WhenAll`: chúng dùng chung một `DbContext` (Scoped), mà `DbContext` không
  thread-safe → "A second operation was started on this context instance".
- Partial dùng lại được đặt ở `Views/Shared` (VD `_ProductCard.cshtml`).
- Ô nhập số (giá, số lượng) trong form lọc dùng `type="number"`, KHÔNG dùng `type="text"`.
  Theo chuẩn HTML, `input type="number"` luôn gửi giá trị ở dạng chuẩn hoá (dấu `.`
  thập phân) bất kể locale trình duyệt. Với `type="text"`, người dùng vi-VN gõ
  `1.000.000` thì model binding trả `null` **trong im lặng** (query string được bind
  bằng `InvariantCulture`) — không có lỗi nào báo, chỉ là bộ lọc không có tác dụng.
- Giữ lại giá trị đã nhập cho **mọi** ô lọc, không chỉ dropdown. Điều kiện hiện link
  "Xoá lọc" phải xét tất cả filter (`HasAnyFilter`), nếu chỉ xét một cái thì người lọc
  bằng cái còn lại không có đường quay về.
- Bộ lọc "vô lý nhưng hợp lệ" (VD `minPrice > maxPrice`) phải **cảnh báo**, không im
  lặng trả về rỗng: SQL đúng nhưng người dùng sẽ tưởng shop hết hàng.

### Định dạng số ở tầng hiển thị
- Dùng `MoneyFormat.ToMoneyText()` (`MiniMart.Web/Extensions`) chứ không gọi trực tiếp
  `ToString("N0")`. Helper khoá vào `InvariantCulture` để cùng một số ra cùng một chuỗi
  bất kể locale của máy chạy.
- Lý do cứng: ASP.NET Core **không** set `CurrentCulture` theo request nếu chưa thêm
  Request Localization, nên nó bằng locale của OS. Máy dev en-US in `111,000`, máy triển
  khai vi-VN in `111.000` — cùng một dòng code, hai kết quả, và test nào assert trên
  chuỗi giá sẽ đỏ khi đổi máy.
- Cảnh báo nếu sau này thêm `CultureInfo.DefaultThreadCurrentCulture = "vi-VN"`: form
  POST được bind bằng `CurrentCulture`, nên form Admin sẽ parse `1000.50` thành `100050`.

### Request AJAX bổ sung dữ liệu vào trang đang mở: PartialView, KHÔNG phải JSON
Quy tắc mặc định của dự án: request AJAX mà kết quả **chỉ để hiển thị** thì trả
`PartialView()`. Đã cân nhắc và loại `Json()` cho `/Product/LoadMore` (xem nợ kỹ thuật).
- Lý do quyết định: markup thẻ sản phẩm chỉ được định nghĩa **một lần** ở
  `_ProductCard.cshtml`. Trả JSON thì client phải dựng lại markup bằng JavaScript, tức
  viết lần thứ hai cùng một giao diện + cùng cách định dạng tiền + cùng logic badge
  còn/hết hàng, và phải tự escape XSS bằng tay vì **JSON không escape ký tự `<`**.
- Lý do quan trọng không kém: HTML server render **test được bằng integration test**,
  còn hàm dựng DOM trong JavaScript thì không có test nào chạm tới.
- Chỉ chọn `Json()` khi thật sự có **client không phải trình duyệt** (mobile app), hoặc
  client cần dữ liệu để tính toán chứ không phải để hiển thị.
- KHÔNG dùng `View()` cho loại request này — nó gửi lại cả layout. Mỗi endpoint trả
  partial phải có test khẳng định response **không** chứa `<!DOCTYPE`/`<html`/`navbar`;
  `PartialView` → `View` là lỗi build-được-nhưng-sai, chỉ test mới bắt.
- Partial trả về nhiều item: tạo partial bao (`_ProductCards.cshtml`) chỉ làm việc lặp,
  và **không bọc thêm thẻ nào**. Output phải là dãy `.col` dán thẳng được vào `.row`;
  thêm một tầng `div` là `.col` không còn con trực tiếp của `.row` → vỡ Bootstrap grid.
- Metadata phân trang đi qua **HTTP response header** (`X-Next-Page`), vì body là HTML
  nên không có chỗ đặt. Hết dữ liệu → header rỗng. Đặt tên header thành `const` trong
  Controller để test tham chiếu cùng một hằng, không gõ lại chuỗi.
- Endpoint phân trang phải nhận **đúng bộ tham số lọc** như action render trang 1.
  Thiếu một tham số là bấm "Xem thêm" xong nhận về sản phẩm đã bị loại ở trang 1.

### Nếu về sau cần endpoint JSON thật
- BẮT BUỘC dùng DTO riêng, TUYỆT ĐỐI không trả thẳng entity. Lý do cứng:
  `Product.Category` ↔ `Category.Products` tạo vòng lặp tham chiếu, `System.Text.Json`
  ném `JsonException: A possible object cycle was detected` → HTTP 500. Lỗi này CÓ ĐIỀU
  KIỆN (chỉ xảy ra khi navigation được nạp), nên một `Include` thêm vào sau này có thể
  làm sập endpoint đang chạy tốt.
- Hai lý do còn lại: không lộ dữ liệu nội bộ (`RowVersion`, `Stock`), và hợp đồng JSON
  không dính vào tên property của entity.
- Làm phẳng navigation property (`CategoryName` thay vì `Category`) và chuyển thông tin
  nghiệp vụ thành trạng thái (`InStock: bool` thay vì `Stock: int`) — DTO cũng là một
  bề mặt lộ dữ liệu như HTML.
- Đặt DTO ở `MiniMart.Web/Models`. `System.Text.Json` tự đổi sang camelCase.
- Response phân trang phải kèm `HasNextPage`, nếu không client cuộn vô tận không biết dừng.

### JavaScript gọi endpoint
- Chèn HTML từ server bằng `insertAdjacentHTML('beforeend', html)`. An toàn vì Razor đã
  escape ở server, và `insertAdjacentHTML` **không** thực thi thẻ `<script>` được chèn.
- Nếu (và chỉ nếu) phải dựng DOM từ JSON: dữ liệu người dùng nhập BẮT BUỘC đi qua
  `textContent`, chỉ dùng `innerHTML` cho khung HTML tĩnh. Razor tự escape
  `@Model.Name`, JavaScript thì không.
- Bộ lọc hiện tại truyền cho JS qua `data-*` **trên nút**, không đọc lại từ các ô input:
  người dùng có thể sửa ô lọc mà chưa bấm "Lọc", lúc đó ô nhập và danh sách đang hiển
  thị đã lệch nhau — phải phân trang theo bộ lọc ĐANG hiển thị.
- Client bỏ hẳn tham số không có giá trị khỏi query string (`if (value) params.set(...)`),
  đúng tinh thần `if (x.HasValue) query = query.Where(...)` ở Repository.
- `fetch` **không** reject khi server trả 4xx/5xx (chỉ reject khi lỗi mạng) → luôn kiểm
  tra `response.ok` trước khi đọc body. Bỏ qua bước này là dán HTML trang lỗi vào DOM.
- Có cờ chặn double-click: `fetch` là bất đồng bộ nên bấm hai lần nhanh sẽ thêm cùng
  một trang vào DOM hai lần.
- Server là nơi duy nhất biết còn trang sau hay không — client đọc header, không tự suy
  ra từ số item nhận được (`items.length < pageSize` sai khi trang cuối vừa đủ đầy).

### Layout
- Khu vực quản trị dùng `Areas/Admin/Views/Shared/_AdminLayout.cshtml`, khai báo trong
  `Areas/Admin/Views/_ViewStart.cshtml`. Trang khách hàng giữ `Views/Shared/_Layout.cshtml`.
- Partial dùng chung cho cả hai (VD `_StatusMessages`) đặt ở `Views/Shared` — view trong
  Area vẫn tìm thấy nhờ cơ chế fallback, không cần chép đôi.
- Layout được phân giải lúc **chạy**, không phải lúc biên dịch: sai tên trong `_ViewStart`
  thì `dotnet build` vẫn qua và chỉ nổ khi mở trang. Vì vậy mỗi layout phải có
  integration test kiểm chứng đúng layout được dùng.

### Upload file
- **Tên file luôn do server sinh** (`Guid.NewGuid()` + phần mở rộng). TUYỆT ĐỐI không
  dùng `file.FileName` làm tên lưu: mở đường cho path traversal và ghi đè lẫn nhau.
- Whitelist phần mở rộng (`.jpg .jpeg .png .webp`) và giới hạn dung lượng bằng
  **Data Annotation** — đây là ràng buộc tĩnh nên annotation dùng đúng chỗ.
- DB chỉ lưu **đường dẫn tương đối**; file nằm trong `wwwroot`. Thư mục upload
  đã được `.gitignore`.
- Form có `input type="file"` BẮT BUỘC có `enctype="multipart/form-data"`.
  Thiếu nó thì `IFormFile` luôn null và không có lỗi nào báo.
- Trong luồng sửa, `imageUrl = null` nghĩa là **giữ ảnh cũ**, không phải xoá ảnh.
- Thứ tự ghi/xoá: lưu file TRƯỚC khi gọi Service (lỗi thì chỉ dư file rác),
  nhưng xoá file SAU khi DB xoá xong (lỗi thì bản ghi vẫn còn ảnh).
- Service lưu trữ file thuộc tầng Web (phụ thuộc `IWebHostEnvironment`), đăng ký
  trực tiếp trong `Program.cs`, không nằm trong `AddApplication`/`AddInfrastructure`.

## Quy ước Authentication / Authorization
- Cookie Authentication, scheme mặc định là `CookieAuthenticationDefaults.AuthenticationScheme`.
- `app.UseAuthentication()` BẮT BUỘC đứng trước `app.UseAuthorization()`.
- Claims dùng đúng `ClaimTypes.NameIdentifier` / `ClaimTypes.Name` / `ClaimTypes.Role`.
  Dùng chuỗi tự chế (`"role"`) sẽ khiến `IsInRole` và `[Authorize(Roles=...)]` im lặng trả false.
- `new ClaimsIdentity(claims, authenticationType)` — thiếu tham số thứ hai thì
  `IsAuthenticated` = false dù có đủ claims.
- Claims là **ảnh chụp lúc đăng nhập**. Đổi Role dưới DB không có hiệu lực cho đến
  khi người dùng đăng nhập lại. `SlidingExpiration` gia hạn cookie nhưng KHÔNG làm mới claims.
- Đặt `[Authorize]` ở cấp class để action thêm sau tự động được bảo vệ.
- Ẩn link trên menu chỉ là trải nghiệm người dùng, không phải bảo mật.

## Quy ước test
- Nghiệp vụ (Service) → **unit test với Moq**, không cần DB. Đây là lợi ích chính của DIP.
- Hành vi của database engine → **integration test trên SQL Server thật**:
  rowversion, check constraint, foreign key, unique index.
- **KHÔNG dùng EF Core InMemory** cho concurrency: nó không thực thi concurrency token
  nên test luôn xanh kể cả khi code có bug.
- Cấu hình model → đọc metadata (`context.Model`). Riêng check constraint phải đọc từ
  `context.GetService<IDesignTimeModel>().Model` vì model runtime đã lược bỏ.
- Integration test tự dọn dữ liệu mình tạo ra (`IAsyncLifetime.DisposeAsync`), dùng
  tên có `Guid` để không đụng nhau.
- **Mutation test bắt buộc**: sau khi viết test, cố tình phá code để xác nhận test đỏ.
  Test xanh chưa chứng minh được gì.
- Tên test viết bằng tiếng Việt không dấu, mô tả hành vi mong đợi.
- **Không assert trên chuỗi số ngắn** (`DoesNotContain("77")`) khi dữ liệu test sinh
  ngẫu nhiên: GUID dạng hex có chứa chữ số nên assertion sẽ đỏ ngẫu nhiên. Dùng giá trị
  đủ đặc biệt (7+ chữ số) hoặc assert theo cấu trúc thay vì chuỗi trần.

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
- dotnet run --project MiniMart.Web --launch-profile http
- dotnet ef migrations add <TenMigration> -p MiniMart.Infrastructure -s MiniMart.Web
- dotnet ef database update -p MiniMart.Infrastructure -s MiniMart.Web

## Lưu ý đặc biệt
- Products.RowVersion dùng cho Optimistic Concurrency, không được xóa/sửa kiểu dữ liệu này khi generate code.
- Xoá Category dùng `DeleteBehavior.Restrict`, KHÔNG đổi sang Cascade.
- Cột tiền tệ luôn là `decimal` với `HasPrecision(18, 2)`, không dùng double/float.

## Nợ kỹ thuật đã biết (cố ý chưa làm, đừng "sửa" nửa vời)
- **Luồng Edit sản phẩm chưa round-trip RowVersion** qua hidden field, nên xung đột
  trong lúc người dùng đang mở form KHÔNG được phát hiện. Thuộc phase Concurrency.
- `IUnitOfWork` chưa có API transaction; sẽ thiết kế khi làm nghiệp vụ đặt hàng.
- Auth còn 3 lỗ hổng: timing attack ở `AuthenticateAsync` (không hash khi user không
  tồn tại), chưa rate limit đăng nhập, mật khẩu chỉ yêu cầu tối thiểu 6 ký tự.
- Chưa có `Directory.Packages.props` (quản lý version package tập trung), `.editorconfig`, CI.
- **`wwwroot/js/home-load-more.js` KHÔNG có test tự động** — dự án chưa có headless
  browser (Playwright). Kiểm chứng được: HTML mà `/Product/LoadMore` trả về (integration
  test — đây là phần lớn giá trị của việc chọn PartialView), header `X-Next-Page`
  (integration test), `data-*` trên nút (integration test), cú pháp JS (`node --check`).
  Chưa kiểm chứng được: `insertAdjacentHTML` chạy thật, cập nhật `shownCount`, xoá nút
  khi hết dữ liệu, chặn double-click. Phải bấm tay.
- `minPrice`/`maxPrice` chưa được validate ở Service (giá âm, min > max đều đi qua).
  Trang chủ **cảnh báo** min > max ở view; `/Product/LoadMore` thì trả rỗng, không cảnh báo.
- `/Product/LoadMore` không có action `Index` đi kèm — `/Product` hiện trả 404. Trang chi
  tiết sản phẩm chưa làm.

## Cách làm việc với tôi (người học)
- Tôi đang học song song, nên MỌI đoạn code Claude Code viết ra đều phải kèm:
  1. Giải thích ngắn gọn: đoạn này làm gì, tại sao chọn cách này.
  2. Chỉ rõ những điểm liên quan trực tiếp đến kiến thức tôi đang học (LINQ, SOLID, DI, EF Core, Auth...).
  3. Nếu có 2 cách làm khác nhau (VD: Optimistic vs Pessimistic locking), giải thích cả 2 và lý do chọn 1.
- Với các phần khó (Transaction, Concurrency, Bulk Update), giải thích Ý TƯỞNG trước khi viết code.
- Cuối mỗi Phase: cập nhật chính file này với pattern/quy ước mới phát sinh.
