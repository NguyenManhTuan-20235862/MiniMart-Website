namespace MiniMart.Domain.ValueObjects;

/// <summary>
/// Một dòng gợi ý trong ô tìm kiếm ở header.
///
/// <para>
/// Read model riêng chứ không trả thẳng <c>Product</c>, và lý do KHÔNG phải là gu:
/// dropdown gợi ý chỉ cần đủ để vẽ một dòng, mà <c>Product</c> mang theo <c>Stock</c>
/// và <c>RowVersion</c>. Chiếu sang kiểu này là biến "không lộ tồn kho" từ một điều
/// phải nhớ thành một điều <b>không biểu diễn được</b> — cùng lý do
/// <c>OrderSummary</c> tồn tại (xem <c>rules/data-access.md</c>).
/// </para>
/// <para>
/// Vì vậy <see cref="ConHang"/> là <c>bool</c> chứ không phải con số: đúng thứ giao
/// diện cần, và không có cách nào lỡ tay in ra số lượng tồn.
/// </para>
/// </summary>
public record ProductSuggestion(
    int Id,
    string Name,
    decimal Price,
    string? ImageUrl,
    string CategoryName,
    bool ConHang);
