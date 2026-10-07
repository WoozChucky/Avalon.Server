using System.Text.Json.Serialization;

namespace Avalon.Api.Contract;

/// <summary>The pool an ability's cost is spent from; None when it costs nothing (#652).</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum PowerType
{
    None = 0,
    Mana = 1,
    Fury = 2,
    Energy = 3,
}
