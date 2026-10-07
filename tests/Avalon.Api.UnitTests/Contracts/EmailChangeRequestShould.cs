using System.ComponentModel.DataAnnotations;
using Avalon.Api.Contract;
using Avalon.Api.Testing;
using Xunit;

namespace Avalon.Api.UnitTests.Contracts;

/// <summary>
/// #503 follow-up: the new address of an email change is held to the rule registration uses, an
/// ASCII address, so .NET and Postgres lower-case it alike.
/// </summary>
public class EmailChangeRequestShould
{
    private static bool IsValid(string newEmail)
    {
        var request = new AccountEmailChangeRequest { NewEmail = newEmail, CurrentPassword = TestPasswords.Valid };
        return Validator.TryValidateObject(request, new ValidationContext(request), new List<ValidationResult>(),
            validateAllProperties: true);
    }

    [Theory]
    [InlineData("x", false)]
    [InlineData("üser@avalon.monster", false)]
    [InlineData("player@exämple.com", false)]
    [InlineData("player@avalon.monster", true)]
    [InlineData("Mixed.Case+tag@Avalon.Monster", true)]
    public void Accept_only_an_ascii_address(string newEmail, bool valid) => Assert.Equal(valid, IsValid(newEmail));
}
