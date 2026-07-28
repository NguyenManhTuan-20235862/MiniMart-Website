using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MiniMart.Application.Interfaces;
using MiniMart.Domain.Entities;
using MiniMart.Web.Areas.Admin.Models;

namespace MiniMart.Web.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize(Roles = nameof(UserRole.Admin))]
public class UserController : Controller
{
    private const int PageSize = 20;

    private readonly IUserService _userService;

    public UserController(IUserService userService)
    {
        _userService = userService;
    }

    [HttpGet]
    public async Task<IActionResult> Index(
        int page = 1,
        string? search = null,
        CancellationToken cancellationToken = default)
    {
        var result = await _userService.GetUsersAsync(
            page: page,
            pageSize: PageSize,
            search: search,
            cancellationToken: cancellationToken);

        var viewModel = new UserListViewModel
        {
            Users = result.Items.ToList(),
            Page = result.Page,
            TotalPages = result.TotalPages,
            TotalUsers = result.TotalCount,
            Search = search
        };

        return View(viewModel);
    }
}
