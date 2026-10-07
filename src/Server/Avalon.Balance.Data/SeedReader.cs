using Avalon.Database.World;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Avalon.Balance.Data;

/// <summary>
/// Reads HasData rows from WorldDbContext's design-time model. The runtime model does not keep seed data; the
/// design-time one does. The connection string points nowhere and nothing connects.
/// </summary>
public sealed class SeedReader : IDisposable
{
    public const string Nowhere = "Host=127.0.0.1;Port=1;Database=design_time_only";

    private readonly WorldDbContext _context;
    private readonly IModel _model;

    public SeedReader()
    {
        _context = new WorldDbContext(new DbContextOptionsBuilder<WorldDbContext>().UseNpgsql(Nowhere).Options);
        _model = _context.GetService<IDesignTimeModel>().Model;
    }

    /// <summary>
    /// Every seeded row of <typeparamref name="T" />, as entities; navigations and shadow properties are left unset.
    /// A seeded column with no writable property is refused rather than dropped.
    /// </summary>
    public List<T> Rows<T>() where T : class, new()
    {
        IEntityType type = _model.FindEntityType(typeof(T))
            ?? throw new InvalidOperationException($"{typeof(T).Name} is not in the World model");

        var rows = new List<T>();
        foreach (IDictionary<string, object?> seed in type.GetSeedData())
        {
            var row = new T();
            foreach (IProperty property in type.GetProperties())
            {
                if (property.IsShadowProperty() || !seed.TryGetValue(property.Name, out object? value))
                    continue;

                if (property.PropertyInfo is not { CanWrite: true } info)
                {
                    throw new InvalidDataException(
                        $"{typeof(T).Name}.{property.Name} is seeded but has no writable property to read it into.");
                }

                info.SetValue(row, value);
            }

            rows.Add(row);
        }

        return rows;
    }

    public void Dispose() => _context.Dispose();
}
