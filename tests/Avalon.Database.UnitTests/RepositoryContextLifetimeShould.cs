using System.Reflection;
using Avalon.Domain;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Avalon.Database.UnitTests;

/// <summary>
/// A repository context lives exactly one method call. These pin both halves of that: the
/// structural half (nothing holds one) and the behavioural half (every call creates and
/// disposes its own).
/// </summary>
public class RepositoryContextLifetimeShould
{
    private static readonly Assembly[] DatabaseAssemblies =
    [
        typeof(Auth.Repositories.AccountRepository).Assembly,
        typeof(Character.Repositories.CharacterRepository).Assembly,
        typeof(World.Repositories.CreatureTemplateRepository).Assembly,
        typeof(EntityFrameworkRepository<,,>).Assembly,
    ];

    public static TheoryData<Type> RepositoryTypes()
    {
        TheoryData<Type> data = new();
        foreach (Type type in DatabaseAssemblies
                     .SelectMany(assembly => assembly.GetTypes())
                     .Where(type => type is { IsClass: true, IsInterface: false } &&
                                    type.Name.EndsWith("Repository", StringComparison.Ordinal))
                     .OrderBy(type => type.FullName, StringComparer.Ordinal))
        {
            data.Add(type);
        }

        return data;
    }

    [Fact]
    public void Find_the_repositories_it_claims_to_check()
    {
        // Guards the theories below: a filter that matched nothing would pass every one of them.
        Assert.True(RepositoryTypes().Count > 20);
    }

    [Theory]
    [MemberData(nameof(RepositoryTypes))]
    public void Not_hold_a_context_in_a_field(Type repositoryType)
    {
        for (Type? type = repositoryType; type is not null && type != typeof(object); type = type.BaseType)
        {
            foreach (FieldInfo field in type.GetFields(BindingFlags.Instance | BindingFlags.Public |
                                                       BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                Assert.False(typeof(DbContext).IsAssignableFrom(field.FieldType),
                    $"{repositoryType.Name} holds a DbContext in field '{field.Name}'. A held context " +
                    "outlives the call that made it and is shared by every caller.");
            }

            foreach (PropertyInfo property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public |
                                                                 BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                Assert.False(typeof(DbContext).IsAssignableFrom(property.PropertyType),
                    $"{repositoryType.Name} exposes a DbContext through property '{property.Name}'.");
            }
        }
    }

    [Theory]
    [MemberData(nameof(RepositoryTypes))]
    public void Not_take_a_context_as_a_constructor_parameter(Type repositoryType)
    {
        foreach (ConstructorInfo constructor in repositoryType.GetConstructors(
                     BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
        {
            foreach (ParameterInfo parameter in constructor.GetParameters())
            {
                Assert.False(typeof(DbContext).IsAssignableFrom(parameter.ParameterType),
                    $"{repositoryType.Name} is injected a DbContext as '{parameter.Name}'. It takes an " +
                    "IDbContextFactory and creates one per call.");
            }
        }
    }

    [Fact]
    public async Task Create_and_dispose_a_context_for_every_call()
    {
        CountingContextFactory factory = new();
        ProbeRepository repository = new(factory);

        // The probe context has no provider, so the query fails — after the repository has already
        // asked for its context. What is under test is the count, not the query.
        await Assert.ThrowsAnyAsync<Exception>(() => repository.FindAllAsync());
        await Assert.ThrowsAnyAsync<Exception>(() => repository.FindAllAsync());
        await Assert.ThrowsAnyAsync<Exception>(() => repository.CreateAsync(new ProbeEntity()));

        Assert.Equal(3, factory.Created);
        Assert.Equal(3, factory.Disposed);
    }

    private sealed class CountingContextFactory : IDbContextFactory<ProbeContext>
    {
        public int Created { get; private set; }
        public int Disposed { get; private set; }

        public ProbeContext CreateDbContext()
        {
            Created++;
            return new ProbeContext(() => Disposed++);
        }

        public Task<ProbeContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateDbContext());
    }

    private sealed class ProbeContext(Action onDispose) : DbContext
    {
        public override void Dispose()
        {
            onDispose();
            base.Dispose();
        }

        public override ValueTask DisposeAsync()
        {
            onDispose();
            return base.DisposeAsync();
        }
    }

    private sealed class ProbeEntity : IDbEntity<int>
    {
        public int Id { get; set; }
    }

    private sealed class ProbeRepository(IDbContextFactory<ProbeContext> contextFactory)
        : EntityFrameworkRepository<ProbeEntity, int, ProbeContext>(contextFactory);
}
