using System;

namespace Avalon.Domain.Attributes;

[AttributeUsage(AttributeTargets.Property)]
public class Column(string name) : Attribute
{
    public string Name { get; } = name;
}
