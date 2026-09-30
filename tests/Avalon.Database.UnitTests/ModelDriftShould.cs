using Avalon.Database.Character;
using Avalon.Database.World;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Avalon.Database.UnitTests;

/// <summary>
/// HasData is what migrations are generated from, and the balance simulator reads it as the seed. A HasData edit
/// without a migration would make the simulator (and every fresh database) disagree with the databases the
/// migrations built. Both contexts are built with a connection string that points nowhere; nothing connects.
/// </summary>
public class ModelDriftShould
{
    private const string Nowhere = "Host=127.0.0.1;Port=1;Database=design_time_only";

    [Fact]
    public void Have_no_world_model_change_without_a_migration()
    {
        using var context = new WorldDbContext(new DbContextOptionsBuilder<WorldDbContext>().UseNpgsql(Nowhere).Options);

        Assert.False(context.Database.HasPendingModelChanges(),
            "WorldDbContext has changes no migration captures: add one with dotnet ef migrations add (see CLAUDE.md).");
    }

    [Fact]
    public void Have_no_characters_model_change_without_a_migration()
    {
        using var context = new CharacterDbContext(new DbContextOptionsBuilder<CharacterDbContext>().UseNpgsql(Nowhere).Options);

        Assert.False(context.Database.HasPendingModelChanges(),
            "CharacterDbContext has changes no migration captures: add one with dotnet ef migrations add (see CLAUDE.md).");
    }
}
