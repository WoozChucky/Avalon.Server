using System.ComponentModel.DataAnnotations;
using Avalon.Api.Contract;
using Avalon.Api.Exceptions;
using Avalon.Api.Services;
using Avalon.Common.ValueObjects;
using Avalon.Database.Character.Repositories;
using Avalon.Database.World.Repositories;
using Avalon.Domain.Characters;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Avalon.Api.UnitTests.Services;

/// <summary>
/// A rename through PATCH /world/{worldId}/character/{id} follows the rule a create does (#757): 3 to 12 ASCII
/// letters, stored first letter upper-case and the rest lower-case, one name per world whatever its case.
/// </summary>
public class CharacterRenameShould
{
    private static readonly CharacterId Self = new(1);
    private static readonly CharacterId Other = new(2);

    private readonly ICharacterRepository _characters = Substitute.For<ICharacterRepository>();
    private readonly CharacterService _service;

    public CharacterRenameShould()
    {
        _characters.UpdateAsync(Arg.Any<Character>(), Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<Character>());
        _service = new CharacterService(_characters, Substitute.For<ICharacterInventoryRepository>(),
            Substitute.For<IItemInstanceRepository>(), Substitute.For<ICharacterAbilityRepository>(),
            Substitute.For<IAbilityTemplateRepository>(), Substitute.For<IItemTemplateRepository>(),
            Substitute.For<ICharacterStatsRepository>(), Substitute.For<ICharacterQuestRepository>());
    }

    public static TheoryData<bool> BothPaths => new() { true, false };

    private Task Rename(bool cosmetic, Character character, string name) => cosmetic
        ? _service.UpdateCosmeticAsync(character, name)
        : _service.UpdateAnyAsync(character, new CharacterPatchDto { Name = name });

    private static Character Holder(CharacterId id, string name) => new() { Id = id, Name = name };

    [Theory]
    [InlineData("Bo")]
    [InlineData("Abcdefghijklm")]
    [InlineData("B0b")]
    [InlineData("Bo b")]
    [InlineData(" Bob")]
    [InlineData("Zoë")]
    [InlineData("Bıll")]
    [InlineData("")]
    public void Refuse_a_name_that_breaks_the_rule_as_a_validation_error(string name)
    {
        var dto = new CharacterPatchDto { Name = name };
        var results = new List<ValidationResult>();

        Assert.False(Validator.TryValidateObject(dto, new ValidationContext(dto), results, validateAllProperties: true));
        Assert.Equal(CharacterName.Requirement, Assert.Single(results).ErrorMessage);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("kAELA")]
    [InlineData("Abcdefghijkl")]
    public void Accept_no_name_or_one_that_follows_the_rule(string? name)
    {
        var dto = new CharacterPatchDto { Name = name };
        Assert.True(Validator.TryValidateObject(dto, new ValidationContext(dto), [], validateAllProperties: true));
    }

    [Theory]
    [MemberData(nameof(BothPaths))]
    public async Task Refuse_a_name_that_breaks_the_rule_in_the_service_too(bool cosmetic)
    {
        Character character = Holder(Self, "Kaela");

        BusinessException refused = await Assert.ThrowsAsync<BusinessException>(() => Rename(cosmetic, character, "B0b"));

        Assert.Equal(CharacterName.Requirement, refused.Message);
        Assert.Equal("Kaela", character.Name);
        await _characters.DidNotReceiveWithAnyArgs().UpdateAsync(default!, default);
    }

    [Theory]
    [MemberData(nameof(BothPaths))]
    public async Task Store_the_name_with_its_first_letter_upper_case_and_the_rest_lower_case(bool cosmetic)
    {
        Character character = Holder(Self, "Kaela");

        await Rename(cosmetic, character, "bORIN");

        Assert.Equal(("Borin", "BORIN"), (character.Name, character.NameKey));
        await _characters.Received(1).UpdateAsync(character, Arg.Any<CancellationToken>());
    }

    [Theory]
    [MemberData(nameof(BothPaths))]
    public async Task Refuse_a_name_another_character_holds_in_any_case(bool cosmetic)
    {
        Character character = Holder(Self, "Kaela");
        _characters.FindByNameAsync("borin", Arg.Any<CancellationToken>()).Returns(Holder(Other, "Borin"));

        BusinessException refused = await Assert.ThrowsAsync<BusinessException>(() => Rename(cosmetic, character, "borin"));

        Assert.Equal("Name already taken", refused.Message);
        Assert.Equal("Kaela", character.Name);
        await _characters.DidNotReceiveWithAnyArgs().UpdateAsync(default!, default);
    }

    [Theory]
    [MemberData(nameof(BothPaths))]
    public async Task Let_a_character_change_the_case_of_its_own_name(bool cosmetic)
    {
        Character character = Holder(Self, "kaela"); // stored before #757
        _characters.FindByNameAsync("KAELA", Arg.Any<CancellationToken>()).Returns(Holder(Self, "kaela"));

        await Rename(cosmetic, character, "KAELA");

        Assert.Equal("Kaela", character.Name);
    }

    /// <summary>
    /// The taken check and the write are not atomic: another character can take the name in between, and the unique
    /// index on NameKey refuses this write. The caller gets the check's answer, never a server error.
    /// </summary>
    [Theory]
    [MemberData(nameof(BothPaths))]
    public async Task Answer_name_already_taken_to_a_rename_that_loses_the_race_to_the_index(bool cosmetic)
    {
        Character character = Holder(Self, "Kaela");
        _characters.FindByNameAsync("Borin", Arg.Any<CancellationToken>())
            .Returns((Character?)null, Holder(Other, "Borin"));
        _characters.UpdateAsync(character, Arg.Any<CancellationToken>())
            .ThrowsAsync(new DbUpdateException("unique violation"));

        BusinessException refused = await Assert.ThrowsAsync<BusinessException>(() => Rename(cosmetic, character, "Borin"));

        Assert.Equal("Name already taken", refused.Message);
        Assert.IsType<DbUpdateException>(refused.InnerException);
    }

    [Theory]
    [MemberData(nameof(BothPaths))]
    public async Task Rethrow_a_failed_write_when_nobody_else_holds_the_name(bool cosmetic)
    {
        Character character = Holder(Self, "Kaela");
        _characters.UpdateAsync(character, Arg.Any<CancellationToken>())
            .ThrowsAsync(new DbUpdateException("something else"));

        await Assert.ThrowsAsync<DbUpdateException>(() => Rename(cosmetic, character, "Borin"));
    }
}
