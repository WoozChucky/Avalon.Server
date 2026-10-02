using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Avalon.Database.World;
using Avalon.Domain.World;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Avalon.Api.Templates;

/// <summary>
/// The version of a template row: a lowercase hex SHA-256 of its stored values, which a save compares to refuse a
/// row that changed since the editor read it. "Stored" is decided by the EF model, not by the C# type, so a column
/// added later is covered with no change here, and a computed or unmapped property (Stackable, BodyRemoveTimer) is
/// left out. The values are the ones written to the database (so <c>AllowedClasses</c> is its CSV), as canonical
/// JSON: properties in ordinal name order, enums as names.
/// </summary>
public static class TemplateVersion
{
    private static readonly Lazy<IModel> Model = new(BuildModel);
    private static readonly ConcurrentDictionary<Type, IReadOnlyList<IProperty>> Columns = new();

    private static readonly JsonSerializerOptions Json = new()
    {
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string Of(ItemTemplate template) => Hash(template);

    public static string Of(AbilityTemplate template) => Hash(template);

    public static string Of(CreatureTemplate template) => Hash(template);

    /// <summary>The names of the columns the version covers for an entity type.</summary>
    public static IReadOnlyList<string> ColumnsOf(Type entityType) =>
        ColumnsFor(entityType).Select(p => p.Name).ToList();

    private static string Hash(object entity)
    {
        SortedDictionary<string, object?> values = new(StringComparer.Ordinal);
        foreach (IProperty property in ColumnsFor(entity.GetType()))
        {
            object? clr = property.GetGetter().GetClrValue(entity);
            values[property.Name] = clr is null ? null : Stored(property, clr);
        }

        byte[] json = JsonSerializer.SerializeToUtf8Bytes(values, Json);
        return Convert.ToHexStringLower(SHA256.HashData(json));
    }

    private static object Stored(IProperty property, object clr)
    {
        // The provider value (the CSV of AllowedClasses, the number inside an id); enums with no converter stay
        // enums and serialize as names.
        object? stored = property.GetValueConverter()?.ConvertToProvider(clr) ?? clr;
        return stored;
    }

    private static IReadOnlyList<IProperty> ColumnsFor(Type entityType) =>
        Columns.GetOrAdd(entityType, type =>
        {
            IEntityType entity = Model.Value.FindEntityType(type)
                ?? throw new InvalidOperationException($"{type.Name} is not in the world model.");
            return entity.GetProperties().Where(p => !p.IsShadowProperty()).OrderBy(p => p.Name, StringComparer.Ordinal)
                .ToList();
        });

    /// <summary>
    /// The world model, built once. It never connects: the context is only asked for its model, so the connection
    /// string is a placeholder.
    /// </summary>
    private static IModel BuildModel()
    {
        DbContextOptions<WorldDbContext> options = new DbContextOptionsBuilder<WorldDbContext>()
            .UseNpgsql("Host=model-only")
            .Options;
        // Sync on purpose: this runs inside a Lazy factory, and nothing is opened that disposal would wait on.
#pragma warning disable MA0045
        using WorldDbContext context = new(options);
#pragma warning restore MA0045
        return context.Model;
    }
}
