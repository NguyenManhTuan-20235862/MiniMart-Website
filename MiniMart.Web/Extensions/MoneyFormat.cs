using System.Globalization;

namespace MiniMart.Web.Extensions;

/// <summary>
/// Định dạng tiền tệ cho tầng hiển thị.
///
/// CỐ Ý khoá vào InvariantCulture chứ không dùng CurrentCulture: cùng một
/// con số phải ra cùng một chuỗi bất kể máy chạy locale nào.
///
/// ASP.NET Core KHÔNG set CurrentCulture theo request nếu chưa thêm Request
/// Localization, nên nó bằng locale của OS. Để mặc định thì máy dev en-US in
/// "111,000" còn máy triển khai vi-VN in "111.000" - cùng một dòng code, hai
/// kết quả, và test nào assert trên chuỗi giá sẽ đỏ khi đổi máy.
/// </summary>
public static class MoneyFormat
{
    public static string ToMoneyText(this decimal value) =>
        value.ToString("N0", CultureInfo.InvariantCulture);
}
