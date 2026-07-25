# MiniMart - ElectroShop

Website bán hàng điện tử xây dựng bằng ASP.NET Core MVC, theo kiến trúc phân lớp:

```
Controller → Service → Repository → EF Core/Dapper → SQL Server
```

Đây là dự án học tập, được phát triển song song với việc học LINQ, SOLID, DI, EF Core, Transaction/Concurrency và Authentication.

## Yêu cầu môi trường

| Thành phần | Phiên bản | Ghi chú |
|---|---|---|
| .NET SDK | 10.0.302 trở lên | `dotnet --version` để kiểm tra |
| SQL Server | LocalDB / Express / Developer | LocalDB đi kèm Visual Studio là đủ |
| `dotnet-ef` | 10.0.10 | Global tool, xem lệnh cài bên dưới |

Cài `dotnet-ef` (chỉ cần làm một lần trên mỗi máy):

```bash
dotnet tool install --global dotnet-ef
```

## Cấu trúc solution

| Project | Vai trò |
|---|---|
| `MiniMart.Web` | ASP.NET Core MVC — Controller, View, ViewModel. Đồng thời là **Composition Root** (nơi đăng ký DI) |
| `MiniMart.Application` | Service layer — `IService` + business logic |
| `MiniMart.Domain` | Entity, Repository interface (`IRepository`) |
| `MiniMart.Infrastructure` | EF Core DbContext, Repository implementation, Dapper |
| `MiniMart.Common` | Helper, Constants, Custom Exception |
| `MiniMart.Tests` | Unit test (xUnit + Moq) |

Đồ thị phụ thuộc — `Domain` là trung tâm, không phụ thuộc project nào ngoài `Common`:

```
Web ──→ Application ──→ Domain ──→ Common
 │                        ↑
 └──→ Infrastructure ─────┘
```

`Application` và `Infrastructure` không biết gì về nhau; cả hai chỉ nói chuyện qua interface trong `Domain`. Đây là cách Dependency Inversion Principle được ép buộc ở mức compile-time: nếu một Service lỡ `using MiniMart.Infrastructure`, project sẽ **không build được**.

Riêng `Web` được phép tham chiếu `Infrastructure` vì phải có một nơi đăng ký `AddScoped<IProductRepository, ProductRepository>()` — nơi đó gọi là Composition Root.

## Chạy dự án

```bash
dotnet restore
dotnet build
dotnet run --project MiniMart.Web
```

URL và port xem trong `MiniMart.Web/Properties/launchSettings.json`.

## Cấu hình database

Connection string đọc từ key `ConnectionStrings:DefaultConnection`.

**Không commit mật khẩu vào `appsettings.json`.** File `appsettings.Development.json` được track trong Git (đúng chuẩn, vì nó chứa cấu hình dev không nhạy cảm như `Logging`), nên mọi thứ đặt vào đó đều bị public. Dùng User Secrets — secret được lưu ngoài repo, tại `%APPDATA%\Microsoft\UserSecrets\`:

```bash
dotnet user-secrets init --project MiniMart.Web
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Server=(localdb)\\mssqllocaldb;Database=MiniMart;Trusted_Connection=True;TrustServerCertificate=True" --project MiniMart.Web
```

Nếu dùng Windows Authentication (`Trusted_Connection=True`) thì không có mật khẩu, đặt thẳng trong `appsettings.Development.json` cũng được. Chỉ bắt buộc dùng User Secrets khi connection string có `User Id` / `Password`.

## Migration

`MiniMart.Infrastructure` chứa DbContext, `MiniMart.Web` là startup project — nên mọi lệnh đều cần cả hai tham số `-p` và `-s`:

```bash
# Tạo migration mới
dotnet ef migrations add <TenMigration> -p MiniMart.Infrastructure -s MiniMart.Web

# Áp dụng lên database
dotnet ef database update -p MiniMart.Infrastructure -s MiniMart.Web

# Gỡ migration cuối (khi chưa update database)
dotnet ef migrations remove -p MiniMart.Infrastructure -s MiniMart.Web
```

Luôn đọc file migration sinh ra trong `MiniMart.Infrastructure/Migrations/` trước khi chạy `database update`, đặc biệt chú ý các thao tác `DropColumn`, `AlterColumn`, `RenameColumn` vì có thể gây mất dữ liệu.

> `Products.RowVersion` dùng cho Optimistic Concurrency. Không xoá hoặc đổi kiểu dữ liệu cột này.

## Test

```bash
dotnet test
```

**Lưu ý về test concurrency:** race condition (VD: trừ tồn kho khi nhiều người mua cùng lúc) **không thể** test bằng Moq hay EF Core InMemory provider — InMemory không thực thi concurrency token nên không ném `DbUpdateConcurrencyException`, khiến test luôn xanh kể cả khi code có bug oversell. Loại nghiệp vụ này cần integration test chạy trên SQL Server thật, bắn nhiều request song song bằng `Task.WhenAll`.

## Quy ước phát triển

- Controller không chứa business logic, chỉ gọi Service.
- Toàn bộ thao tác DB dùng `async`/`await`.
- Validation: Data Annotation trước, custom validation khi logic phức tạp hơn.

Chi tiết đầy đủ xem [CLAUDE.md](CLAUDE.md). Thư mục [.claude/skills/](.claude/skills/) chứa các quy trình chuẩn dùng khi phát triển (repository pattern, migration, kiểm tra concurrency, workflow học-và-code).

## Trạng thái hiện tại

Mới scaffold xong solution. Chưa có Entity, DbContext, connection string hay migration nào — các phần "Cấu hình database" và "Migration" ở trên mô tả quy trình sẽ dùng, chưa phải thứ đang chạy được.
