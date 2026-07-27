# Quy ước thanh toán VNPay

Đọc trước khi sửa `IVnPayService`, `VnPayService`, `VnPayOptions`, hoặc bất cứ gì
chạm tới chữ ký. Bí mật và cấu hình nằm ở `.claude/rules/build.md`.

## Nguyên tắc gốc
Máy chủ VNPay **dựng lại chuỗi đó ở phía họ** rồi băm bằng cùng khoá và so sánh. Nên
mọi quy ước dưới đây không phải "cho đẹp" — lệch một ký tự là ra một chữ ký hoàn toàn
khác và cả request bị từ chối. Không có "gần đúng".

## Bốn bước ký, và chỗ hỏng của từng bước

| Bước | Làm gì | Sai thì sao |
|---|---|---|
| 1. Gom tham số | `SortedDictionary<string,string>(StringComparer.Ordinal)` | — |
| 2. Ghép chuỗi | `key=urlencode(value)` nối bằng `&`, **theo thứ tự đã sắp** | Sai thứ tự → sai chữ ký |
| 3. Ký | `HMACSHA512(UTF8(secret))` trên `UTF8(chuỗi)`, ra hex thường | Sai encoding/thuật toán → sai chữ ký |
| 4. Ghép URL | `BaseUrl + "?" + chuỗi + "&vnp_SecureHash=" + chữ ký` | — |

- **`SortedDictionary` chứ không `Dictionary` + `.OrderBy()` lúc ghép.** Cả hai đều
  đúng, nhưng với `SortedDictionary` thứ tự là **tính chất của kiểu dữ liệu**, còn với
  `.OrderBy()` nó là một **bước có thể quên**. Đã mutation test: đổi sang `Dictionary`
  thì 3 test đỏ.
- **`StringComparer.Ordinal`**, không phải so sánh theo culture. Mọi khoá đều `vnp_*`
  nên hôm nay hai cách cho cùng kết quả — nhưng "hôm nay trùng nhau" không phải lý do
  để phụ thuộc vào culture của máy chạy.
- **`vnp_SecureHash` KHÔNG nằm trong tập được ký.** Nó là kết quả của phép băm nên
  không thể là đầu vào của chính nó.
- **Một hàm ghép chuỗi duy nhất cho CẢ chuỗi-để-ký lẫn query string thật.** Đây là
  nguyên nhân số một của lỗi "sai chữ ký": ký một chuỗi rồi gửi đi một chuỗi khác. Hai
  chuỗi chỉ khác cách mã hoá dấu cách (`%20` với `+` — **cả hai đều đúng theo chuẩn**)
  là hỏng, mà mắt thường đọc log hai bên vẫn thấy giống hệt.
- Dùng **`WebUtility.UrlEncode`** theo mẫu chính thức của VNPay. KHÔNG đổi sang
  `Uri.EscapeDataString` cho "chuẩn hơn": hai hàm mã hoá dấu cách khác nhau, và bên
  kiểm chữ ký là VNPay chứ không phải ta.
- Chỉ mã hoá **giá trị**, không mã hoá **khoá**.
- `HMACSHA512` chứ không `SHA512` trần: băm thường ai cũng tính được nên không chứng
  minh được ai gửi. HMAC trộn khoá bí mật vào, nên chỉ hai bên biết khoá mới ký được.

## Định dạng từng tham số — toàn bẫy im lặng
- `vnp_Amount` = số tiền **× 100**, ép `long`, `InvariantCulture`. Để `decimal` thì máy
  vi-VN in `"125000000,00"` và dấu phẩy đi thẳng vào chữ ký — máy dev en-US **không tái
  hiện được**. Cùng họ với bẫy `ToString("N0")` ở tầng Web.
- `vnp_CreateDate` / `vnp_ExpireDate` = `yyyyMMddHHmmss` theo **giờ Việt Nam (UTC+7)**,
  không phải UTC. Gửi UTC là lệch 7 tiếng → VNPay coi lệnh đã hết hạn.
- Offset +7 đóng cứng, KHÔNG tra `TimeZoneInfo` theo tên: tên khác nhau giữa Windows
  (`SE Asia Standard Time`) và Linux (`Asia/Ho_Chi_Minh`) nên tra theo tên là chạy ở máy
  dev và đổ trên CI. Việt Nam không có giờ mùa hè từ 1975 nên +7 là hằng số thật.
- `vnp_OrderInfo` chỉ ASCII không dấu — chuỗi này đi qua nhiều màn hình của VNPay và
  của ngân hàng.
- Bỏ qua tham số có giá trị rỗng: gửi `vnp_X=` vẫn được tính vào chữ ký nên hai bên dễ lệch.

## Kiến trúc
- `IVnPayService` ở **Domain**, `VnPayService` ở **Infrastructure** — cùng hình dạng với
  `ICartStore` và `IProductImageStorage`.
- ⚠ Nợ đặt tên đã biết: Domain đang biết tên một nhà cung cấp cụ thể. Giữ vì hôm nay chỉ
  có MỘT cổng, và tổng quát hoá từ đúng một cài đặt thường đoán sai chỗ cần tổng quát.
  Khi có cổng thứ hai thì đổi thành `IPaymentGateway`.
- `CreatePaymentUrl` **đồng bộ**, không `async`: bước này không gọi mạng, chỉ ghép chuỗi
  và băm. Đánh dấu `async` là nói dối về chi phí.
- **Singleton**: không giữ state thay đổi.
- `TimeProvider` tiêm qua DI (abstraction có sẵn của .NET 8+, không tự viết `IClock`).
  Bắt buộc vì `vnp_CreateDate` đi vào chữ ký — dùng đồng hồ thật thì mỗi lần chạy ra một
  chữ ký khác và không viết được giá trị mong đợi.

## Test
- Unit test thuần, không DB không mạng — đó là lý do phép ký KHÔNG được nhét vào Controller.
- ⚠ **Giới hạn phải nói rõ**: bộ test chứng minh chữ ký đúng theo đặc tả mà code đang
  *hiểu*. Nó KHÔNG chứng minh VNPay chấp nhận. Chỉ một giao dịch sandbox thật với khoá
  thật mới trả lời được điều đó.
- Test "chữ ký khớp" phải **tính lại HMAC từ chính query string đã gửi** — đúng việc máy
  chủ VNPay làm. Bản đầu tiên của test này khẳng định "phần được ký là tiền tố của query
  string" và **TAUTOLOGY**: cả hai vế bóc ra từ cùng một URL, nên nó xanh kể cả khi code
  ký một chuỗi rồi gửi chuỗi mã hoá kiểu khác. Chỉ mutation test phát hiện.
- Test thứ tự phải có **cả hai dạng**: một danh sách khoá cứng (bắt việc thêm tham số
  mới sai chỗ) và một khẳng định tính chất `dãy == dãy.OrderBy(Ordinal)` (vẫn đúng khi
  tập tham số đổi).
- Test `vnp_Amount` phải **ép `CultureInfo.CurrentCulture = "vi-VN"`** rồi khôi phục
  trong `finally`. Không ép thì máy dev không bao giờ thấy lỗi dấu phẩy.

## Return URL: chỉ để XEM, tuyệt đối không ghi DB

`GET /Payment/Return` là nơi VNPay đưa **trình duyệt của khách** quay về. Nó **KHÔNG
được** dùng để xác nhận đơn hàng. Bốn lý do, lý do đầu khiến ba lý do sau không cứu vãn được:

1. **Có thể KHÔNG BAO GIỜ xảy ra.** Khách trả tiền xong rồi đóng tab / mất mạng / hết
   pin — tiền đã trừ mà request này không tới. Nếu đây là nơi ghi nhận thanh toán thì
   đơn đó vĩnh viễn "chưa trả" trong khi ngân hàng đã trừ tiền. Không sửa được bằng code
   ở đây, vì code ở đây không chạy.
2. **Có thể xảy ra NHIỀU LẦN.** F5 là gửi lại đúng URL đó, chữ ký vẫn hợp lệ.
3. **Thời điểm không đáng tin.** URL nằm trong lịch sử trình duyệt, mở lại sau nhiều
   ngày vẫn hợp lệ. Chữ ký chứng minh dữ liệu **do VNPay tạo ra**, nó KHÔNG chứng minh
   "vừa mới xảy ra".
4. **Đã có kênh đúng: IPN** — VNPay gọi thẳng máy chủ sang máy chủ, tự thử lại khi thất
   bại, không phụ thuộc trình duyệt khách.

> Return trả lời *"hiện cho khách xem cái gì"*. IPN trả lời *"sự thật là gì"*.
> Trộn hai câu hỏi vào một chỗ là cách kinh điển để có những đơn đã thu tiền mà hệ thống
> không biết.

- Ràng buộc này thể hiện bằng **CẤU TRÚC**: `PaymentController` chỉ nhận `IVnPayService`
  trong constructor — không `IOrderService`, không `IUnitOfWork`, không `DbContext`. Có
  test cấu trúc khoá đúng danh sách tham số đó. Test hành vi ("gọi Return rồi kiểm DB
  không đổi") hôm nay **không chứng minh gì** vì chưa có cột nào để đổi.
- **Kiểm chữ ký TRƯỚC mọi thứ khác.** Mọi trường trong query string đều là chuỗi do
  người gửi tự đặt cho tới khi chữ ký được xác nhận — kể cả `vnp_ResponseCode`.
- Kiểm **CẢ HAI** mã: `vnp_ResponseCode` (kết quả lệnh gửi tới cổng) và
  `vnp_TransactionStatus` (kết quả chính giao dịch).
- Chữ ký sai → trả `VnPayReturn.KhongHopLe` **không kèm dữ liệu đã đọc được**, kể cả
  `OrderId`. Trả kèm là mời tầng trên lỡ tay dùng giá trị chưa xác thực. Trang lỗi cũng
  KHÔNG được dựng link sang đơn hàng từ dữ liệu đó.
- **Không ném exception** khi chữ ký sai: dữ liệu đến từ internet nên chữ ký sai là
  chuyện bình thường (bot quét URL). Ném là biến việc thường ngày thành HTTP 500 và làm
  ngập log tới mức sự cố thật bị chìm.
- Loại **cả `vnp_SecureHashType`** lẫn `vnp_SecureHash` khỏi phần băm.
- So chữ ký bằng `CryptographicOperations.FixedTimeEquals`, không `==`: so sánh chuỗi
  dừng ở byte đầu khác nhau nên **thời gian** rò rỉ "đoán đúng bao nhiêu ký tự đầu".
  Cùng loại lỗ hổng với việc `AuthenticateAsync` phải băm mật khẩu giả (`rules/auth.md`).
- Chấp nhận chữ ký viết HOA (VNPay không cam kết hoa/thường).
- `[AllowAnonymous]` trên `Return` dù class có `[Authorize]`: phiên có thể hết hạn trong
  lúc khách thao tác ở ngân hàng, và đẩy họ sang trang đăng nhập ngay sau khi vừa trả
  tiền là cách chắc nhất khiến họ tưởng giao dịch hỏng. An toàn vì trang này **không
  hiện dữ liệu riêng tư nào** — chi tiết đơn nằm sau `/Checkout/Success/{id}`, vẫn
  `[Authorize]` và vẫn lọc theo chủ sở hữu.
- Câu chữ phải nói **"đang được xác nhận"**, KHÔNG phải "đã thanh toán thành công": đó
  đúng là những gì hệ thống biết lúc đó. Hứa quá lời là tạo tranh cãi về sau với chính
  khách đang đọc.
- Khách tự huỷ (`24`) là một trạng thái RIÊNG, không phải lỗi. Dùng `enum` bốn trạng
  thái chứ không `bool ThanhCong` — ba trạng thái nhét vào một bool là chỗ sinh ra
  những câu thông báo sai.
- `Return` là `[HttpGet]`, đúng vì nó không ghi gì. Ngày nào nó bắt đầu ghi thì GET
  thành lỗ hổng (`<img src="...">` là kích hoạt được) — hai ràng buộc đi liền nhau.

### Bài học mutation ở phần này
- Bỏ `ChuKyHopLe &&` trong `ThanhToanThanhCong` → **cả 19 test vẫn xanh**, vì `Verify`
  đã trả toàn `null` khi chữ ký sai nên nhánh đó không chạm tới được qua đường công
  khai. An toàn thật nằm ở `Verify`. Lệnh kiểm vẫn phải ở lại (nó là thứ duy nhất còn
  đứng đó nếu ai đó "cải tiến" `Verify` để trả kèm dữ liệu), nên phải có **unit test
  dựng thẳng value object** mới khoá được.
- Test tích hợp phần thanh toán **phải tự cấp cấu hình VNPay** bằng `AddInMemoryCollection`.
  Bản đầu của `PaymentReturnTests` lấy khoá từ User Secrets trên máy tôi — xanh ở đây,
  đỏ ở mọi máy khác và trên CI.
- Hàm ký trong test được **viết lại**, không gọi `VnPayService`: nó mô phỏng phía ĐỐI
  TÁC. Dùng chính code đang test để tạo dữ liệu đầu vào thì test chỉ chứng minh code
  nhất quán với chính nó, kể cả khi cả hai chiều cùng sai.

## IPN: nguồn sự thật, và bốn lệnh kiểm theo ĐÚNG thứ tự

`GET /Payment/IpnAction` — VNPay gọi thẳng máy chủ sang máy chủ. Đây là nơi **DUY NHẤT**
được phép đặt `Order.Status = Paid`. Nghiệp vụ nằm ở `PaymentService`, Controller chỉ
chuyển query string vào rồi dịch kết quả sang JSON.

| # | Kiểm | Sai thì trả |
|---|---|---|
| 1 | Chữ ký HMAC | `97` |
| 2 | `vnp_TxnRef` trỏ tới đơn có thật | `01` |
| 3 | **`vnp_Amount` khớp `Order.TotalAmount`** | `04` |
| 4 | Đơn chưa được ghi nhận | `02` |

Thứ tự là **một phần của hợp đồng**, không phải sở thích:
- Kiểm 1 phải đầu tiên: trước đó mọi trường chỉ là chuỗi do người gửi tự đặt, kể cả
  `vnp_TxnRef`. Truy vấn DB bằng giá trị chưa xác thực là để người lạ điều khiển câu
  truy vấn của ta. Có test `Verify(GetForUpdateAsync, Times.Never)` khoá điều này.
- Kiểm 3 phải **trước mọi lệnh ghi**. Ghi `Payment` rồi mới kiểm tiền thì đã có một bản
  ghi tài chính sai trong DB, và `UNIQUE(OrderId)` khiến IPN đúng gửi lại sau đó **không
  ghi được nữa**.

### ★ Vì sao KHÔNG được bỏ bước đối chiếu số tiền
Chữ ký hợp lệ **chỉ** chứng minh *"thông báo này do VNPay tạo ra"*. Nó **KHÔNG** chứng
minh *"số tiền này đúng với đơn hàng của ta"*. Hai câu đó khác nhau hoàn toàn, và khoảng
cách giữa chúng chính là chỗ mất tiền.

> Chữ ký bảo vệ **tính toàn vẹn của thông điệp**. Đối chiếu số tiền bảo vệ **tính đúng
> đắn của giao dịch**. Không cái nào thay được cái kia.

Nếu có bất kỳ đường nào khiến số tiền lúc TẠO lệnh thanh toán khác số tiền của đơn — một
tham số lọt vào từ form, một lỗi làm tròn `× 100`, một lần sửa đơn sau khi khách đã bấm
thanh toán — thì VNPay sẽ thu đúng số tiền nhỏ đó, ký một thông báo **hoàn toàn hợp lệ**,
và IPN tới báo "thành công". Không có lệnh kiểm này thì đơn 10 triệu được đánh dấu đã
thanh toán bằng 10 nghìn: **không exception, không log lỗi, không gì cả**.

- So với `Order.TotalAmount` **đã lưu trong DB**, không phải tổng tính lại từ giỏ hàng:
  giỏ có thể đã đổi, còn con số ràng buộc với khách là con số trong đơn.
- Log khi lệch phải kèm **cả hai** con số — đó là dấu hiệu hoặc có bug ở đường tạo lệnh,
  hoặc có người đang thử. Cả hai đều cần người đọc log thấy ngay.
- Mutation: bỏ lệnh kiểm này → **8 test đỏ**.

### RspCode nghĩa là gì (rất dễ nhầm)
`RspCode` trả lời **"tôi đã nhận và xử lý xong thông báo của bạn chưa"**, KHÔNG phải
"giao dịch có thành công không". Một giao dịch **thất bại** mà ta ghi nhận được vẫn phải
trả `00`. Trả mã lỗi cho giao dịch thất bại khiến VNPay tưởng ta chưa nhận và gửi lại mãi.

### Idempotency
- VNPay gửi lại IPN khi chưa nhận được phản hồi → endpoint BẮT BUỘC idempotent.
- Lệnh kiểm `order.Status != Pending` là **đường đẹp**, có khe TOCTOU. Bảo đảm thật là
  **`UNIQUE(Payments.OrderId)`**; `DuplicateKeyException` được dịch thành `02`.
- Mutation đáng nhớ: bỏ lệnh kiểm ở Service thì **integration test vẫn xanh** — UNIQUE
  index bắt được và cho ra đúng `02`. Chỉ unit test (mock `IUnitOfWork`) đỏ. Đây là
  **tính chất tốt**, không phải lỗ hổng test: cùng hình dạng với việc bỏ lệnh kiểm "sản
  phẩm đã có đơn" ở `ProductService` (xem `data-access.md`).
- Vì vậy `Order` **vẫn không cần `RowVersion`**: UNIQUE index đã chống ghi đè, và hai mô
  hình concurrency trên một bảng khó suy luận hơn một.

### Những thứ khác của kênh này
- `[AllowAnonymous]` là **bắt buộc**: người gọi là máy chủ VNPay, không có cookie nào.
  Thứ xác thực là chữ ký HMAC — một cơ chế đầy đủ, và mạnh hơn cookie ở chỗ không bị
  đánh cắp được bằng XSS.
- **Luôn trả HTTP 200**, kể cả khi từ chối. VNPay đọc mã trong THÂN response, không đọc
  mã HTTP. Trả 400 cho chữ ký sai làm họ coi là lỗi vận chuyển và gửi lại mãi một thông
  báo không bao giờ hợp lệ.
- `PaymentService` **không bao giờ ném** — mọi kết cục là một `IpnResult`. Exception lọt
  lên Controller thành 500, mà 500 với VNPay là "chưa nhận được". Đổi lại **bắt buộc log
  kèm exception**: nuốt lỗi mà không log là biến sự cố thành sự im lặng.
- Ghi **cả lần thất bại** (`PaymentStatus.Failed` kèm `ResponseCode`). Khi khách gọi lên
  nói "tôi trả rồi", câu trả lời nằm ở chính bản ghi đó.
- `Payment.Amount` lưu số **cổng báo**, không chép `order.TotalAmount`: hai số bằng nhau
  tại thời điểm ghi, nhưng giá trị của cột này nằm ở chỗ nó độc lập khi đối soát.
- Enum lưu thành **chuỗi** (`HasConversion<string>()`): chèn giá trị mới vào giữa enum sẽ
  làm mọi dòng cũ đổi nghĩa trong im lặng nếu lưu int. Với cột trạng thái tài chính thì
  đọc được và ổn định quan trọng hơn vài byte.
- `IOrderRepository.GetForUpdateAsync` **không có tham số `userId`** — đúng, vì không có
  người dùng đăng nhập nào. ⚠ Vì vậy nó TUYỆT ĐỐI không được dùng cho endpoint nào nhận
  `orderId` từ người dùng: đó sẽ là IDOR ngay lập tức.

## Khởi tạo thanh toán: nút ở trang Checkout

`/Checkout` có **hai nút submit trong CÙNG một form**, phân biệt bằng `name="PhuongThuc"`
+ `value`. Không dùng cặp radio rồi một nút submit: ít hơn một thao tác, và không tồn tại
trạng thái "đã chọn VNPay nhưng chưa bấm gửi" — thứ luôn sinh ra câu hỏi giao diện phải
hiện gì lúc đó. Trình duyệt chỉ gửi name/value của **đúng nút được bấm**.

- **Thứ tự bắt buộc**: tạo đơn TRƯỚC, dựng URL thanh toán SAU. `vnp_TxnRef` là `OrderId`
  và `vnp_Amount` là `TotalAmount` đã chốt — cả hai chỉ tồn tại sau khi đơn được lưu.
- Chuyển sang cổng **KHÔNG** phải là đã thanh toán: đơn vẫn `Pending`, chỉ IPN mới đặt
  `Paid`. Có test riêng khoá điều này.
- Số tiền vào URL lấy từ `Order.TotalAmount` đã lưu, **không** từ giỏ hàng hay form —
  đây chính là con số mà IPN sẽ đối chiếu lại. Lấy từ nguồn khác là tự tạo ra khoảng
  cách giữa số tiền thu và số tiền của đơn.
- `PaymentService.TaoUrlThanhToanAsync` lọc theo `userId` **ngay trong truy vấn** và từ
  chối đơn đã `Paid`. Controller không tự nạp đơn, không tự kiểm chủ sở hữu.
- `Redirect(url)` chứ không `LocalRedirect`: đích ở tên miền khác. Ngoại lệ có kiểm soát
  với quy tắc chống open-redirect vì URL do **server** dựng từ `BaseUrl` trong cấu hình,
  không một ký tự nào đến từ request.
- **Mặc định phải an toàn**: thiếu tham số `PhuongThuc` thì đặt hàng bình thường, không
  đẩy khách sang cổng ngoài ý muốn. Giữ bằng HAI lớp — property initializer (`= Cod`) và
  `Cod = 0` trong enum.
- ⚠ Bài học mutation: đảo `VnPay = 0` mà giữ initializer thì **hành vi không đổi và mọi
  test vẫn xanh** — đúng, vì đó là mutation không đổi ngữ nghĩa. Nó lộ ra rằng chú thích
  ban đầu của tôi nói sai cơ chế (tưởng `default(enum)` là thứ đang giữ mặc định, thật ra
  là initializer). Chỉ mutation bỏ **cả hai** lớp mới làm test đỏ.
- `PhuongThuc` **không được lưu xuống DB**, cố ý: `Order` chỉ có `Status`. Thêm cột
  `PaymentMethod` bây giờ là lặp lại đúng cái sai đã tránh hai lần — thêm cột trước khi
  có nghiệp vụ đọc nó. Khi có báo cáo doanh thu theo phương thức thì mới thêm.

## Trang Return: câu chữ là một phần của hợp đồng

- Ghi chú **"kết quả sơ bộ / trạng thái chính thức sẽ được xác nhận qua hệ thống"** phải
  hiện cho MỌI trạng thái đã xác thực được — **kể cả "thành công"**. Có thật một khoảng
  thời gian khách đọc "thành công" ở đây trong khi đơn dưới DB vẫn `Pending`, vì trang
  này dựng từ dữ liệu đi qua trình duyệt còn ghi nhận đi qua IPN.
- Nhưng cũng **không được im lặng**: khách thấy tiền bị trừ mà đơn chưa đổi sẽ nghĩ mất
  tiền. Phải nói rõ "đang được xác nhận", không phải "đã thanh toán thành công".
- Diễn giải `vnp_ResponseCode` chỉ cho những mã mà khách **làm được gì đó** khi biết
  (hết tiền, quá hạn, sai OTP nhiều lần, ngân hàng bảo trì). Liệt kê đủ 30 mã của đặc tả
  là tạo ra 30 câu mà 29 câu không ai đọc, mỗi câu một chỗ để sai.
- **Không hiện mã thô** ra màn hình: với khách nó vô nghĩa, còn để tra cứu thì nó đã nằm
  trong `Payments.ResponseCode`.
- Chữ ký sai → **không** dựng link sang đơn hàng (dữ liệu chưa đáng tin, kể cả `OrderId`).

### Script thử tay: `scripts/test-vnpay-ipn.ps1`
Bộ test xUnit chạy trong `WebApplicationFactory` với HashSecret do **chính test bơm vào**.
Nó chứng minh nghiệp vụ đúng nhưng KHÔNG chạm tới khoá thật trong User Secrets, cấu hình
thật trong `appsettings.json`, hay pipeline HTTP của `dotnet run`. Script lấp đúng khoảng
trống đó, và cũng là công cụ để thử với khoá sandbox thật + ngrok (chỉ cần đổi `-BaseUrl`).

```
dotnet run --project MiniMart.Web --launch-profile http     # cửa sổ khác
./scripts/test-vnpay-ipn.ps1 -OrderId <id-don-Pending> -Reset
```

- 7 case: sai chữ ký, sai khoá, sai số tiền (2 kiểu), đơn không tồn tại, thành công, gửi lại.
- Thứ tự case có chủ đích: mọi case TỪ CHỐI chạy trước, case thành công sau cùng — nó đổi
  trạng thái đơn nên chạy trước sẽ khiến các case sau nhận `02` thay vì mã đang muốn kiểm.
- Sắp xếp khoá dùng `[StringComparer]::Ordinal`, **KHÔNG** `Sort-Object` (so sánh theo
  culture — đúng cái quy ước C# cấm). Sai chỗ này thì script tự tạo chữ ký khác server và
  mọi case đều "sai chữ ký": một kết quả trông rất thuyết phục mà hoàn toàn vô nghĩa.
- Script GHI VÀO DB thật. `-Reset` đưa đơn về `Pending` và xoá bản ghi `Payment` để chạy
  lại được. Chỉ dùng trên máy dev.
- Đã mutation test **chính script**: bỏ đối chiếu số tiền ở `PaymentService`, khởi động
  lại app, chạy script → 3 case FAIL, exit code 1, và dòng đắt giá nhất:
  `Payments.Amount = 1.00 (mong đợi 2000000.00)` với `Orders.Status = Paid`. Đơn 2 triệu
  được đánh dấu đã trả bằng **1 đồng**, chữ ký hợp lệ, không exception nào. Đây là bằng
  chứng chạy thật cho lý do lệnh kiểm số tiền không thể bỏ.

### Bài học migration
`dotnet ef` sinh `defaultValue: ""` cho cột enum-as-string NOT NULL. **Chuỗi rỗng không
phải giá trị `OrderStatus` hợp lệ** — để nguyên thì đơn cũ ném ngay khi EF đọc lên,
migration chạy êm còn lỗi nổ ở một trang chẳng liên quan. Phải sửa tay thành `"Pending"`.

### Guard cấu trúc đã yếu đi (ghi lại để không ai tưởng nó còn nguyên)
Trước khi có IPN, `PaymentController` chỉ nhận `IVnPayService` nên **không tồn tại đường
nào để ghi DB** — bảo đảm bằng cấu trúc. Từ khi có `IpnAction`, nó buộc phải giữ thêm
`IPaymentService`, tức bảo đảm đó không còn. Bù lại bằng thứ mạnh hơn: `Order.Status` giờ
đã tồn tại nên có **test hành vi** khẳng định `Return` với chữ ký hợp lệ báo thành công
vẫn để đơn ở `Pending` và không tạo `Payment` nào.

## Nợ đã biết
- `vnp_TxnRef = OrderId`. VNPay từ chối một `TxnRef` đã dùng, nên khách bỏ dở rồi thanh
  toán lại **cùng đơn** sẽ bị từ chối. Sửa đúng là bảng `PaymentAttempt` và lấy Id lần
  thử làm `TxnRef` — làm khi có luồng "thanh toán lại".
- `vnp_ExpireDate` cố định 15 phút, chưa lấy từ cấu hình.
- **Chưa có "thanh toán lại".** Khách chọn COD, hoặc bỏ dở ở cổng, thì không có đường
  nào quay lại trả tiền. Chưa làm vì nó bị chặn bởi một hạn chế thật: `vnp_TxnRef` đang
  là `OrderId`, mà VNPay từ chối một `TxnRef` đã dùng. Phải làm bảng `PaymentAttempt`
  trước, rồi mới thêm nút ở trang đơn hàng.
- **`Order` chưa lưu phương thức thanh toán** — xem mục nút Checkout ở trên.
- **IPN chưa chạy được ở local**: VNPay cần gọi tới máy chủ ta từ internet, mà
  `localhost:5231` thì họ không thấy. Thử thật cần ngrok (hoặc tương đương) và khai URL
  đó trên cổng quản trị.
- **Chưa có đối soát định kỳ.** IPN có thể mất hẳn (mạng, ta down đúng lúc). Hệ thống
  thật cần một job gọi API `queryDr` của VNPay cho các đơn `Pending` quá lâu. Chưa làm
  vì chưa chạy thật.
- **Trang `/Checkout/Success` chưa hiện trạng thái thanh toán** — vẫn như cũ dù `Order`
  đã có `Status`.
- `OrderStatus` cố ý chỉ có `Pending`/`Paid`. Chưa có `Shipping`/`Delivered`/`Cancelled`
  vì chưa luồng nào đặt chúng.
