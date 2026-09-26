using System.Text;
using Avalon.Database.Auth.Repositories;
using Avalon.Domain.Auth;
using Microsoft.EntityFrameworkCore;

namespace Avalon.Server.Auth.UnitTests.Services;

/// <summary>
/// #487 review: the unique index is on the normalised username only while every writer normalises
/// it. A check constraint (<c>"Username" = upper(trim("Username"))</c>) makes the database refuse
/// a row that is not, whoever writes it.
/// </summary>
public sealed class UsernameConstraintShould : IDisposable
{
    private readonly AuthSqlite _database = new();

    public void Dispose() => _database.Dispose();

    private Task<Account> CreateAsync(string username) => new AccountRepository(_database).CreateAsync(new Account
    {
        Username = username,
        Email = "someone@example.com",
        Salt = [1],
        Verifier = Encoding.UTF8.GetBytes("unused"),
        JoinDate = DateTime.UtcNow,
        LastLogin = DateTime.UtcNow,
    });

    [Theory]
    [InlineData("lower")]
    [InlineData("Mixed")]
    [InlineData(" PADDED")]
    [InlineData("PADDED ")]
    public async Task Refuse_a_username_that_is_not_stored_normalised(string username)
    {
        await Assert.ThrowsAsync<DbUpdateException>(() => CreateAsync(username));
    }

    [Fact]
    public async Task Accept_a_normalised_username()
    {
        Account created = await CreateAsync("NORMALISED");

        Assert.Equal("NORMALISED", created.Username);
    }
}
