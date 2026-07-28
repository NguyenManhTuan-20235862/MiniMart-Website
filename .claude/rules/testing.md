# Quy ước test

Đọc trước khi viết hoặc sửa bất kỳ test nào.

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
- Mô phỏng "hai người dùng" bằng **hai DI scope riêng**, không dùng chung một scope. Ba lý
  do, đều là hệ quả của `DbContext` đăng ký **Scoped** (một request = một scope = một context):
  - **Identity map**: trong MỘT `DbContext`, mỗi khoá chính chỉ có MỘT object. Đọc sản phẩm
    hai lần trả về cùng một instance, nên "người mua B" thấy ngay phép trừ của A dù A chưa
    lưu. Không còn UPDATE nào mang `RowVersion` cũ → không xung đột nào để phát hiện.
  - **Không thread-safe**: `Task.WhenAll` trên một context ném `InvalidOperationException`
    ("A second operation was started") — thất bại vì lỗi test, không phải vì nghiệp vụ.
  - Hai scope mới cho hai kết nối, hai transaction, hai bản `RowVersion` độc lập — đúng
    hình dạng của hai request thật.
- ⚠ Điều nguy hiểm là dùng chung scope **không làm test đỏ một cách rõ ràng**. Đã đo:
  tắt concurrency token + dùng chung scope thì `Assert.Single(thành công)` và
  `Assert.Single(thất bại)` VẪN PASS, vì crash của `DbContext` cho ra đúng hình dạng
  "1 thành công, 1 thất bại" của kết quả đúng. Chỉ **assert KIỂU exception** mới bắt được.
  Vì vậy test concurrency bắt buộc phải khẳng định kiểu exception, không chỉ khẳng định số đếm.
- `DbContextScopeTests` tồn tại để khoá chính phương pháp này lại: nó chứng minh bằng test
  rằng cùng scope → cùng `DbContext` → cùng object, còn khác scope → bản sao riêng.
- Markup có hook `data-*` cho JavaScript: mỗi hook phải có test khẳng định nó còn trong
  HTML (dùng `[Theory]` liệt kê từng cái). Đổi tên hook trong Razor làm JS im lặng ngừng
  chạy — không lỗi build, không lỗi runtime. Khi chưa có Playwright thì đây là cách duy
  nhất kiểm soát được file JS.
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

- **Helper đăng nhập phải tự khẳng định đăng nhập thành công**, đừng để test phát hiện
  qua hệ quả. Đăng nhập hỏng không ném gì cả: client chỉ đơn giản là không có cookie,
  rồi request tới `/Admin/...` bị đá về trang đăng nhập, và test đỏ ở một assertion nói
  về Base64 RowVersion — không manh mối nào chỉ về đăng nhập.

  ⚠ **Tín hiệu đúng KHÔNG phải mã trạng thái**, và đây là chỗ chín bản helper cũ lệch
  nhau:

  | Client | Đăng nhập THÀNH CÔNG | Đăng nhập THẤT BẠI |
  |---|---|---|
  | `AllowAutoRedirect = false` | **302** | 200 |
  | `AllowAutoRedirect = true` | **200** (đã đi theo redirect) | 200 |

  Đòi đúng `302` là sai cho cột hai — đã đo: **21 test đỏ** ngay khi gộp. Chấp nhận
  `Found or OK` là sai cho cột một, và sai theo hướng nguy hiểm hơn vì nó cho THẤT BẠI
  đi qua. Câu hỏi đúng là **"đã rời khỏi trang form chưa"**: mọi nhánh thất bại của
  `AccountController` đều `View()` lại chính URL đó, còn `RequestMessage.RequestUri` là
  URI CUỐI CÙNG sau khi đã đi hết chuỗi redirect. Cài đặt ở
  `TestAuthExtensions.BaoDamRoiKhoiForm`.

- ⚠ **Rate limit KHÔNG dùng chung giữa các test class** — điều ngược lại từng được ghi
  ở đây và trong `CLAUDE.md`, và nó **sai**. Mỗi test class tạo `WebApplicationFactory`
  riêng nên có host riêng, DI container riêng, limiter riêng; partition key đều là
  `"unknown"` nhưng bảng partition tách rời. Đo bằng hai factory cùng hạ hạn mức xuống
  2, đốt sạch của A rồi mới gọi B: `A = [200,200,429,429]`, `B = [200,200]`. Và
  `EnvironmentName` trong test là `Development` nên hạn mức thật là **1000/phút**.
  Giữ mục này lại để không ai "sửa" lại một vấn đề không tồn tại.

- Helper HTTP dùng chung (POST kèm antiforgery token) đặt ở `HttpClientTestExtensions`;
  helper đăng ký/đăng nhập ở `TestAuthExtensions`.
  Ngưỡng gộp là **bản copy thứ ba** — hai bản thì để nguyên còn dễ đọc hơn.

- ⚠ **Bộ test của chính cơ chế xác thực KHÔNG được dùng helper xác thực dùng chung.**
  `AuthHardeningTests` tự POST form thô, cố ý: nếu nó gọi `TestAuthExtensions` thì nó
  đang dùng thứ đang được kiểm để dựng dữ liệu đầu vào cho phép kiểm, và sẽ xanh kể cả
  khi cả hai cùng sai. Cùng hình dạng với việc hàm ký trong test VNPay được **viết lại**
  thay vì gọi `VnPayService` (xem `payments.md`).

- **Bóc `value` từ HTML thì BẮT BUỘC `WebUtility.HtmlDecode`.** Base64 của `rowversion`
  đôi khi chứa ký tự `+`, và `HtmlEncoder` mã hoá nó thành `&#x2B;`. HTML như vậy là
  ĐÚNG — trình duyệt tự giải mã lúc parse thuộc tính nên form thật gửi đi đúng chuỗi.
  Chỗ sai nằm ở test đọc chuỗi thô rồi dùng nó như dữ liệu.
  Chuỗi hậu quả đầy đủ, vì nó không hiển nhiên:
  POST lại `AAAAAAAd0&#x2B;s=` → binder không giải mã được Base64 → `RowVersion` = null
  → `SetExpectedRowVersion` bị bỏ qua → **không xung đột nào được phát hiện** →
  Controller redirect thay vì render lại → test đỏ ở `Expected: OK / Actual: Found`.
  Không một manh mối nào chỉ về dấu `+`.
- ⚠ Loại bug này **giả dạng flaky hạ tầng**: nó chỉ nổ khi giá trị `rowversion` ngẫu
  nhiên tình cờ có dấu `+` (đo được: khoảng 1 trong vài lần chạy toàn bộ, xanh khi chạy
  riêng). Tôi đã quy sai nguyên nhân cho hạn mức rate limit dùng chung trước khi bắt
  được thông báo thật. **Bài học quy trình: đỏ ngẫu nhiên thì phải bắt cho được thông
  báo lỗi trước khi kết luận nguyên nhân** — "xanh khi chạy riêng" đúng với cả hai giả
  thuyết nên nó không phân biệt được gì.

  📌 Đuôi của câu chuyện này dài hơn tôi tưởng: giả thuyết sai đó **không chỉ dẫn sai
  một lần**, nó còn được viết vào `CLAUDE.md` như một món nợ kỹ thuật và nằm đó nhiều
  phase, cho tới khi có người định đi "sửa gốc" nó. Đo mất năm phút và cho thấy vấn đề
  chưa từng tồn tại. **Nợ kỹ thuật chưa được đo cũng chỉ là giả thuyết — nhưng viết vào
  tài liệu rồi thì nó đọc y hệt sự thật.**
- Bài học phụ thuộc DỮ LIỆU phải được khoá bằng test **tự cấp dữ liệu**, không phải bằng
  test đọc dữ liệu thật. Ba test đọc HTML thật chỉ chạm vào bug này khi may rủi;
  `Helper_doc_value_phai_giai_ma_thuc_the_HTML` dựng thẳng chuỗi `&#x2B;` nên tất định.

## Test chạy trong TRÌNH DUYỆT THẬT (Playwright)

Đọc trước khi sửa `PlaywrightTestBase`, `KestrelWebFactory`, hoặc bất kỳ test nào có
đuôi `BrowserTests`.

- Chọn **`Microsoft.Playwright` (bản .NET)** chứ không `@playwright/test` của Node: giữ
  được MỘT lệnh `dotnet test` cho cả bộ, dùng chung helper đăng nhập và khuôn dọn dữ
  liệu đã có, không thêm hệ sinh thái thứ hai vào CI. Đánh đổi đã biết: mất trace viewer.
- Package **không tự tải trình duyệt**. Máy mới chạy một lần:
  `pwsh MiniMart.Tests/bin/Debug/net10.0/playwright.ps1 install chromium`. CI có bước
  riêng kèm `--with-deps`. `PlaywrightTestBase` bắt lỗi thiếu trình duyệt và ném lại kèm
  **đúng câu lệnh cần chạy** — cùng quy ước với `VnPayOptionsValidator`.
- `WebApplicationFactory` mặc định chạy trên `TestServer`, **không có cổng HTTP** nên
  trình duyệt không vào được. `KestrelWebFactory` dựng **hai** host trên cùng cấu hình:
  một `TestServer` cho lớp cơ sở, một Kestrel thật cho trình duyệt. Thứ tự trong
  `CreateHost` không đảo được — đọc comment trong file đó trước khi sửa.
- Cổng phải là **0** (hệ điều hành tự chọn). Đóng cứng một cổng là test đổ khi chạy song
  song hoặc khi máy đang có thứ khác chiếm cổng.
- `ViewportSize` phải **cố định**. Layout Bootstrap đổi theo breakpoint, để trình duyệt
  tự chọn là mở đường cho test đỏ tuỳ máy.

### ★ Vì sao phải có lớp test này: nó thấy thứ mà 591 test kia không thấy

Ngay lần chạy đầu tiên, Playwright tìm ra **hai lỗi thật** đã sống qua toàn bộ bộ test cũ:

| Lỗi | Vì sao test cũ không thấy |
|---|---|
| `<form>` bộ lọc bao cả lưới sản phẩm → form lồng nhau → nút "Thêm vào giỏ" của thẻ đầu tiên rơi vào form lọc | Chuỗi HTML **server gửi đi hoàn toàn đúng**; sai lầm nằm ở bộ phân tích HTML của trình duyệt |
| Thiếu `data-bs-auto-close="outside"` → dropdown sập ngay lần bấm đầu | Không có Bootstrap thì không có sự kiện `show.bs.dropdown` để mà sai |

Bài học tổng quát: **integration test khẳng định trên CHUỖI server sinh ra, không phải
trên CÂY DOM trình duyệt dựng lên.** Hai thứ đó khác nhau đúng ở chỗ HTML không hợp lệ —
và trình duyệt không báo lỗi, nó lặng lẽ sửa cây theo cách của nó.

### Viết assertion cho trình duyệt
- Dùng `#grid > .col` (con TRỰC TIẾP) chứ không `.col`. Đây không phải chi tiết thẩm mỹ:
  `insertAdjacentHTML('afterend')` đặt thẻ thành ANH EM của lưới, trang vẫn hiện đủ sản
  phẩm, và đếm bằng `.col` trần vẫn ra đúng số → test xanh vô nghĩa. Đã mutation: đổi
  `beforeend` → `afterend` làm **5 test đỏ** nhờ dấu `>`.
- Test cờ chặn double-click **không được dùng `ClickAsync` hai lần**: Playwright tự đợi
  nút hết `disabled`, mà `disabled = true` chính là nửa còn lại của cơ chế đang muốn
  kiểm. Dùng `dispatchEvent` hai lần liên tiếp.
- Locator quét cả trang sẽ khớp cả **dropdown giỏ hàng trên navbar** (đang ẩn). Bóc vùng
  trước (`.es-main-wrapper >> text=...`) rồi mới assert — `.First` không cứu được vì nó
  có thể chọn đúng phần tử vô hình đó.
- Giả lập lỗi server bằng `Page.RouteAsync` + `FulfillAsync(500)`, không cần sửa code.
