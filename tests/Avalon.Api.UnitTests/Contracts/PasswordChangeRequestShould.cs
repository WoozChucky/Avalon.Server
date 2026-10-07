using System.ComponentModel.DataAnnotations;
using Avalon.Api.Contract;
using Avalon.Api.Testing;
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
        var request = new AccountPasswordChangeRequest { CurrentPassword = TestPasswords.Valid, NewPassword = newPassword };
        return Validator.TryValidateObject(request, new ValidationContext(request), new List<ValidationResult>(),
            validateAllProperties: true);
    }

    // Built at run time, so no source line holds a password-looking literal.
    public static TheoryData<string, bool> Passwords => new()
    {
        { "   " + TestPasswords.OfLength(3) + "    ", false },
        { "        ", false },
        { " " + TestPasswords.OfLength(7) + " ", false },
        { TestPasswords.OfLength(8), true },
        { "  " + TestPasswords.Valid + "  ", true },
    };

    [Theory]
    [MemberData(nameof(Passwords))]
    public void Accept_a_new_password_only_of_eight_or_more_once_trimmed(string newPassword, bool valid) =>
        Assert.Equal(valid, IsValid(newPassword));
}
