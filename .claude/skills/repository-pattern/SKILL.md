---
name: repository-pattern
description: Dùng khi tạo mới hoặc chỉnh sửa Repository/Service trong MiniMart - đảm bảo đúng thứ tự IRepository (Domain) / IService (Application) → Implementation → đăng ký DI (Scoped) → inject vào Service, và luôn giải thích DIP.
---

# Repository Pattern - MiniMart

## Khi nào áp dụng
Mỗi khi tạo mới hoặc chỉnh sửa một Repository hoặc Service trong solution.

## Quy trình bắt buộc (đúng thứ tự)

1. **Định nghĩa interface trước tiên** — hai loại interface nằm ở HAI project khác nhau:
   - `I<Entity>Repository` (VD: `IProductRepository`) → **`MiniMart.Domain`**. Domain khai báo thứ nó *cần* từ tầng lưu trữ.
   - `I<Entity>Service` (VD: `IProductService`) → **`MiniMart.Application`**, nằm cùng tầng với implementation của nó, để Domain giữ thuần nghiệp vụ.
   - Interface chỉ khai báo method signature, không chứa logic.
   - Mọi method thao tác DB phải là `async`, trả về `Task`/`Task<T>`.

2. **Infrastructure (`MiniMart.Infrastructure`)**: implement interface.
   - Class `<Entity>Repository` implement `I<Entity>Repository`.
   - Dùng EF Core `DbContext` cho thao tác CRUD thông thường; dùng Dapper cho query đọc phức tạp/cần tối ưu hiệu năng.
   - Không đặt business logic ở Repository - chỉ lo truy vấn/lưu trữ dữ liệu.

3. **Đăng ký DI trong `Program.cs` (MiniMart.Web)**:
   ```csharp
   services.AddScoped<IProductRepository, ProductRepository>();
   services.AddScoped<IProductService, ProductService>();
   ```
   - Dùng vòng đời **Scoped** vì mỗi HTTP request có một `DbContext` riêng, và `DbContext` không thread-safe nên không được dùng Singleton; Transient thì tạo lại nhiều lần không cần thiết trong cùng một request.

4. **Service (`MiniMart.Application`)**:
   - `ProductService` implement `IProductService` (cả hai cùng nằm trong project này).
   - Inject `IProductRepository` qua constructor, KHÔNG inject trực tiếp class Infrastructure.
   - Service chứa business logic, gọi Repository để lấy/lưu dữ liệu.

## Luôn giải thích DIP khi sinh code

Mỗi lần áp dụng skill này, phải giải thích ngắn gọn cho người dùng (đang học song song) lý do Controller/Service không phụ thuộc trực tiếp vào Infrastructure:

- **Dependency Inversion Principle (chữ D trong SOLID)**: module cấp cao (Service) không nên phụ thuộc module cấp thấp (Infrastructure); cả hai nên phụ thuộc vào abstraction (Interface trong Domain).
- **Lợi ích thực tế trong dự án này**:
  - Unit test được Service bằng cách mock `IProductRepository`, không cần DB thật.
  - Đổi hạ tầng (VD: EF Core → Dapper, đổi DB provider) mà không phải sửa Service.
  - `MiniMart.Domain` không reference `MiniMart.Infrastructure` → tránh phụ thuộc vòng giữa các project trong solution.

## Checklist trước khi hoàn thành
- [ ] `IRepository` nằm trong `MiniMart.Domain`, không chứa logic.
- [ ] `IService` nằm trong `MiniMart.Application`, KHÔNG nằm ở Domain.
- [ ] `Repository` implement nằm trong `MiniMart.Infrastructure`.
- [ ] Đã đăng ký DI Scoped trong `Program.cs`.
- [ ] Service chỉ inject Interface, không inject class Infrastructure.
- [ ] Đã giải thích DIP cho người dùng.
