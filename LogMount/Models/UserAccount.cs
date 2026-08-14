using System.ComponentModel.DataAnnotations;

namespace LogMount.Models;

public class UserAccount
{
    public int Id { get; set; }

    [Required, MaxLength(100)]
    public string Username { get; set; } = string.Empty;

    [Required, MaxLength(500)]
    public string PasswordHash { get; set; } = string.Empty;

    [Required, MaxLength(200)]
    public string FullName { get; set; } = string.Empty;

    [MaxLength(200)]
    public string? Department { get; set; }

    [MaxLength(100)]
    public string? EmployeeId { get; set; }

    [Required, MaxLength(20)]
    public string Role { get; set; } = UserRoles.User;
}

public static class UserRoles
{
    public const string User = "User";
    public const string Staff = "Staff";
    public const string Admin = "Admin";
    public const string StaffOrAdmin = Staff + "," + Admin;
}
