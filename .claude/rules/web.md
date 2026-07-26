# Quy ước tầng Web — Controller, Razor, JavaScript, upload

Đọc file này trước khi sửa bất kỳ file nào trong `MiniMart.Web`.

## Controller và form
- Form dùng **ViewModel riêng**, không bind thẳng Entity (chống over-posting).
- Mọi POST có `[ValidateAntiForgeryToken]`. Xoá phải là POST, không dùng GET.
- Sau POST thành công luôn `RedirectToAction` (Post-Redirect-Get); thông báo qua `TempData`.
- Khi `ModelState` không hợp lệ và render lại form: **nạp lại dropdown/SelectList**,
  vì `<select>` không gửi danh sách lựa chọn lên server.
- Controller bắt exception nghiệp vụ và chuyển thành `ModelState.AddModelError`
  hoặc `TempData`, không tự quyết định nghiệp vụ.
- Area Admin: controller đặt tên `ProductController`, KHÔNG phải `AdminProductController`
  (Area đã cung cấp tiền tố `/Admin/`). Route area phải đăng ký trước route default.
- Nhiều lệnh `await` trong cùng một action phải **nối tiếp**, không gói vào
  `Task.WhenAll`: chúng dùng chung một `DbContext` (Scoped), mà `DbContext` không
  thread-safe → "A second operation was started on this context instance".

## Razor view và form lọc
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
- Trang **xem hàng** vẫn truy vấn bình thường khi `ModelState` không hợp lệ (chỉ hiện cảnh
  báo), khác form **ghi dữ liệu** (chặn hẳn). Chặn ở trang xem hàng là người dùng chỉ thấy
  trang trống.
- **`HtmlEncoder.Create(UnicodeRanges.All)`** phải được đăng ký trong `Program.cs`. Mặc
  định escape mọi ký tự non-ASCII nên `"Khoảng giá"` do Razor sinh ra thành
  `"Kho&#x1EA3;ng gi&#xE1;"` — vẫn hiển thị đúng nhưng HTML phình to, không đọc được khi
  debug, và mọi test assert chuỗi tiếng Việt do `@` sinh ra sẽ đỏ. `UnicodeRanges.All`
  VẪN escape `< > & " '` nên không hở XSS.
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

## Định dạng số ở tầng hiển thị
- Dùng `MoneyFormat.ToMoneyText()` (`MiniMart.Web/Extensions`) chứ không gọi trực tiếp
  `ToString("N0")`. Helper khoá vào `InvariantCulture` để cùng một số ra cùng một chuỗi
  bất kể locale của máy chạy.
- Lý do cứng: ASP.NET Core **không** set `CurrentCulture` theo request nếu chưa thêm
  Request Localization, nên nó bằng locale của OS. Máy dev en-US in `111,000`, máy triển
  khai vi-VN in `111.000` — cùng một dòng code, hai kết quả, và test nào assert trên
  chuỗi giá sẽ đỏ khi đổi máy.
- Cảnh báo nếu sau này thêm `CultureInfo.DefaultThreadCurrentCulture = "vi-VN"`: form
  POST được bind bằng `CurrentCulture`, nên form Admin sẽ parse `1000.50` thành `100050`.

## Request AJAX bổ sung dữ liệu vào trang đang mở: PartialView, KHÔNG phải JSON
Quy tắc mặc định của dự án: request AJAX mà kết quả **chỉ để hiển thị** thì trả
`PartialView()`. Đã cân nhắc và loại `Json()` cho `/Product/LoadMore`.
- Lý do quyết định: markup thẻ sản phẩm chỉ được định nghĩa **một lần** ở
  `_ProductCard.cshtml`. Trả JSON thì client phải dựng lại markup bằng JavaScript, tức
  viết lần thứ hai cùng một giao diện + cùng cách định dạng tiền + cùng logic badge
  còn/hết hàng, và phải tự escape XSS bằng tay vì **JSON không escape ký tự `<`**.
- Lý do quan trọng không kém: HTML server render **test được bằng integration test**,
  còn hàm dựng DOM trong JavaScript thì không có test nào chạm tới.
- Chỉ chọn `Json()` khi thật sự có **client không phải trình duyệt** (mobile app), hoặc
  client cần dữ liệu để tính toán chứ không phải để hiển thị. Ngoại lệ đã áp dụng:
  `/Cart/Summary` trả `Json` vì badge cần một CON SỐ, không cần một khối HTML.

### Ngoại lệ thứ hai: cập nhật TẠI CHỖ vài con số (dropdown giỏ hàng)
Ba endpoint ghi của giỏ hàng (`/Cart/Add`, `/Cart/UpdateQuantity`, `/Cart/Remove`) trả
`Json` khi request có `Accept: application/json`. Lý do: dropdown chỉ cần đổi số lượng
của một dòng, thành tiền dòng đó, tổng cộng và badge. Thay cả khối HTML làm dropdown
nhấp nháy và mất vị trí cuộn.

Ngoại lệ này chỉ hợp lệ khi giữ ĐỦ HAI ràng buộc dưới đây. Thiếu một trong hai là quay
về đúng cái mà quy ước muốn tránh — **đừng nới ra**:

1. **Server định dạng sẵn mọi số tiền** trong DTO (`TotalText`, `LineTotalText` qua
   `MoneyFormat`). JavaScript TUYỆT ĐỐI không tự format tiền, không tự cộng trừ tổng.
   Bỏ `TotalText` đi là cách in tiền tồn tại ở hai nơi rồi lệch nhau theo locale.
2. **DTO chỉ đủ để GÁN vào node có sẵn, không đủ để dựng markup.** Việc dựng HTML vẫn
   thuộc Razor: `GET /Cart/Dropdown` trả `_CartDropdown`. Chia việc theo LOẠI thay đổi —
   đổi con số thì JSON, đổi cấu trúc (thêm dòng, giỏ thành rỗng) thì tải lại partial.
   Vì vậy DTO trả về **đúng dòng vừa tác động**, không trả cả giỏ: trả cả giỏ sẽ dụ người
   viết JS lặp qua nó và dựng lại danh sách.

Hệ quả về markup và test:
- Hook cho JavaScript là thuộc tính **`data-*`**, không phải `class` (class để tạo kiểu,
  Bootstrap có thể đổi) và không phải `id` (không lặp lại được cho nhiều dòng).
- Đổi tên một `data-*` trong Razor làm `querySelector` trả `null` và JS **im lặng ngừng
  hoạt động** — không lỗi build, không lỗi runtime. Vì vậy mỗi hook phải có test khẳng
  định nó còn trong HTML (`[Theory]` liệt kê từng hook). Đây là cách duy nhất kiểm soát
  được file JS khi dự án chưa có Playwright.
- Node mà JS cần cập nhật phải **luôn được server render**, chỉ ẩn/hiện. Badge giỏ hàng
  KHÔNG được bọc trong `@if (count > 0)`: lần đầu thêm hàng vào giỏ rỗng sẽ không có node
  nào để gán `textContent` và JS buộc phải tự tạo thẻ.
- Endpoint có nhiều nhánh response thì **thứ tự kiểm tra là một phần của hợp đồng**:
  `fetch` của dropdown gửi cả `Accept: application/json` và `X-Requested-With`, nên nhánh
  JSON phải xét TRƯỚC nhánh PartialView. Đảo lại thì client nhận HTML và `response.json()`
  ném ở chỗ chẳng liên quan.
- Không so bằng `Request.Headers.Accept == "application/json"`: trình duyệt gửi cả danh
  sách kèm q-value nên so bằng sẽ trượt. Dùng `Contains`.
- KHÔNG dùng `View()` cho loại request này — nó gửi lại cả layout. Mỗi endpoint trả
  partial phải có test khẳng định response **không** chứa `<!DOCTYPE`/`<html`/`navbar`;
  `PartialView` → `View` là lỗi build-được-nhưng-sai, chỉ test mới bắt.
- Partial trả về nhiều item: tạo partial bao (`_ProductCards.cshtml`) chỉ làm việc lặp,
  và **không bọc thêm thẻ nào**. Output phải là dãy `.col` dán thẳng được vào `.row`;
  thêm một tầng `div` là `.col` không còn con trực tiếp của `.row` → vỡ Bootstrap grid.
- Metadata phân trang đi qua **HTTP response header** (`X-Next-Page`), vì body là HTML
  nên không có chỗ đặt. Hết dữ liệu → header rỗng. Đặt tên header thành `const` trong
  Controller để test tham chiếu cùng một hằng, không gõ lại chuỗi.
- Giá trị header phải là **ASCII/latin1**. Số lượng đi được (`X-Cart-Count`), thông báo
  tiếng Việt thì KHÔNG — nó phải đi trong thân response qua ViewModel.
- Endpoint phân trang phải nhận **đúng bộ tham số lọc** như action render trang 1.
  Thiếu một tham số là bấm "Xem thêm" xong nhận về sản phẩm đã bị loại ở trang 1.

## Nếu về sau cần endpoint JSON thật
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

## JavaScript gọi endpoint
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
- `fetch()` không tự gửi `__RequestVerificationToken` như form HTML, nên token phải đi
  qua header: `AddAntiforgery(o => o.HeaderName = "RequestVerificationToken")`.

## Layout
- Khu vực quản trị dùng `Areas/Admin/Views/Shared/_AdminLayout.cshtml`, khai báo trong
  `Areas/Admin/Views/_ViewStart.cshtml`. Trang khách hàng giữ `Views/Shared/_Layout.cshtml`.
- Partial dùng chung cho cả hai (VD `_StatusMessages`) đặt ở `Views/Shared` — view trong
  Area vẫn tìm thấy nhờ cơ chế fallback, không cần chép đôi.
- Layout được phân giải lúc **chạy**, không phải lúc biên dịch: sai tên trong `_ViewStart`
  thì `dotnet build` vẫn qua và chỉ nổ khi mở trang. Vì vậy mỗi layout phải có
  integration test kiểm chứng đúng layout được dùng.

## Upload file
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
