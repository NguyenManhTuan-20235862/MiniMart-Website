namespace MiniMart.Common;

/// <summary>
/// Một trang dữ liệu kèm thông tin để vẽ bộ phân trang.
/// Không có TotalCount thì giao diện không biết còn trang sau hay không.
/// </summary>
public class PagedResult<T>
{
    public IReadOnlyList<T> Items { get; }

    /// <summary>Tổng số bản ghi khớp bộ lọc, KHÔNG phải số bản ghi trong trang này.</summary>
    public int TotalCount { get; }

    public int Page { get; }

    public int PageSize { get; }

    public int TotalPages => PageSize == 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);

    public bool HasPreviousPage => Page > 1;

    public bool HasNextPage => Page < TotalPages;

    public PagedResult(IReadOnlyList<T> items, int totalCount, int page, int pageSize)
    {
        Items = items;
        TotalCount = totalCount;
        Page = page;
        PageSize = pageSize;
    }
}
