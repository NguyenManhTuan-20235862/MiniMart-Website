namespace MiniMart.Web.Middleware;

/// <summary>
/// Đánh dấu một action mà khi hỏng phải trả <b>JSON</b>, kể cả khi người gọi không gửi
/// header <c>Accept</c> nào.
///
/// <para>
/// Cần thiết vì thương lượng theo <c>Accept</c> là <b>chưa đủ</b> cho các endpoint
/// server-to-server. Máy chủ VNPay gọi <c>/Payment/IpnAction</c> không phải bằng trình
/// duyệt và không cam kết gửi <c>Accept: application/json</c>; đoán theo header sẽ trả
/// về một trang HTML tiếng Việt cho một chương trình đang chờ JSON.
/// </para>
/// <para>
/// Là <b>attribute</b> chứ không phải một danh sách đường dẫn trong middleware: danh
/// sách đường dẫn nằm xa nơi nó nói về, nên đổi route là nó lặng lẽ sai. Attribute đi
/// cùng action nên không thể lệch.
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public sealed class JsonErrorResponseAttribute : Attribute;
