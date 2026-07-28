---
name: minimart-taste
description: |
  Kích hoạt khi làm việc với dự án MiniMart-Website (ElectroShop) tại d:\MiniMart-Website.
  Cung cấp toàn bộ context: kiến trúc, quy ước code, entity, trang hiện có, nợ kỹ thuật
  và bẫy im lặng. Dùng khi bắt đầu session mới, nhận task feature/bug mới, hoặc cần
  nhắc lại context của dự án ASP.NET Core MVC này.
---

# MiniMart Taste — Project Context Snapshot

> **Mục đích:** Cung cấp đủ ngữ cảnh để làm việc đúng ngay từ đầu, không hỏi lại những
> thứ đã có trong tài liệu. Đọc file này trước khi sửa bất kỳ dòng code nào.

---

## 1. Dự án là gì

**MiniMart - ElectroShop** — website bán hàng điện tử, viết bằng **ASP.NET Core MVC (.NET 10)**,
là **dự án học tập** của một developer đang học song song LINQ, SOLID, DI, EF Core,
Transaction/Concurrency và Authentication.

- **Workspace:** `d:\MiniMart-Website`
- **Chạy:** `dotnet run --project MiniMart.Web --launch-profile http` → `http://localhost:5231`
- **DB:** SQL Server 2025 Express, instance `SQLEXPRESS`, database `MiniMart`

```
Controller → Service → Repository → EF Core / Dapper → SQL Server
```

---

## 2. Cấu trúc Solution (6 projects)

```
MiniMart.Web ──→ MiniMart.Application ──→ MiniMart.Domain ──→ MiniMart.Common
     │                                            ↑
     └──────────→ MiniMart.Infrastructure ────────┘
```

| Project | Vai trò |
|---------|---------|
| `MiniMart.Web` | Controller, View, ViewModel, DI Composition Root, `Program.cs` |
| `MiniMart.Application` | `IService` + `ServiceImpl` (business logic) |
| `MiniMart.Domain` | Entity, `IRepository`, `IUnitOfWork`, `ITransaction`, `ICartStore`, `IVnPayService` |
| `MiniMart.Infrastructure` | EF Core DbContext, Repository impl, UnitOfWork, Dapper, VNPay |
| `MiniMart.Common` | Helper (`MoneyFormat`), Constants, Custom Exception |
| `MiniMart.Tests` | 49 file: xUnit + Moq (unit) + `WebApplicationFactory` (integration) |

**Compile-time DIP:** `Application` không được `using MiniMart.Infrastructure` → build fail ngay.

---

## 3. Domain Entities

| Entity | Ghi chú quan trọng |
|--------|-------------------|
| `Product` | Có `RowVersion` (Optimistic Concurrency) — **KHÔNG xóa/đổi kiểu** |
| `Category` | `DeleteBehavior.Restrict` — KHÔNG đổi sang Cascade |
| `User` / `UserRole` | Auth tự viết, không dùng ASP.NET Identity |
| `Cart` / `CartItem` | `CartItems → Products` dùng `Cascade` (cố ý khác Category) |
| `Order` / `OrderDetail` | Snapshot giá tại thời điểm mua |
| `Payment` | VNPay integration |

---

## 4. Controllers & Services

**Services:** `ProductService`, `CategoryService`, `CartService`, `OrderService`, `PaymentService`, `UserService`

**Controllers (Web):** `HomeController`, `ProductController`, `CartController`, `CheckoutController`, `PaymentController`, `AccountController`, `ProfileController`

**Admin Area:** `DashboardController`, `Admin/ProductController`, `Admin/CategoryController`

---

## 5. Các trang hiện có

### Công khai
| URL | Trang |
|-----|-------|
| `/` | Trang chủ (danh sách SP + Load More) |
| `/Account/Login` | Đăng nhập — **full-page dark UI, `Layout = null`** |
| `/Account/Register` | Đăng ký — **full-page dark UI, `Layout = null`** |
| `/Cart` | Giỏ hàng |
| `/Checkout` | Đặt hàng + địa chỉ giao hàng |
| `/Checkout/Success` | Đặt hàng thành công |
| `/Profile` | Thông tin tài khoản |
| `/Payment/Return` | Kết quả VNPay redirect |

### Admin (role Admin)
`/Admin/Dashboard`, `/Admin/Product` (CRUD + `/BulkEdit`), `/Admin/Category` (CRUD)

### Chưa có
- `/Product/{id}` → trả 404, chưa làm
- Trang "Đơn hàng của tôi" — backend sẵn sàng, UI chưa có
- `Order.Status` — chưa có cột trạng thái

---

## 6. Quy ước code — KHÔNG ĐƯỢC BỎ QUA

1. **Controller** không chứa business logic — chỉ gọi Service.
2. **`IRepository`** → `MiniMart.Domain` | **`IService`** → `MiniMart.Application`.
3. **`SaveChangesAsync`** CHỈ ở `IUnitOfWork`, không bao giờ ở Repository.
4. Mọi thao tác DB: **`async`/`await`**, mọi method public nhận `CancellationToken`.
5. **Form dùng ViewModel riêng** — không bind thẳng Entity (chống over-posting).
6. Mọi POST có `[ValidateAntiForgeryToken]`. Xóa phải là POST, không GET.
7. **Cột tiền:** `decimal` + `HasPrecision(18, 2)` + `MoneyFormat.ToMoneyText()` (KHÔNG `ToString("N0")`).
8. `Application` và `Web` KHÔNG được `using Microsoft.EntityFrameworkCore`.
9. Tên test tiếng Việt không dấu, mô tả hành vi mong đợi.
10. **Mutation test bắt buộc**: cố tình phá code để xác nhận test đỏ.

---

## 7. Bẫy im lặng thường gặp nhất

| Bẫy | Hậu quả |
|-----|---------|
| `ToString("N0")` thay `MoneyFormat.ToMoneyText()` | Kết quả khác nhau giữa locale |
| `asp-for` cho `byte[] RowVersion` thiếu `type="hidden"` | Render `"System.Byte[]"`, concurrency biến mất |
| `ExecuteUpdate` cho đường ghi có `RowVersion` | Không qua Change Tracker → không kẹp token → oversell |
| `Stock -= n` cột không phải concurrency token | Oversell |
| `new ClaimsIdentity(claims)` thiếu tham số 2 | `IsAuthenticated = false` dù đủ claims |
| Chuỗi tự chế thay `ClaimTypes.Role` | `IsInRole` và `[Authorize(Roles=…)]` luôn false |
| `PartialView` → `View` | Layout lồng trong layout |
| `Task.WhenAll` + nhiều `await` chung `DbContext` | "A second operation was started" |
| `type="text"` cho ô số filter | vi-VN gõ `1.000.000` → bind `null` |
| `disabled` thay `readonly` cho input trong bảng bulk edit | Trình duyệt không gửi → binder bỏ mọi dòng sau |
| Nhận `userId` từ form thay vì `ICurrentUser` | IDOR |

---

## 8. File rules (đọc trước khi sửa)

| Sắp sửa gì | Đọc trước |
|---|---|
| Domain / Application / Infrastructure, truy vấn | `.claude/rules/data-access.md` |
| Bất kỳ file trong MiniMart.Web | `.claude/rules/web.md` |
| Giỏ hàng, luồng đăng nhập | `.claude/rules/cart.md` |
| Edit Product, UnitOfWork, RowVersion | `.claude/rules/concurrency.md` |
| AccountController, cookie/authorization | `.claude/rules/auth.md` |
| VNPay, chữ ký, `IVnPayService` | `.claude/rules/payments.md` |
| Bất kỳ test nào | `.claude/rules/testing.md` |
| `.editorconfig`, `.csproj`, CI | `.claude/rules/build.md` |

---

## 9. Skills chuyên biệt (đọc khi liên quan)

| Khi nào | Skill |
|---------|-------|
| Tạo/sửa Repository hoặc Service | `.claude/skills/repository-pattern` |
| Entity thay đổi, cần migration | `.claude/skills/ef-core-migration` |
| Trừ tồn kho, đặt hàng, race condition | `.claude/skills/concurrency-check` |
| Implement tính năng mới + giải thích học | `.claude/skills/learn-and-build` |

---

## 10. Lệnh hay dùng

```bash
dotnet build
dotnet test
dotnet format --verify-no-changes
dotnet run --project MiniMart.Web --launch-profile http

# Migration
dotnet ef migrations add <TenMigration> -p MiniMart.Infrastructure -s MiniMart.Web
dotnet ef database update -p MiniMart.Infrastructure -s MiniMart.Web

# Giả lập VNPay IPN
./scripts/test-vnpay-ipn.ps1 -OrderId <id> -Reset

# Kiểm tra DB
"C:\Program Files\Microsoft SQL Server\Client SDK\ODBC\180\Tools\Binn\SQLCMD.EXE" -S "localhost\SQLEXPRESS" -E -d MiniMart -C -Q "<sql>"
```

---

## 11. Nợ kỹ thuật đã biết (KHÔNG "sửa" nửa vời)

| Hạng mục | Ghi chú |
|----------|---------|
| `/Product/{id}` trả 404 | Tính năng chưa làm, không phải bug |
| Cart/CartItem chưa có RowVersion | Cố ý — giỏ là dữ liệu 1 người |
| Hai file JS chưa có E2E test | Chờ Playwright |
| CI chưa xác nhận chạy thật | File YAML có, chưa push |
| Rate limit + Session in-process | Cần Redis khi scale ngang |
| Đặt hàng chưa có retry | Optimistic chống oversell nhưng không retry |
| VNPay chưa chạy thật | Cần ngrok + key sandbox thật |
| Trang "Đơn hàng của tôi" | API có, UI chưa có |
| `Order.Status` | Chưa có — cố ý hoãn |
| Helper đăng nhập test bị chép 4 bản | Ngưỡng gộp đã qua, chưa refactor |

---

## 12. Phong cách làm việc

Người dùng đang **học song song**. Mọi code phải kèm:
1. Giải thích ngắn: làm gì, tại sao chọn cách này.
2. Điểm liên quan kiến thức đang học (LINQ, SOLID, DI, EF Core, Auth...).
3. Nếu có 2 cách → giải thích cả 2, nêu lý do chọn 1.

Phần khó (Transaction, Concurrency, Bulk Update) → **ý tưởng trước, code sau**.
