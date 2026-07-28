namespace MiniMart.Web;

/// <summary>
/// Tên policy rate limit, khai báo một chỗ để Program.cs, Controller và test
/// dùng cùng một hằng. Gõ lại chuỗi ở ba nơi thì sai một chữ là policy im lặng
/// không được áp dụng - <c>[EnableRateLimiting("Login")]</c> với tên không tồn
/// tại sẽ ném lúc chạy, nhưng chỉ khi có người gọi đúng action đó.
/// </summary>
public static class RateLimitPolicies
{
    public const string Login = "login";
}
