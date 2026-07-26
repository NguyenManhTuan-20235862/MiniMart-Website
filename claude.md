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
- Bộ lọc dùng ở NHIỀU action phải gộp thành **một type** (`ProductFilter`), không để mỗi
  action khai báo tham số rời. `HomeController.Index` và `ProductController.LoadMore` bắt
  buộc nhận cùng bộ lọc; hai danh sách tham số giống nhau thì không có gì ngăn chúng lệch
  nhau khi thêm filter mới. Thêm property vào type là cả hai nhận được ngay.
- Input đặt `name` **thủ công** (`name="minPrice"`) thay vì `asp-for="Filter.MinPrice"`:
  `asp-for` sinh `name="Filter.MinPrice"` và URL thành `?Filter.MinPrice=1000` — dài, và
  lệch với tham số mà endpoint phân trang nhận. Đổi lại phải tự đổ `value` và tự hiện lỗi
  bằng `asp-validation-summary`.
- Ràng buộc liên quan HAI thuộc tính (`minPrice > maxPrice`) dùng `IValidatableObject`,
  không phải attribute trên một property. Nó chạy SAU khi từng property đã hợp lệ nên
  không phải kiểm tra null hay kiểu dữ liệu lại.
- Trang **xem hàng** vẫn truy vấn bình thường khi `ModelState` không hợp lệ (chỉ hiện cảnh
  báo), khác form **ghi dữ liệu** (chặn hẳn). Chặn ở trang xem hàng là người dùng chỉ thấy
  trang trống.
- **`HtmlEncoder.Create(UnicodeRanges.All)`** phải được đăng ký trong `Program.cs`. Mặc
  định escape mọi ký tự non-ASCII nên `"Khoảng giá"` do Razor sinh ra thành
  `"Kho&#x1EA3;ng gi&#xE1;"` — vẫn hiển thị đúng nhưng HTML phình to, không đọc được khi
  debug, và mọi test assert chuỗi tiếng Việt do `@` sinh ra sẽ đỏ. `UnicodeRanges.All`
  VẪN escape `< > & " '` nên không hở XSS.
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

## Quy ước Giỏ hàng (hai kho lưu trữ, một nghiệp vụ)

Khách vãng lai cũng phải mua được hàng, nên giỏ hàng có HAI nơi lưu: Session (chưa đăng
nhập) và bảng `Carts`/`CartItems` (đã đăng nhập). Nghiệp vụ chỉ viết MỘT lần.

### ICartStore — abstraction chia đôi nơi lưu trữ
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

### Factory chọn kho — câu `if (đã đăng nhập)` nằm ĐÚNG một chỗ
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

### Gộp giỏ khi đăng nhập — cái bẫy `SignInAsync`
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

### Chống IDOR bằng cấu trúc, không bằng câu `if`
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

### Schema giỏ hàng
- `UNIQUE(Carts.UserId)`: một người một giỏ. Có khe race (hai request đồng thời cùng tạo
  giỏ lần đầu) → `DuplicateKeyException`, đúng tinh thần "validate ở Service, ràng buộc ở DB".
- `UNIQUE(CartItems.CartId, ProductId)` — **hai cột**, không phải chỉ `ProductId`: cùng một
  sản phẩm ở hai giỏ khác nhau là hợp lệ. Có test riêng khoá điều này.
- `CHECK ([Quantity] > 0)`.
- `CartItems → Products` dùng **Cascade**, cố ý KHÁC `Category → Product` (Restrict). Lý do:
  xoá sản phẩm khỏi shop không được bị chặn chỉ vì có người để nó trong giỏ.
- `Cart`/`CartItem` **chưa có `RowVersion`** — có chủ đích, xem nợ kỹ thuật.

### Controller giỏ hàng
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

## Quy ước Concurrency (Optimistic, qua RowVersion)
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

### Chống dò và brute-force đăng nhập
- `AuthenticateAsync` khi username KHÔNG tồn tại vẫn phải **verify một hash giả**. Return
  null sớm khiến username sai trả lời ~1ms còn username đúng ~100ms (PBKDF2) — đo được
  qua mạng. Đã kiểm chứng: bỏ hash giả thì tỉ lệ thời gian là **37.9x**, có thì ~1x.
  Thông báo lỗi chung là chưa đủ; **thời gian** cũng là một kênh rò rỉ.
- Rate limit `POST /Account/Login` bằng `AddRateLimiter` (có sẵn trong framework, không
  cần package). Đặt trên **POST**, không đặt cấp class — xem trang đăng nhập là bình thường.
- `app.UseRateLimiter()` BẮT BUỘC đứng **sau** `app.UseRouting()`: policy khai báo bằng
  attribute, mà endpoint metadata chỉ có sau UseRouting. Đặt trước thì im lặng không giới
  hạn gì. Đã mutation test: response trả 400 thay vì 429.
- Phân vùng theo **IP**, không theo username: theo username thì kẻ tấn công đổi username
  mỗi lần thử là thoát giới hạn. Đánh đổi đã biết: nhiều người sau cùng NAT dùng chung hạn.
- `RejectionStatusCode = 429`. Mặc định của framework là 503 — sai nghĩa, server vẫn khoẻ.
- `QueueLimit = 0`: vượt hạn thì từ chối ngay. Xếp hàng là tự tạo chỗ cho DoS.
- Hạn mức đọc từ config (`RateLimiting:LoginPermitLimit`): 5 ở `appsettings.json`, 1000 ở
  `appsettings.Development.json` để integration test đăng nhập nhiều lần không bị chặn.
  Test riêng cho rate limit **tự ghi đè xuống 2** bằng `AddInMemoryCollection`, KHÔNG sửa file.
- Mật khẩu: tối thiểu 8 ký tự + phải có cả chữ và số. KHÔNG đòi ký tự đặc biệt — NIST
  SP 800-63B cho thấy quy tắc phức tạp đẩy người dùng về `Password1!`, dễ đoán hơn một
  passphrase dài. Trần 100 ký tự cố ý để rộng.

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
- Mô phỏng "hai người dùng" bằng **hai DI scope riêng**, không dùng chung một scope: mỗi
  scope có một `DbContext` với Change Tracker riêng. Dùng chung thì hai bên nhìn cùng một
  entity trong bộ nhớ và không xung đột nào xảy ra — test xanh vô nghĩa.
- Regex đọc HTML trong test **không được phụ thuộc thứ tự thuộc tính**. `asp-items` render
  `<option selected="selected" value="1">` (selected TRƯỚC value); regex
  `<option value="..." selected` trượt, trường về rỗng, ModelState invalid, và request
  không bao giờ tới nhánh đang cần test. Tìm thẻ trước, bóc thuộc tính sau — và thêm
  `Assert` cho chính helper để nó tự tố giác khi đọc sai.
- Test đo **thời gian** phải lấy trung vị nhiều lần và làm nóng trước (JIT, static `Lazy`),
  và assert theo **tỉ lệ trong một khoảng** chứ không so bằng: đo thời gian luôn nhiễu.
- **`dotnet format --verify-no-changes` là một dạng test.** Nó đã phát hiện field chết
  (`ProductQueryTests._categoryB`, `CartMergeTests._productHetHang`) mà `dotnet build`
  không hề cảnh báo, và cả file BOM/line-ending sai do script sinh ra.
- Hai implementation của cùng một interface → **test hợp đồng abstract** (base class chứa
  toàn bộ `[Fact]`, hai class con chỉ override cách tạo store). Viết hai bộ test rời là hai
  bộ sẽ lệch nhau, và không có gì khoá được tính thay thế lẫn nhau (Liskov).
- Test cần một `HttpContext` giả mà code đọc qua `IHttpContextAccessor`: **phải tự viết
  accessor giả** giữ context trong field thường. `HttpContextAccessor` thật giữ trong
  `AsyncLocal`, mà `AsyncLocal` **không chảy** từ `InitializeAsync` vào thân test — 13 test
  đã đỏ vì lý do này.
- Thứ tự các bước trong test là một phần của test. Ba test gộp giỏ lúc đầu "bỏ hàng vào giỏ
  rồi mới đăng ký", nhưng `Register` đã gộp và xoá giỏ Session nên lần `Login` sau đó chỉ
  gộp một giỏ **rỗng**: hai test **xanh mà không kiểm chứng gì**. Chỉ mutation test bắt được.
- An toàn nhờ **cấu trúc** (không có đường biểu diễn thao tác sai) thì ngoài test hành vi
  phải có thêm **test cấu trúc** trên hợp đồng (reflection trên request model). Test hành vi
  chỉ chứng minh hôm nay đúng; test cấu trúc canh giữ chính lý do nó đúng.
- Helper HTTP dùng chung (POST kèm antiforgery token) đặt ở `HttpClientTestExtensions`.
  Ngưỡng gộp là **bản copy thứ ba** — hai bản thì để nguyên còn dễ đọc hơn.

## Quy ước hạ tầng build
- **Central Package Management**: version package nằm ở `Directory.Packages.props`, csproj
  chỉ `<PackageReference Include="..." />` KHÔNG kèm `Version`. Lý do: trước đây
  `EntityFrameworkCore.SqlServer` (Infrastructure) và `EntityFrameworkCore.Design` (Web) là
  hai dòng version độc lập — nâng một bên mà quên bên kia thì hai phiên bản EF Core cùng
  tồn tại, lỗi biểu hiện lúc chạy ở chỗ chẳng liên quan.
- `.editorconfig` là nguồn duy nhất cho style. Hai bài học khi viết nó:
  - Luật `static readonly` phải khai báo **TRƯỚC** luật private field: Roslyn áp dụng luật
    khớp ĐẦU TIÊN, mà `required_modifiers = readonly` khớp cả static lẫn instance. Đảo
    thứ tự thì `AllowedExtensions` bị đòi đổi thành `_allowedExtensions`.
  - Thư mục `Migrations/` phải được loại trừ (`generated_code = true`, `charset = utf-8-bom`,
    `end_of_line = unset`): file do `dotnet ef` sinh ra, sửa tay thì lần sinh sau lại lệch
    và diff giả che mất migration thật.
- CI (`.github/workflows/ci.yml`) BẮT BUỘC có SQL Server thật vì bộ test cố ý không dùng
  InMemory. Máy dev dùng `Trusted_Connection=True` (Windows Auth) nhưng container Linux
  không có, nên CI ghi đè bằng biến môi trường
  `ConnectionStrings__DefaultConnection` (hai gạch dưới = cấu hình lồng nhau) — không sửa
  `appsettings.json`.

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

### Hoãn có chủ đích — KHÔNG làm cho tới khi điều kiện đủ
- `IUnitOfWork` **chưa có API transaction**, và sẽ chưa có cho tới khi làm nghiệp vụ đặt
  hàng. Thiết kế API transaction mà chưa có nghiệp vụ nào dùng nó là đoán: không biết
  transaction bọc bao nhiêu thao tác, có cần lồng nhau không, isolation level nào. Sai
  thiết kế ở đây tốn hơn là chờ.
- **Trang chi tiết sản phẩm chưa làm**, nên `/Product` trả 404 (chỉ có action `LoadMore`).
  Đây là **tính năng chưa làm**, không phải nợ kỹ thuật — không có gì hỏng, chỉ là chưa có.
- `Cart`/`CartItem` **chưa có `RowVersion`**, và chưa cần: giỏ hàng là dữ liệu của MỘT
  người, không có hai người cùng sửa một giỏ. Trường hợp duy nhất là một người mở hai tab,
  và ở đó "ghi sau thắng" đúng ý muốn của họ. Việc cần khoá thật là **trừ tồn kho lúc đặt
  hàng** — nhưng khoá trên `Products.RowVersion` (đã có), không phải trên giỏ.
- **Giỏ hàng chưa có JavaScript.** Endpoint đã trả `PartialView` và header `X-Cart-Count`
  cho AJAX, nhưng chưa có file JS nào gọi tới. Hiện chạy hoàn toàn bằng form POST + PRG nên
  **không có gì hỏng** — chỉ là mỗi thao tác phải tải lại trang.

### Nợ thật, chưa trả
- **`wwwroot/js/home-load-more.js` KHÔNG có test tự động** — dự án chưa có headless browser
  (Playwright). Kiểm chứng được: HTML mà `/Product/LoadMore` trả về, header `X-Next-Page`,
  `data-*` trên nút (integration test), cú pháp JS (`node --check`). Chưa kiểm chứng được:
  `insertAdjacentHTML` chạy thật, cập nhật `shownCount`, xoá nút khi hết dữ liệu, chặn
  double-click. Đây là khoảng trống test lớn nhất còn lại; thêm Playwright đáng một phase riêng.
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

### Đã trả (giữ lại để không ai "sửa" ngược)
- ~~Round-trip RowVersion~~ → đã làm, xem `## Quy ước Concurrency`.
- ~~3 lỗ hổng auth~~ → đã vá cả ba, xem `### Chống dò và brute-force đăng nhập`.
- ~~`Directory.Packages.props`, `.editorconfig`, CI~~ → đã có, xem `## Quy ước hạ tầng build`.
- ~~Validate `minPrice`/`maxPrice`~~ → đã làm bằng Data Annotation + `IValidatableObject`
  trên `ProductFilter`, KHÔNG phải ở Service: ràng buộc này không cần DB, không cần async,
  không cần DI — đúng tiêu chí ở `### Validation — đặt ở đâu`.
- ~~Giỏ hàng cho khách vãng lai~~ → đã làm bằng `ICartStore` + factory Scoped, xem
  `## Quy ước Giỏ hàng`. Đừng "đơn giản hoá" bằng cách bắt đăng nhập mới mua được.
- ~~Chống IDOR trên giỏ hàng~~ → đã làm bằng cấu trúc (`productId`, không `cartItemId`), có
  7 test và 3 mutation. Đừng thêm `cartItemId` vào request model "cho tiện".

## Cách làm việc với tôi (người học)
- Tôi đang học song song, nên MỌI đoạn code Claude Code viết ra đều phải kèm:
  1. Giải thích ngắn gọn: đoạn này làm gì, tại sao chọn cách này.
  2. Chỉ rõ những điểm liên quan trực tiếp đến kiến thức tôi đang học (LINQ, SOLID, DI, EF Core, Auth...).
  3. Nếu có 2 cách làm khác nhau (VD: Optimistic vs Pessimistic locking), giải thích cả 2 và lý do chọn 1.
- Với các phần khó (Transaction, Concurrency, Bulk Update), giải thích Ý TƯỞNG trước khi viết code.
- Cuối mỗi Phase: cập nhật chính file này với pattern/quy ước mới phát sinh.
