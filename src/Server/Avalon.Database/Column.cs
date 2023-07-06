using System;

namespace Avalon.Database;

[AttributeUsage(AttributeTargets.Property)]
public class Column : Attribute
{
    public string Name { get; }

    public Column(string name)
    {
        Name = name;
    }
}
