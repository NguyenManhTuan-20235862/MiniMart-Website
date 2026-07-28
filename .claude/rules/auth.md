# Quy ước Authentication / Authorization

Đọc trước khi sửa `AccountController`, cấu hình cookie/authorization trong
`Program.cs`, hoặc `UserService`.

- Cookie Authentication, scheme mặc định là `CookieAuthenticationDefaults.AuthenticationScheme`.
- `app.UseAuthentication()` BẮT BUỘC đứng trước `app.UseAuthorization()`.
- Claims dùng đúng `ClaimTypes.NameIdentifier` / `ClaimTypes.Name` / `ClaimTypes.Role`.
  Dùng chuỗi tự chế (`"role"`) sẽ khiến `IsInRole` và `[Authorize(Roles=...)]` im lặng trả false.
- `new ClaimsIdentity(claims, authenticationType)` — thiếu tham số thứ hai thì
  `IsAuthenticated` = false dù có đủ claims.
- Claims là **ảnh chụp lúc đăng nhập**. Đổi Role dưới DB không có hiệu lực cho đến
  khi người dùng đăng nhập lại. `SlidingExpiration` gia hạn cookie nhưng KHÔNG làm mới claims.
- Đặt `[Authorize]` ở cấp class để action thêm sau tự động được bảo vệ.

- Ẩn link trên menu chỉ là trải nghiệm người dùng, không phải bảo mật.

## Chống dò và brute-force đăng nhập
- `AuthenticateAsync` khi username KHÔNG tồn tại vẫn phải **verify một hash giả**. Return
  null sớm khiến username sai trả lời ~1ms còn username đúng ~100ms (PBKDF2) — đo được
  qua mạng. Đã kiểm chứng: bỏ hash giả thì tỉ lệ thời gian là **37.9x**, có thì ~1x.
  Thông báo lỗi chung là chưa đủ; **thời gian** cũng là một kênh rò rỉ.
- Rate limit `POST /Account/Login` bằng `AddRateLimiter` (có sẵn trong framework, không
  cần package). Đặt trên **POST**, không đặt cấp class — xem trang đăng nhập là bình thường.
- `app.UseRateLimiter()` BẮT BUỘC đứng **sau** `app.UseRouting()`: policy khai báo bằng
  attribute, mà endpoint metadata chỉ có sau UseRouting. Đặt trước thì im lặng không giới
  hạn gì. Đã mutation test: response trả 400 thay vì 429.
- Phân vùng theo **IP**, không theo username: theo username thì kẻ tấn công đổi username
  mỗi lần thử là thoát giới hạn. Đánh đổi đã biết: nhiều người sau cùng NAT dùng chung hạn.
- `RejectionStatusCode = 429`. Mặc định của framework là 503 — sai nghĩa, server vẫn khoẻ.
- `QueueLimit = 0`: vượt hạn thì từ chối ngay. Xếp hàng là tự tạo chỗ cho DoS.
- Hạn mức đọc từ config (`RateLimiting:LoginPermitLimit`): 5 ở `appsettings.json`, 1000 ở
  `appsettings.Development.json` để integration test đăng nhập nhiều lần không bị chặn.
  Test riêng cho rate limit **tự ghi đè xuống 2** bằng `AddInMemoryCollection`, KHÔNG sửa file.
- Mật khẩu: tối thiểu 8 ký tự + phải có cả chữ và số. KHÔNG đòi ký tự đặc biệt — NIST
  SP 800-63B cho thấy quy tắc phức tạp đẩy người dùng về `Password1!`, dễ đoán hơn một
  passphrase dài. Trần 100 ký tự cố ý để rộng.
