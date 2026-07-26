namespace MiniMart.Web;

/// <summary>
/// Khoá cho Keyed DI của hai kho giỏ hàng.
///
/// <para>
/// Bình thường mã nguồn KHÔNG được chọn kho - factory ở <c>Program.cs</c> đã chọn
/// đúng một lần và mọi nơi khác chỉ tiêm <c>ICartStore</c>. Ngoại lệ duy nhất là
/// lúc GỘP giỏ sau đăng nhập: thao tác đó vốn dĩ cần CẢ HAI kho cùng lúc, nên
/// không có cách nào diễn đạt bằng một <c>ICartStore</c> duy nhất.
/// </para>
/// <para>
/// Dùng Keyed DI (.NET 8+) thay vì tiêm thẳng <c>SessionCartStore</c> /
/// <c>DatabaseCartStore</c> để Controller vẫn chỉ phụ thuộc abstraction
/// <c>ICartStore</c> của Domain. Tiêm class cụ thể sẽ đưa tên một class của
/// Infrastructure vào constructor của Controller - Composition Root là
/// <c>Program.cs</c>, không phải Controller.
/// </para>
/// </summary>
public static class CartStoreKeys
{
    /// <summary>Giỏ của khách vãng lai (ASP.NET Core Session).</summary>
    public const string Session = "cart-store:session";

    /// <summary>Giỏ của người đã đăng nhập (bảng Carts/CartItems).</summary>
    public const string Database = "cart-store:database";
}
