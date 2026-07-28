namespace MiniMart.Web.Models;

/// <summary>
/// Khách chọn trả tiền thế nào ở trang xác nhận đặt hàng.
///
/// <para>
/// Nằm ở tầng <b>Web</b>, không phải Domain: hôm nay nó chỉ quyết định người dùng được
/// chuyển đi đâu sau khi đơn đã tạo, chứ chưa là dữ liệu của đơn hàng. Ngày nào nó được
/// lưu xuống DB thì nó thành khái niệm nghiệp vụ và phải chuyển sang Domain.
/// </para>
/// <para>
/// <c>Cod = 0</c> là chủ ý, nhưng KHÔNG phải cơ chế chính giữ mặc định an toàn — điều
/// đó do property initializer trên <c>CheckoutViewModel.PhuongThuc</c> làm. Đã đo bằng
/// mutation: đảo <c>VnPay = 0</c> mà giữ initializer thì hành vi KHÔNG đổi và mọi test
/// vẫn xanh (đúng, vì đó là mutation không đổi ngữ nghĩa).
/// </para>
/// <para>
/// Giữ <c>Cod = 0</c> làm lớp thứ hai: mất initializer thì <c>default(enum)</c> vẫn
/// phải rơi vào lựa chọn AN TOÀN NHẤT — đặt hàng bình thường, chứ không đẩy khách sang
/// một cổng thanh toán ngoài ý muốn. Mutation bỏ CẢ HAI lớp thì test đỏ.
/// </para>
/// </summary>
public enum PhuongThucThanhToan
{
    /// <summary>Thanh toán khi nhận hàng. Đơn ở <c>Pending</c> và không đi đâu cả.</summary>
    Cod = 0,

    /// <summary>Chuyển sang cổng VNPay ngay sau khi đơn được tạo.</summary>
    VnPay = 1
}
