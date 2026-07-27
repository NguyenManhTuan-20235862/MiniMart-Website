using MiniMart.Application.Models;

namespace MiniMart.Tests;

/// <summary>
/// Thông tin giao hàng hợp lệ dùng chung cho mọi test đặt hàng.
///
/// <para>
/// Gộp về một chỗ vì <c>CheckoutAsync</c> có hơn 20 điểm gọi trong bộ test: rải giá
/// trị mẫu ra từng file thì thêm một trường bắt buộc vào <see cref="ShippingInfo"/>
/// là phải sửa hơn 20 nơi. Ngưỡng gộp của dự án là bản copy thứ ba - ở đây đã vượt xa.
/// </para>
/// <para>
/// ⚠ Đây CHỈ dành cho các test không quan tâm tới nội dung địa chỉ. Test nào đang
/// kiểm chứng chính việc snapshot địa chỉ phải tự khai giá trị riêng, nếu không thì
/// nó chỉ đang so một hằng số với chính nó qua một vòng DB.
/// </para>
/// </summary>
public static class CheckoutTestData
{
    public const string TenNguoiNhan = "Nguyen Van A";
    public const string SoDienThoai = "0912345678";
    public const string DiaChi = "12 Nguyen Trai, Thanh Xuan, Ha Noi";

    public static ShippingInfo GiaoHang { get; } =
        new(TenNguoiNhan, SoDienThoai, DiaChi);

    /// <summary>
    /// Cùng dữ liệu đó ở dạng các trường form, cho test đi qua HTTP thật.
    ///
    /// <para>
    /// Tên khoá phải khớp CHÍNH XÁC tên property của <c>CheckoutViewModel</c>: sai một
    /// chữ thì trường về rỗng, <c>[Required]</c> nổi lên, và request không bao giờ tới
    /// nhánh mà test đang muốn kiểm - test đỏ ở một chỗ chẳng liên quan gì.
    /// </para>
    /// </summary>
    public static Dictionary<string, string> Form(
        string? tenNguoiNhan = null,
        string? soDienThoai = null,
        string? diaChi = null) =>
        new()
        {
            ["RecipientName"] = tenNguoiNhan ?? TenNguoiNhan,
            ["RecipientPhone"] = soDienThoai ?? SoDienThoai,
            ["ShippingAddress"] = diaChi ?? DiaChi
        };
}
