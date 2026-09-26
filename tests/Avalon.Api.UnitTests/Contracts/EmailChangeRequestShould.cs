using System.ComponentModel.DataAnnotations;
using Avalon.Api.Contract;
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
    [InlineData("x")]
    [InlineData("üser@avalon.monster")]
    [InlineData("player@exämple.com")]
    public void Refuse_a_new_email_that_is_not_an_ascii_address(string newEmail) => Assert.False(IsValid(newEmail));

    [Theory]
    [InlineData("player@avalon.monster")]
    [InlineData("Mixed.Case+tag@Avalon.Monster")]
    public void Accept_an_ascii_address(string newEmail) => Assert.True(IsValid(newEmail));
}
