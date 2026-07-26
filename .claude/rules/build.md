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
- CI (`.github/workflows/ci.yml`) BẮT BUỘC có SQL Server thật vì bộ test cố ý không dùng
  InMemory. Máy dev dùng `Trusted_Connection=True` (Windows Auth) nhưng container Linux
  không có, nên CI ghi đè bằng biến môi trường
  `ConnectionStrings__DefaultConnection` (hai gạch dưới = cấu hình lồng nhau) — không sửa
  `appsettings.json`.
