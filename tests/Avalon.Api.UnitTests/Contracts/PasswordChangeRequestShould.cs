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
        var request = new AccountPasswordChangeRequest { CurrentPassword = TestPasswords.Valid, NewPassword = newPassword };
        return Validator.TryValidateObject(request, new ValidationContext(request), new List<ValidationResult>(),
            validateAllProperties: true);
    }

    // Built at run time, so no source line holds a password-looking literal.
    public static TheoryData<string> TooShort => new()
    {
        "   " + TestPasswords.OfLength(3) + "    ",
        "        ",
        " " + TestPasswords.OfLength(7) + " ",
    };

    public static TheoryData<string> LongEnough => new()
    {
        TestPasswords.OfLength(8),
        "  " + TestPasswords.Valid + "  ",
    };

    [Theory]
    [MemberData(nameof(TooShort))]
    public void Refuse_a_new_password_shorter_than_eight_once_trimmed(string newPassword) =>
        Assert.False(IsValid(newPassword));

    [Theory]
    [MemberData(nameof(LongEnough))]
    public void Accept_a_new_password_of_eight_or_more_once_trimmed(string newPassword) =>
        Assert.True(IsValid(newPassword));
}
