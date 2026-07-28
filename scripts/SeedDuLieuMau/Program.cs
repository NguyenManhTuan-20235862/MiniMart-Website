using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MiniMart.Domain.Entities;
using MiniMart.Domain.Enums;
using MiniMart.Infrastructure.Data;

namespace SeedDuLieuMau;

/// <summary>
/// Xoá sạch dữ liệu (GIỮ tài khoản Admin) rồi nạp lại bộ dữ liệu mẫu.
///
/// <para>
/// Đi qua EF Core chứ không SQL thô vì hai lý do không lách được:
/// <c>PasswordHash</c> phải do đúng <see cref="PasswordHasher{TUser}"/> sinh ra
/// (PBKDF2 + salt ngẫu nhiên, không viết tay được), và <c>Order.Status</c> là enum
/// được cấu hình lưu dạng CHUỖI — viết SQL tay là chỗ để ghi ra một giá trị không
/// hợp lệ mà chỉ nổ khi EF đọc lên.
/// </para>
/// </summary>
internal static class Program
{
    // Hạt cố định: chạy lại cho ra ĐÚNG bộ dữ liệu cũ. Dữ liệu mẫu mà mỗi lần chạy
    // một khác thì không tái hiện được lỗi nào đã thấy.
    private const int HatNgauNhien = 20235862;

    private const string MatKhauKhach = "Khach@2026";

    private static readonly (string Ten, (string Ten, decimal Gia)[] SanPham)[] DuLieu =
    [
        ("Điện thoại",
        [
            ("iPhone 16 Pro Max 256GB", 34_990_000m),
            ("iPhone 16 128GB", 22_990_000m),
            ("Samsung Galaxy S25 Ultra", 33_490_000m),
            ("Samsung Galaxy S25", 22_990_000m),
            ("Samsung Galaxy A56 5G", 9_490_000m),
            ("Xiaomi 15 Pro", 24_990_000m),
            ("Xiaomi Redmi Note 14 Pro", 7_290_000m),
            ("OPPO Find X8 Pro", 27_990_000m),
            ("vivo V40 5G", 10_490_000m),
            ("Google Pixel 9 Pro", 25_990_000m)
        ]),
        ("Laptop",
        [
            ("MacBook Pro 14 M4 Pro", 49_990_000m),
            ("MacBook Air 13 M4", 27_990_000m),
            ("Dell XPS 14 9440", 42_590_000m),
            ("Dell Inspiron 15 3530", 15_990_000m),
            ("ASUS ROG Zephyrus G14", 44_990_000m),
            ("ASUS Vivobook 15 OLED", 16_490_000m),
            ("Lenovo ThinkPad X1 Carbon Gen 12", 41_990_000m),
            ("Lenovo IdeaPad Slim 5", 14_990_000m),
            ("HP Pavilion 15 eg3095TU", 17_490_000m),
            ("Acer Nitro V 15", 21_990_000m)
        ]),
        ("Tai nghe",
        [
            ("AirPods Pro 2 USB-C", 5_990_000m),
            ("AirPods 4", 3_790_000m),
            ("Sony WH-1000XM5", 7_990_000m),
            ("Sony WF-1000XM5", 5_490_000m),
            ("Bose QuietComfort Ultra", 8_990_000m),
            ("JBL Tune 770NC", 2_290_000m),
            ("Samsung Galaxy Buds3 Pro", 4_290_000m),
            ("Marshall Major V", 3_490_000m),
            ("Anker Soundcore Q30", 1_490_000m),
            ("Sennheiser Momentum 4", 7_490_000m)
        ]),
        ("Đồng hồ thông minh",
        [
            ("Apple Watch Series 10 46mm", 11_490_000m),
            ("Apple Watch SE 2 40mm", 6_490_000m),
            ("Apple Watch Ultra 2", 21_990_000m),
            ("Samsung Galaxy Watch7 44mm", 7_290_000m),
            ("Samsung Galaxy Watch Ultra", 15_990_000m),
            ("Garmin Forerunner 265", 12_490_000m),
            ("Garmin Instinct 2X Solar", 13_990_000m),
            ("Xiaomi Watch S4", 3_290_000m),
            ("Amazfit GTR 4", 3_990_000m),
            ("Huawei Watch GT 5 Pro", 8_990_000m)
        ]),
        ("Phụ kiện",
        [
            ("Sạc nhanh Anker 65W GaN", 890_000m),
            ("Pin dự phòng Anker 20000mAh", 1_290_000m),
            ("Cáp USB-C Belkin 2m", 390_000m),
            ("Chuột Logitech MX Master 3S", 2_590_000m),
            ("Bàn phím Keychron K2 Pro", 2_890_000m),
            ("Ổ cứng SSD Samsung T7 1TB", 2_490_000m),
            ("Thẻ nhớ SanDisk Extreme 256GB", 990_000m),
            ("Giá đỡ laptop nhôm Rain Design", 1_190_000m),
            ("Hub USB-C Ugreen 9 in 1", 1_490_000m),
            ("Túi chống sốc Tomtoc 14 inch", 690_000m)
        ])
    ];

    private static readonly string[] HoTen =
    [
        "Nguyễn Văn An", "Trần Thị Bình", "Lê Hoàng Cường", "Phạm Thu Dung",
        "Hoàng Minh Đức", "Vũ Thị Giang", "Đặng Quốc Hưng", "Bùi Khánh Linh",
        "Đỗ Trung Nam", "Ngô Phương Oanh"
    ];

    private static readonly string[] DiaChi =
    [
        "12 Ngõ 25 Vũ Ngọc Phan, Láng Hạ, Đống Đa, Hà Nội",
        "88 Nguyễn Trãi, Thanh Xuân, Hà Nội",
        "45/7 Trần Hưng Đạo, Quận 1, TP Hồ Chí Minh",
        "230 Lê Văn Sỹ, Quận 3, TP Hồ Chí Minh",
        "17 Nguyễn Văn Linh, Hải Châu, Đà Nẵng",
        "102 Lý Thường Kiệt, Ninh Kiều, Cần Thơ",
        "56 Hùng Vương, TP Huế, Thừa Thiên Huế",
        "9 Trần Phú, TP Nha Trang, Khánh Hoà",
        "301 Nguyễn Ái Quốc, Biên Hoà, Đồng Nai",
        "74 Hoàng Văn Thụ, TP Hải Phòng"
    ];

    /// <summary>
    /// Cờ bắt buộc để script chịu xoá dữ liệu.
    ///
    /// <para>
    /// Không có nó thì <c>dotnet run</c> trần chỉ IN RA kế hoạch rồi thoát. Lý do:
    /// đây là một dự án mà <c>dotnet run --project ...</c> là câu lệnh người ta gõ
    /// hàng ngày, và một script XOÁ SẠCH DATABASE không được phép nằm cách một lần
    /// gõ nhầm. Chốt này rẻ hơn nhiều so với việc khôi phục từ backup.
    /// </para>
    /// </summary>
    private const string CoXacNhan = "--xac-nhan";

    private static async Task<int> Main(string[] args)
    {
        var chuoiKetNoi = DocChuoiKetNoi();

        var options = new DbContextOptionsBuilder<MiniMartDbContext>()
            .UseSqlServer(chuoiKetNoi)
            .Options;

        await using var context = new MiniMartDbContext(options);

        if (!args.Contains(CoXacNhan, StringComparer.Ordinal))
        {
            await InKeHoachAsync(context, chuoiKetNoi);
            return 1;
        }

        var random = new Random(HatNgauNhien);

        await XoaSachAsync(context);

        var categories = await NapDanhMucVaSanPhamAsync(context);
        var khachHang = await NapKhachHangAsync(context);

        var sanPham = categories.SelectMany(c => c.Products).ToList();

        await NapDonHangAsync(context, khachHang, sanPham, random);

        await InTongKetAsync(context);

        return 0;
    }

    /// <summary>
    /// Đọc chuỗi kết nối từ chính <c>appsettings.json</c> của MiniMart.Web.
    ///
    /// <para>
    /// Chép chuỗi vào đây cũng chạy, nhưng nó tạo ra nguồn sự thật thứ hai: đổi
    /// instance SQL Server thì script này âm thầm trỏ vào một database khác — mà
    /// script này XOÁ DỮ LIỆU, nên trỏ nhầm là hỏng thật.
    /// </para>
    /// </summary>
    private static string DocChuoiKetNoi()
    {
        // Đi lên từ thư mục chạy (scripts/SeedDuLieuMau/bin/<Cấu hình>/net10.0) chứ
        // KHÔNG dùng đường dẫn tuyệt đối: đường dẫn tuyệt đối là thứ chạy trên đúng
        // một máy. Cùng cách mà MiniMart.Tests định vị appsettings.json, chỉ khác
        // số cấp vì project này nằm sâu hơn một tầng (trong scripts/).
        var duongDan = Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..", "MiniMart.Web", "appsettings.json");

        using var tep = File.OpenRead(duongDan);
        using var taiLieu = JsonDocument.Parse(tep, new JsonDocumentOptions
        {
            // appsettings.json của dự án có khoá bình luận dạng "// Logging".
            CommentHandling = JsonCommentHandling.Skip
        });

        return taiLieu.RootElement
            .GetProperty("ConnectionStrings")
            .GetProperty("DefaultConnection")
            .GetString()!;
    }

    /// <summary>
    /// Nói rõ sẽ xoá gì, Ở ĐÂU, rồi thoát mà không đụng vào dữ liệu.
    ///
    /// <para>
    /// In cả chuỗi kết nối là có chủ ý: sai lầm đắt nhất với script loại này không
    /// phải chạy nhầm lúc, mà là chạy đúng lúc trên <b>nhầm database</b>.
    /// </para>
    /// </summary>
    private static async Task InKeHoachAsync(MiniMartDbContext context, string chuoiKetNoi)
    {
        Console.WriteLine("Script này XOÁ TOÀN BỘ dữ liệu rồi nạp lại bộ mẫu.");
        Console.WriteLine();
        Console.WriteLine($"  Kết nối tới : {chuoiKetNoi}");
        Console.WriteLine($"  Sẽ xoá      : {await context.Products.CountAsync()} sản phẩm, "
            + $"{await context.Categories.CountAsync()} danh mục, "
            + $"{await context.Orders.CountAsync()} đơn hàng, "
            + $"{await context.Users.CountAsync(u => u.Role != UserRole.Admin)} tài khoản khách");
        Console.WriteLine($"  Sẽ GIỮ      : {await context.Users.CountAsync(u => u.Role == UserRole.Admin)} tài khoản Admin");
        Console.WriteLine();
        Console.WriteLine($"Chạy lại kèm {CoXacNhan} nếu đúng ý.");
    }

    /// <summary>
    /// Xoá theo ĐÚNG thứ tự khoá ngoại, từ bảng con lên bảng cha.
    ///
    /// <para>
    /// Không dựa vào Cascade: <c>OrderDetail → Product</c> và <c>Order → User</c> đều
    /// là <b>Restrict</b> (đơn hàng là bản ghi tài chính, phải sống lâu hơn thứ nó trỏ
    /// tới). Xoá Products trước OrderDetails là đâm thẳng vào khoá ngoại đó.
    /// </para>
    /// </summary>
    private static async Task XoaSachAsync(MiniMartDbContext context)
    {
        Console.WriteLine("Đang xoá dữ liệu cũ...");

        // ExecuteDeleteAsync gửi thẳng DELETE xuống DB, không nạp entity lên bộ nhớ.
        // Ở đây nó đúng chỗ: không có RowVersion nào cần kẹp, và không có logic
        // nghiệp vụ nào phải chạy khi xoá.
        await context.Payments.ExecuteDeleteAsync();
        await context.OrderDetails.ExecuteDeleteAsync();
        await context.Orders.ExecuteDeleteAsync();
        await context.CartItems.ExecuteDeleteAsync();
        await context.Carts.ExecuteDeleteAsync();
        await context.Products.ExecuteDeleteAsync();
        await context.Categories.ExecuteDeleteAsync();

        // GIỮ mọi tài khoản Admin. Lọc theo Role chứ không theo Id cụ thể: hardcode
        // Id là script chỉ đúng trên đúng một máy.
        var soKhachDaXoa = await context.Users
            .Where(u => u.Role != UserRole.Admin)
            .ExecuteDeleteAsync();

        var admin = await context.Users
            .AsNoTracking()
            .Where(u => u.Role == UserRole.Admin)
            .Select(u => u.Username)
            .ToListAsync();

        Console.WriteLine($"  Đã xoá {soKhachDaXoa} tài khoản khách.");
        Console.WriteLine($"  Giữ lại {admin.Count} tài khoản Admin: {string.Join(", ", admin)}");
    }

    private static async Task<List<Category>> NapDanhMucVaSanPhamAsync(MiniMartDbContext context)
    {
        var categories = DuLieu
            .Select(d => new Category
            {
                Name = d.Ten,
                Products = d.SanPham
                    .Select(sp => new Product
                    {
                        Name = sp.Ten,
                        Price = sp.Gia,
                        // Tồn kho tính từ tên: cố định nhưng KHÔNG đều nhau, nên trang
                        // danh sách có cả món sắp hết. Vẫn > 0 để mọi món mua được;
                        // món hết hàng sẽ do đơn hàng bên dưới tạo ra một cách tự nhiên.
                        Stock = 12 + Math.Abs(sp.Ten.GetHashCode(StringComparison.Ordinal) % 39),
                        // Chưa có thư mục wwwroot/images/products nên để null. View đã có
                        // nhánh placeholder cho trường hợp này.
                        ImageUrl = null
                    })
                    .ToList()
            })
            .ToList();

        // Thêm Category kéo theo cả Products nhờ navigation property — EF tự điền
        // CategoryId sau khi Category có Id. Không phải SaveChanges hai lần.
        context.Categories.AddRange(categories);
        await context.SaveChangesAsync();

        Console.WriteLine(
            $"Đã thêm {categories.Count} danh mục, "
            + $"{categories.Sum(c => c.Products.Count)} sản phẩm.");

        return categories;
    }

    private static async Task<List<User>> NapKhachHangAsync(MiniMartDbContext context)
    {
        // ĐÚNG cơ chế mà UserService dùng khi đăng ký: PBKDF2, salt ngẫu nhiên mỗi
        // lần gọi. Nhờ vậy 10 tài khoản này đăng nhập được bằng form thật.
        var hasher = new PasswordHasher<User>();

        var users = Enumerable.Range(1, 10)
            .Select(i =>
            {
                var user = new User
                {
                    Username = $"khach{i:D2}",
                    Role = UserRole.Customer
                };

                user.PasswordHash = hasher.HashPassword(user, MatKhauKhach);

                return user;
            })
            .ToList();

        context.Users.AddRange(users);
        await context.SaveChangesAsync();

        Console.WriteLine($"Đã thêm {users.Count} tài khoản khách (mật khẩu: {MatKhauKhach}).");

        return users;
    }

    private static async Task NapDonHangAsync(
        MiniMartDbContext context,
        List<User> khachHang,
        List<Product> sanPham,
        Random random)
    {
        var donHang = new List<Order>();

        for (var i = 0; i < khachHang.Count; i++)
        {
            var khach = khachHang[i];
            var soDon = random.Next(2, 4);   // 2 hoặc 3

            for (var d = 0; d < soDon; d++)
            {
                // Chọn 1-5 sản phẩm KHÁC NHAU. Trộn rồi lấy đầu danh sách thay vì bốc
                // ngẫu nhiên từng món: bốc từng món có thể trùng, và hai dòng cùng
                // ProductId trong một đơn là dữ liệu vô nghĩa.
                var soMon = random.Next(1, 6);
                var chon = sanPham.OrderBy(_ => random.Next()).Take(soMon).ToList();

                var order = new Order
                {
                    UserId = khach.Id,
                    // Rải trong 120 ngày gần đây để trang "Đơn hàng của tôi" có thứ tự
                    // thật để sắp xếp, không phải 25 đơn cùng một giây.
                    CreatedAt = DateTime.Now
                        .AddDays(-random.Next(1, 121))
                        .AddMinutes(-random.Next(0, 1440)),
                    RecipientName = HoTen[i],
                    RecipientPhone = $"09{random.Next(10, 100)}{random.Next(100000, 1000000)}",
                    ShippingAddress = DiaChi[i],
                    // Khoảng 2/3 đã thanh toán. Cần cả hai trạng thái thì trang đơn
                    // hàng mới hiện được cả hai kiểu badge.
                    Status = random.Next(0, 3) > 0 ? OrderStatus.Paid : OrderStatus.Pending
                };

                foreach (var product in chon)
                {
                    var soLuong = random.Next(1, 4);

                    order.Items.Add(new OrderDetail
                    {
                        ProductId = product.Id,
                        // SNAPSHOT cả tên lẫn giá — đúng quy ước: shop đổi tên hay đổi
                        // giá thì đơn cũ vẫn phải hiện đúng thứ khách đã thấy lúc mua.
                        ProductName = product.Name,
                        UnitPrice = product.Price,
                        Quantity = soLuong
                    });

                    // Trừ tồn kho cho dữ liệu nhất quán: 25 đơn mà tồn kho không đổi
                    // thì con số tồn kho nói dối về lịch sử bán hàng.
                    product.Stock = Math.Max(0, product.Stock - soLuong);
                }

                // Tổng tính từ giá ĐÃ SNAPSHOT, không đọc lại product.Price: hai chỗ
                // đọc giá là hai cơ hội để tổng đơn lệch khỏi tổng các dòng.
                order.TotalAmount = order.Items.Sum(item => item.UnitPrice * item.Quantity);

                donHang.Add(order);
            }
        }

        context.Orders.AddRange(donHang);
        await context.SaveChangesAsync();

        Console.WriteLine(
            $"Đã thêm {donHang.Count} đơn hàng, "
            + $"{donHang.Sum(o => o.Items.Count)} dòng đơn.");
    }

    private static async Task InTongKetAsync(MiniMartDbContext context)
    {
        var vn = CultureInfo.GetCultureInfo("vi-VN");

        Console.WriteLine();
        Console.WriteLine("── Tổng kết ──────────────────────────────");
        Console.WriteLine($"  Người dùng   : {await context.Users.CountAsync()}");
        Console.WriteLine($"  Danh mục     : {await context.Categories.CountAsync()}");
        Console.WriteLine($"  Sản phẩm     : {await context.Products.CountAsync()}");
        Console.WriteLine($"  Đơn hàng     : {await context.Orders.CountAsync()}");
        Console.WriteLine($"  Dòng đơn     : {await context.OrderDetails.CountAsync()}");

        var doanhThu = await context.Orders
            .Where(o => o.Status == OrderStatus.Paid)
            .SumAsync(o => o.TotalAmount);

        Console.WriteLine($"  Đã thanh toán: {doanhThu.ToString("N0", vn)} đ");
        Console.WriteLine($"  Hết hàng     : {await context.Products.CountAsync(p => p.Stock == 0)} sản phẩm");
    }
}
