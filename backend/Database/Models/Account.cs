namespace NzbWebDAV.Database.Models;

public class Account
{
    public AccountType Type { get; init; }
    public string Username { get; init; } = null!;
    public string PasswordHash { get; init; } = null!;
    public string RandomSalt { get; init; } = null!;

    public enum AccountType
    {
        Admin = 1,
        WebDav = 2,
    }
}