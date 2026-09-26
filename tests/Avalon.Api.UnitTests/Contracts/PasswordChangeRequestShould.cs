using System.ComponentModel.DataAnnotations;
using Avalon.Api.Contract;
using Xunit;

namespace Avalon.Api.UnitTests.Contracts;

/// <summary>
/// #478 review: <c>[MinLength(8)]</c> measured the new password before it was trimmed, and the hash
/// is made from the trimmed one, so "   abc    " passed as ten characters and was stored as three.
/// </summary>
public class PasswordChangeRequestShould
{
    private static bool IsValid(string newPassword)
    {
        var request = new AccountPasswordChangeRequest { CurrentPassword = "current", NewPassword = newPassword };
        return Validator.TryValidateObject(request, new ValidationContext(request), new List<ValidationResult>(),
            validateAllProperties: true);
    }

    [Theory]
    [InlineData("   abc    ")]
    [InlineData("        ")]
    [InlineData(" 1234567 ")]
    public void Refuse_a_new_password_shorter_than_eight_once_trimmed(string newPassword) =>
        Assert.False(IsValid(newPassword));

    [Theory]
    [InlineData("12345678")]
    [InlineData("  a strong one  ")]
    public void Accept_a_new_password_of_eight_or_more_once_trimmed(string newPassword) =>
        Assert.True(IsValid(newPassword));
}
