using System.Text.RegularExpressions;

namespace MiniMart.Tests;

/// <summary>
/// Gửi form POST kèm antiforgery token - việc mà mọi bộ test giỏ hàng đều cần.
/// </summary>
internal static class HttpClientTestExtensions
{
    /// <summary>
    /// Token luôn lấy từ trang ĐĂNG NHẬP, không phải từ trang đích.
    ///
    /// <para>
    /// Hai lý do: token gắn với cookie của client chứ không gắn với action nào, và
    /// trang đăng nhập LUÔN có form - khác <c>/Cart</c>, vì <c>_CartTable</c> chỉ
    /// render form khi giỏ có hàng nên giỏ rỗng thì không có token nào để lấy.
    /// </para>
    /// </summary>
    public static async Task<string> LayAntiforgeryTokenAsync(this HttpClient client)
    {
        var html = await client.GetStringAsync("/Account/Login");

        var match = Regex.Match(
            html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");

        Assert.True(match.Success, "Không tìm thấy antiforgery token trên trang đăng nhập.");

        return match.Groups[1].Value;
    }

    /// <param name="ajax">
    /// Gắn header <c>X-Requested-With</c> - chính header này quyết định Controller
    /// trả <c>PartialView</c> hay redirect theo Post-Redirect-Get.
    /// </param>
    public static async Task<HttpResponseMessage> PostFormAsync(
        this HttpClient client,
        string path,
        Dictionary<string, string> fields,
        bool ajax = false)
    {
        fields["__RequestVerificationToken"] = await client.LayAntiforgeryTokenAsync();

        var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = new FormUrlEncodedContent(fields)
        };

        if (ajax)
        {
            request.Headers.Add("X-Requested-With", "XMLHttpRequest");
        }

        return await client.SendAsync(request);
    }
}
