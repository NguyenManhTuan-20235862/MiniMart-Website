# Quy ước hạ tầng build

Đọc trước khi sửa `.editorconfig`, `Directory.Packages.props`, file `.csproj`,
hoặc `.github/workflows/`.

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
## Bí mật (secret) — thứ nào được vào Git, thứ nào không

Tiêu chí duy nhất: **biết giá trị này có làm được điều gì mà lẽ ra không được phép không?**

| Giá trị | Ở đâu | Vì sao |
|---|---|---|
| `ConnectionStrings:DefaultConnection` | `appsettings.json` | Dùng `Trusted_Connection=True`, không chứa mật khẩu |
| `VnPay:TmnCode` | User Secrets / biến môi trường | Không bí mật (đi trong URL) nhưng của riêng mỗi shop |
| `VnPay:BaseUrl`, `VnPay:ReturnUrl` | `appsettings.json` | Công khai, nhưng phải ở cấu hình vì sandbox ≠ production |
| **`VnPay:HashSecret`** | **User Secrets (dev) / `VnPay__HashSecret` (CI, prod)** | Ai có nó thì **tự ký được** một "đã thanh toán thành công" |

- `VnPay:HashSecret` không bảo vệ dữ liệu, nó bảo vệ **tiền**. Nó là khoá HMAC-SHA512
  dùng cho CẢ hai chiều: ký lệnh gửi đi, và kiểm chữ ký khi VNPay gọi về. Lộ khoá là
  kẻ tấn công tự tạo được một callback hợp lệ báo đơn đã thanh toán mà không trả đồng nào.
- Ba lý do không hardcode, xếp theo mức độ khó sửa: (1) **Git nhớ vĩnh viễn** — xoá ở
  commit sau không xoá khỏi lịch sử, phải rewrite history và **đổi khoá**; (2) mỗi môi
  trường một khoá, hardcode nghĩa là đổi khoá phải build lại; (3) khoá nằm nguyên văn
  trong DLL đã build, `strings` là đọc được.
- `appsettings.json` giữ **khoá bình luận** `"// HashSecret"` mô tả cách khai báo, nhưng
  KHÔNG có khoá `"HashSecret"` thật. Có test cấu trúc canh việc này
  (`VnPayOptionsTests.appsettings_json_TUYET_DOI_khong_chua_HashSecret`) — cần thiết vì
  thêm bí mật vào file làm ứng dụng chạy **tốt hơn** (hết lỗi cấu hình) và mọi test khác
  vẫn xanh. Không có gì tự tố giác ngoài test đó.
- `VnPayOptions` dùng `.ValidateOnStart()` + `IValidateOptions<T>` viết tay. Thông báo lỗi
  phải nêu **câu lệnh cần chạy**, không chỉ nêu tên khoá thiếu — người đọc nó đang không
  biết phải làm gì.
- ⚠ Đã mutation test: **bỏ `.ValidateOnStart()` thì cả 399 test vẫn xanh**, vì Options
  được tạo LƯỜI nên không test nào chạm tới. Phải có test boot app ở environment
  `Production` (nơi User Secrets không nạp) và khẳng định `OptionsValidationException`.
- Hệ quả bắt buộc nhớ: test nào `UseEnvironment("Production")` phải **tự cấp** cấu hình
  VNPay bằng `AddInMemoryCollection`, không sửa file. Cùng cách `LoginRateLimitTests` tự
  hạ hạn mức xuống 2.

- CI (`.github/workflows/ci.yml`) BẮT BUỘC có SQL Server thật vì bộ test cố ý không dùng
  InMemory. Máy dev dùng `Trusted_Connection=True` (Windows Auth) nhưng container Linux
  không có, nên CI ghi đè bằng biến môi trường
  `ConnectionStrings__DefaultConnection` (hai gạch dưới = cấu hình lồng nhau) — không sửa
  `appsettings.json`.
