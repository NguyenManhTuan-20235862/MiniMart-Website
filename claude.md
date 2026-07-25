# MiniMart - ElectroShop

## Kiến trúc
Controller → Service → Repository → EF Core/Dapper → SQL Server

## Cấu trúc Solution
- MiniMart.Web             : ASP.NET Core MVC (Controller, View, ViewModel)
- MiniMart.Application      : Service layer (business logic)
- MiniMart.Domain           : Entity, Interface (IRepository, IService)
- MiniMart.Infrastructure   : EF Core DbContext, Repository impl, Dapper
- MiniMart.Common           : Helper, Constants, Custom Exception

## Quy ước code
- Controller KHÔNG chứa business logic, chỉ gọi Service.
- Service phụ thuộc Interface (Domain), không phụ thuộc trực tiếp Infrastructure (DIP).
- Đặt tên: IProductRepository / ProductRepository, IProductService / ProductService.
- Toàn bộ thao tác DB dùng async/await.
- Validation: Data Annotation trước, custom validation nếu logic phức tạp hơn.

## Cách làm việc với tôi (người học)
- Tôi đang học song song, nên MỌI đoạn code Claude Code viết ra đều phải kèm:
  1. Giải thích ngắn gọn: đoạn này làm gì, tại sao chọn cách này.
  2. Chỉ rõ những điểm liên quan trực tiếp đến kiến thức tôi đang học (LINQ, SOLID, DI, EF Core, Auth...).
  3. Nếu có 2 cách làm khác nhau (VD: Optimistic vs Pessimistic locking), giải thích cả 2 và lý do chọn 1.
- Với các phần khó (Transaction, Concurrency, Bulk Update), giải thích Ý TƯỞNG trước khi viết code.

## Lệnh hay dùng
- dotnet build
- dotnet ef migrations add <TenMigration> -p MiniMart.Infrastructure -s MiniMart.Web
- dotnet ef database update -p MiniMart.Infrastructure -s MiniMart.Web
- dotnet test

## Lưu ý đặc biệt
- Products.RowVersion dùng cho Optimistic Concurrency, không được xóa/sửa kiểu dữ liệu này khi generate code.