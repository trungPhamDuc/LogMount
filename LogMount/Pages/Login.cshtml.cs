using System.Security.Claims;
using LogMount.Data;
using LogMount.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace LogMount.Pages;

[AllowAnonymous]
public class LoginModel : PageModel
{
    private readonly LogMountDbContext _db;
    private readonly PasswordHasher<UserAccount> _passwordHasher = new();

    public LoginModel(LogMountDbContext db) => _db = db;

    [BindProperty] public string Username { get; set; } = string.Empty;
    [BindProperty] public string Password { get; set; } = string.Empty;
    [BindProperty(SupportsGet = true)] public string? ReturnUrl { get; set; }
    public string? ErrorMessage { get; set; }

    public async Task<IActionResult> OnGetAsync(bool denied = false)
    {
        if (User.Identity?.IsAuthenticated == true)
            return LocalRedirect(User.IsInRole(UserRoles.Admin) ? "/Users" : "/");

        if (denied) ErrorMessage = "Bạn không có quyền thực hiện thao tác này.";
        await Task.CompletedTask;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var username = Username.Trim();
        var account = await _db.UserAccounts.SingleOrDefaultAsync(x => x.Username == username);
        if (account is null || _passwordHasher.VerifyHashedPassword(account, account.PasswordHash, Password) == PasswordVerificationResult.Failed)
        {
            ErrorMessage = "Tên đăng nhập hoặc mật khẩu không đúng.";
            return Page();
        }

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, account.Id.ToString()),
            new Claim(ClaimTypes.Name, account.Username),
            new Claim(ClaimTypes.GivenName, account.FullName),
            new Claim(ClaimTypes.Role, account.Role)
        };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme));
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal);

        if (!string.IsNullOrWhiteSpace(ReturnUrl) && Url.IsLocalUrl(ReturnUrl))
            return LocalRedirect(ReturnUrl);
        return LocalRedirect(account.Role == UserRoles.Admin ? "/Users" : "/");
    }

    public async Task<IActionResult> OnPostLogoutAsync()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToPage();
    }
}
