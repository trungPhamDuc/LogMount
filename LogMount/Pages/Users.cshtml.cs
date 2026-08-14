using LogMount.Data;
using LogMount.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace LogMount.Pages;

[Authorize(Roles = UserRoles.Admin)]
public class UsersModel : PageModel
{
    private readonly LogMountDbContext _db;
    private readonly PasswordHasher<UserAccount> _passwordHasher = new();
    public UsersModel(LogMountDbContext db) => _db = db;

    public IReadOnlyList<UserAccount> Accounts { get; private set; } = [];
    [BindProperty] public UserForm Input { get; set; } = new();

    public async Task OnGetAsync() => Accounts = await _db.UserAccounts.AsNoTracking().OrderBy(x => x.Username).ToListAsync();

    public async Task<IActionResult> OnPostCreateAsync()
    {
        if (string.IsNullOrWhiteSpace(Input.Password)) { ModelState.AddModelError("Input.Password", "Mật khẩu là bắt buộc."); return await ReloadAsync(); }
        var username = Input.Username.Trim();
        if (await _db.UserAccounts.AnyAsync(x => x.Username == username)) { ModelState.AddModelError("Input.Username", "Tên đăng nhập đã tồn tại."); return await ReloadAsync(); }
        var account = Input.ToAccount();
        account.Username = username;
        account.PasswordHash = _passwordHasher.HashPassword(account, Input.Password);
        _db.UserAccounts.Add(account);
        await _db.SaveChangesAsync();
        TempData["Status"] = "Đã tạo tài khoản.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostUpdateAsync(int id)
    {
        var account = await _db.UserAccounts.FindAsync(id);
        if (account is null) return NotFound();
        account.FullName = Input.FullName.Trim(); account.Department = Input.Department?.Trim(); account.EmployeeId = Input.EmployeeId?.Trim(); account.Role = Input.Role;
        if (!string.IsNullOrWhiteSpace(Input.Password)) account.PasswordHash = _passwordHasher.HashPassword(account, Input.Password);
        await _db.SaveChangesAsync();
        TempData["Status"] = "Đã cập nhật tài khoản.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        var account = await _db.UserAccounts.FindAsync(id);
        if (account is null) return NotFound();
        if (account.Username == User.Identity?.Name) { TempData["Error"] = "Không thể xóa tài khoản đang đăng nhập."; return RedirectToPage(); }
        if (account.Role == UserRoles.Admin && await _db.UserAccounts.CountAsync(x => x.Role == UserRoles.Admin) == 1) { TempData["Error"] = "Phải còn ít nhất một Admin."; return RedirectToPage(); }
        _db.UserAccounts.Remove(account);
        await _db.SaveChangesAsync();
        TempData["Status"] = "Đã xóa tài khoản.";
        return RedirectToPage();
    }

    private async Task<PageResult> ReloadAsync() { await OnGetAsync(); return Page(); }
}

public class UserForm
{
    [BindProperty] public string Username { get; set; } = string.Empty;
    [BindProperty] public string Password { get; set; } = string.Empty;
    [BindProperty] public string FullName { get; set; } = string.Empty;
    [BindProperty] public string? Department { get; set; }
    [BindProperty] public string? EmployeeId { get; set; }
    [BindProperty] public string Role { get; set; } = UserRoles.User;
    public UserAccount ToAccount() => new() { Username = Username.Trim(), FullName = FullName.Trim(), Department = Department?.Trim(), EmployeeId = EmployeeId?.Trim(), Role = Role };
}
