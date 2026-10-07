using System.Reflection;

namespace Avalon.Balance.Core;

/// <summary>Lists every value <see cref="Overrides" /> accepts, from the same table map, for an editor to offer.</summary>
public static class Catalog
{
    public static IReadOnlyList<Tunable> Describe(SeedTables seed)
    {
        var tunables = new List<Tunable>();
        foreach ((string tableName, Overrides.Table table) in Overrides.s_tables)
        {
            PropertyInfo[] columns = table.Columns().ToArray();
            PropertyInfo? name = table.RowType.GetProperty("Name", BindingFlags.Public | BindingFlags.Instance);

            foreach ((string rowKey, object row) in table.Rows(seed))
            {
                string rowDisplay = name?.PropertyType == typeof(string) && name.GetValue(row) is string text && text.Length > 0
                    ? text
                    : rowKey;

                foreach (PropertyInfo column in columns)
                {
                    tunables.Add(new Tunable(
                        table.Keyless ? $"{tableName}.{column.Name}" : $"{tableName}.{rowKey}.{column.Name}",
                        tableName,
                        table.Keyless ? null : rowKey,
                        column.Name,
                        TypeName(column.PropertyType),
                        Overrides.Show(column.GetValue(row)),
                        table.Keyless ? column.Name : rowDisplay));
                }
            }
        }

        return tunables;
    }

    private static string TypeName(Type type)
    {
        Type target = Nullable.GetUnderlyingType(type) ?? type;
        return target.IsEnum ? target.Name : target.Name switch
        {
            "Int32" => "int",
            "UInt32" => "uint",
            "UInt16" => "ushort",
            "Single" => "float",
            "Double" => "double",
            "Boolean" => "bool",
            "String" => "string",
            "Byte" => "byte",
            "Int16" => "short",
            "Int64" => "long",
            "UInt64" => "ulong",
            _ => target.Name,
        };
    }
}
