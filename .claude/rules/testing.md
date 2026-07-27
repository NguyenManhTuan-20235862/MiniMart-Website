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

- Helper HTTP dùng chung (POST kèm antiforgery token) đặt ở `HttpClientTestExtensions`.
  Ngưỡng gộp là **bản copy thứ ba** — hai bản thì để nguyên còn dễ đọc hơn.
