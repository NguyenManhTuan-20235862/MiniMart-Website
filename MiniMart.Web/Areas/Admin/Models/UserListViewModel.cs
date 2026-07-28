using MiniMart.Domain.Entities;

namespace MiniMart.Web.Areas.Admin.Models;

public class UserListViewModel
{
    public List<User> Users { get; set; } = [];

    public int Page { get; set; } = 1;

    public int TotalPages { get; set; } = 1;

    public int TotalUsers { get; set; } = 0;

    public string? Search { get; set; }

    public bool HasPreviousPage => Page > 1;

    public bool HasNextPage => Page < TotalPages;
}
