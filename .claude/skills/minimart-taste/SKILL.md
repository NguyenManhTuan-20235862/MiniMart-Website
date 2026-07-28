---
name: minimart-taste
description: |
  Kích hoạt khi bắt đầu làm việc với dự án MiniMart-Website (ElectroShop) để nắm context
  đầy đủ: kiến trúc, quy ước code, entity, các trang hiện có, nợ kỹ thuật, và bẫy im lặng.
  Dùng khi: bắt đầu session mới, nhận task feature/bug mới, hoặc không chắc context hiện tại.
---

# MiniMart Taste — Project Context Snapshot

> **Mục đích:** Cung cấp đủ ngữ cảnh để làm việc đúng ngay từ đầu, không hỏi lại những
> thứ đã có trong tài liệu. Đọc file này trước khi sửa bất kỳ dòng code nào.

---

## 1. Dự án là gì

**MiniMart - ElectroShop** — website bán hàng điện tử, viết bằng **ASP.NET Core MVC (.NET 10)**,
là **dự án học tập** của một developer đang học song song LINQ, SOLID, DI, EF Core,
Transaction/Concurrency và Authentication.

```
Controller → Service → Repository → EF Core / Dapper → SQL Server 2025 Express (SQLEXPRESS)
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
| `MiniMart.Application` | `IService` + `ServiceImpl` (business logic), không biết Infrastructure |
| `MiniMart.Domain` | Entity, `IRepository`, `IUnitOfWork`, `ITransaction`, `ICartStore`, `IVnPayService` |
| `MiniMart.Infrastructure` | EF Core DbContext, `RepositoryImpl`, `UnitOfWork`, Dapper, VNPay |
| `MiniMart.Common` | Helper (`MoneyFormat`), Constants, Custom Exception |
| `MiniMart.Tests` | 49 file: xUnit + Moq (unit) + `WebApplicationFactory` (integration) |

**Compile-time DIP enforcement:** `Application` và `Infrastructure` không tham chiếu nhau.
Nếu viết `using MiniMart.Infrastructure` trong Application → build fail ngay.
`Web` tham chiếu `Infrastructure` chỉ để đăng ký DI (Composition Root).

---

## 3. Domain Entities

| Entity | Ghi chú quan trọng |
|--------|-------------------|
| `Product` | Có `RowVersion` (Optimistic Concurrency) — **KHÔNG được xóa/đổi kiểu** |
| `Category` | `DeleteBehavior.Restrict` — KHÔNG đổi sang Cascade |
| `User` / `UserRole` | Auth tự viết (không dùng ASP.NET Identity) |
| `Cart` / `CartItem` | `CartItems → Products` dùng `Cascade` (khác Category, cố ý) |
| `Order` / `OrderDetail` | Snapshot giá tại thời điểm mua — KHÔNG đọc lại `product.Price` để tính tổng |
| `Payment` | VNPay integration |

---

## 4. Services & Controllers hiện có

**Services (Application):** `ProductService`, `CategoryService`, `CartService`,
`OrderService`, `PaymentService`, `UserService`

**Controllers (Web):** `HomeController`, `ProductController`, `CartController`,
`CheckoutController`, `PaymentController`, `AccountController`, `ProfileController`

**Admin Area:** `DashboardController`, `Admin/ProductController`, `Admin/CategoryController`

---

## 5. Các trang hiện có

### Công khai
| URL | Trang |
|-----|-------|
| `/` | Trang chủ (danh sách SP + Load More) |
| `/Account/Login` | Đăng nhập (full-page dark UI, `Layout = null`) |
| `/Account/Register` | Đăng ký (full-page dark UI, `Layout = null`) |
| `/Cart` | Giỏ hàng |
| `/Checkout` | Đặt hàng |
| `/Checkout/Success` | Đặt hàng thành công |
| `/Profile` | Thông tin tài khoản |
| `/Payment/Return` | Kết quả VNPay redirect |

### Admin (role Admin)
| URL | Trang |
|-----|-------|
| `/Admin/Dashboard` | Dashboard |
| `/Admin/Product` + CRUD + `/BulkEdit` | Quản lý sản phẩm |
| `/Admin/Category` + CRUD | Quản lý danh mục |

### Chưa có (nợ kỹ thuật)
- `/Product/{id}` — trả 404, chưa làm
- Trang "Đơn hàng của tôi" — backend sẵn sàng, chưa có UI
- `Order.Status` — chưa có cột trạng thái

---

## 6. Quy ước code — KHÔNG ĐƯỢC BỎ QUA

1. **Controller** không chứa business logic — chỉ gọi Service.
2. **`IRepository`** đặt ở `Domain`, **`IService`** đặt ở `Application`.
3. **`SaveChangesAsync`** CHỈ ở `IUnitOfWork`, không bao giờ ở Repository.
4. Mọi thao tác DB dùng **`async`/`await`**, mọi method public nhận `CancellationToken`.
5. **Form dùng ViewModel riêng** — không bind thẳng Entity (chống over-posting).
6. Mọi POST có `[ValidateAntiForgeryToken]`. Xóa phải là POST, không GET.
7. **Cột tiền:** `decimal` với `HasPrecision(18, 2)`, dùng `MoneyFormat.ToMoneyText()` — KHÔNG `ToString("N0")`.
8. `Application` và `Web` KHÔNG được `using Microsoft.EntityFrameworkCore`.
9. Tên test viết tiếng Việt không dấu, mô tả hành vi mong đợi.
10. **Mutation test bắt buộc**: sau khi viết test, cố tình phá code để xác nhận test đỏ.

---

## 7. Bẫy im lặng thường gặp nhất

> Những lỗi này build được, chạy được, nhưng sai — không có exception nào báo.

| Bẫy | Hậu quả |
|-----|---------|
| `ToString("N0")` thay `MoneyFormat.ToMoneyText()` | Máy vi-VN in `111.000`, máy en-US in `111,000` — khác nhau |
| `asp-for` cho `byte[] RowVersion` thiếu `type="hidden"` | Render `"System.Byte[]"`, concurrency biến mất |
| `ExecuteUpdate` cho đường ghi có `RowVersion` | Không đi qua Change Tracker → không kẹp token → oversell |
| `Stock -= n` mà cột không phải concurrency token | Oversell — đã đo bằng test |
| `new ClaimsIdentity(claims)` thiếu tham số 2 | `IsAuthenticated = false` dù đủ claims |
| Chuỗi tự chế thay `ClaimTypes.Role` | `IsInRole` và `[Authorize(Roles=…)]` luôn false |
| `PartialView` → `View` | Response kèm cả layout, trang lồng trong trang |
| `Skip/Take` không có tie-breaker trong `OrderBy` | Bản ghi trùng/mất giữa hai trang |
| `Task.WhenAll` nhiều `await` dùng chung `DbContext` | "A second operation was started" |
| `type="text"` cho ô số ở form lọc | vi-VN gõ `1.000.000` → bind thành `null` |
| Thiếu `ParseLimitsInInvariantCulture` ở `[Range]` tiền | Máy vi-VN ném `ArgumentException` → HTTP 500 |
| `disabled` thay vì `readonly` cho ô input trong bảng sửa hàng loạt | Trình duyệt không gửi input disabled → binder bỏ mọi dòng sau |
| Nhận `userId` từ form thay vì `ICurrentUser` | IDOR: đặt đơn / đọc đơn dưới tên người khác |

---

## 8. File rules cần đọc trước khi sửa

| Sắp sửa gì | Đọc trước |
|---|---|
| Domain / Application / Infrastructure, truy vấn | `.claude/rules/data-access.md` |
| Bất kỳ file nào trong MiniMart.Web | `.claude/rules/web.md` |
| Giỏ hàng, luồng đăng nhập (nó gộp giỏ) | `.claude/rules/cart.md` |
| Edit Product, UnitOfWork, RowVersion | `.claude/rules/concurrency.md` |
| AccountController, cookie/authorization, UserService | `.claude/rules/auth.md` |
| VNPay, chữ ký, `IVnPayService` | `.claude/rules/payments.md` |
| Bất kỳ test nào | `.claude/rules/testing.md` |
| `.editorconfig`, `Directory.Packages.props`, `.csproj`, CI | `.claude/rules/build.md` |

---

## 9. Skills chuyên biệt (đọc khi liên quan)

| Khi nào | Skill |
|---------|-------|
| Tạo/sửa Repository hoặc Service | `.claude/skills/repository-pattern` |
| Entity thay đổi, cần migration | `.claude/skills/ef-core-migration` |
| Trừ tồn kho, đặt hàng, race condition | `.claude/skills/concurrency-check` |
| Implement tính năng mới + giải thích học | `.claude/skills/learn-and-build` |

---

## 10. Môi trường & lệnh hay dùng

```bash
dotnet build
dotnet test
dotnet format --verify-no-changes
dotnet run --project MiniMart.Web --launch-profile http   # http://localhost:5231

# Migration
dotnet ef migrations add <TenMigration> -p MiniMart.Infrastructure -s MiniMart.Web
dotnet ef database update -p MiniMart.Infrastructure -s MiniMart.Web

# Giả lập VNPay IPN
./scripts/test-vnpay-ipn.ps1 -OrderId <id> -Reset

# Kiểm tra DB trực tiếp
"C:\Program Files\Microsoft SQL Server\Client SDK\ODBC\180\Tools\Binn\SQLCMD.EXE" \
  -S "localhost\SQLEXPRESS" -E -d MiniMart -C -Q "<sql>"
```

**Connection string:** `appsettings.json` → key `ConnectionStrings:DefaultConnection`.
**VNPay secrets:** User Secrets — thiếu là app từ chối khởi động (`ValidateOnStart`).

---

## 11. Nợ kỹ thuật đã biết (KHÔNG "sửa" nửa vời)

| Hạng mục | Ghi chú |
|----------|---------|
| Trang chi tiết sản phẩm | Chưa có, `/Product` trả 404 — tính năng chưa làm, không phải bug |
| Cart/CartItem chưa có RowVersion | Cố ý — giỏ là dữ liệu 1 người, không cần |
| PartialView nhánh giỏ hàng | Giữ lại vì có test, nhưng chưa có client dùng |
| Hai file JS chưa có test E2E | Chờ Playwright |
| CI chưa xác nhận chạy thật | File YAML đã có, chưa push |
| Rate limit dùng in-process memory | Cần Redis khi scale ngang |
| Session giỏ hàng in-process | Cùng vấn đề, cùng giải pháp Redis |
| Đặt hàng chưa có retry khi xung đột | Optimistic chống oversell, nhưng 10 người mua 5 hàng chỉ 1 thành công |
| VNPay chưa chạy thật | Cần ngrok + key sandbox thật |
| Trang "Đơn hàng của tôi" | API có, UI chưa có |
| `Order.Status` | Chưa có — cố ý hoãn cho đến khi biết đủ trạng thái |

---

## 12. Phong cách làm việc với người dùng

Người dùng đang **học song song**, mọi đoạn code đều phải kèm:
1. Giải thích ngắn: đoạn này làm gì, tại sao chọn cách này.
2. Điểm liên quan đến kiến thức đang học (LINQ, SOLID, DI, EF Core, Auth...).
3. Nếu có 2 cách làm khác nhau → giải thích cả 2 và lý do chọn 1.

Với phần khó (Transaction, Concurrency, Bulk Update) → giải thích **ý tưởng trước**, code sau.
