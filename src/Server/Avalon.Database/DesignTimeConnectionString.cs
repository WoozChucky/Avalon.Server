using System;
using System.Reflection;
using Microsoft.Extensions.Configuration;

namespace Avalon.Database;

/// <summary>
/// Where the World and Character design-time factories (what dotnet ef builds a context with) read
/// their connection string (#523): the database project's user-secrets, then the environment, and
/// nothing else. They used to read appsettings.Design.json and appsettings.json from the working
/// directory, which in practice was the api's; the api lists Database:Worlds now and has no single
/// World/Characters pair, and falling back to a local file is what once pointed a command at a real
/// database. Without the string the factory refuses rather than guess.
/// </summary>
public static class DesignTimeConnectionString
{
    public static IConfiguration Sources(Assembly databaseProject) =>
        new ConfigurationBuilder()
            .AddUserSecrets(databaseProject, optional: true)
            .AddEnvironmentVariables()
            .Build();

    /// <summary>
    /// Database:<paramref name="database"/>:ConnectionString, or the refusal naming the variable.
    /// A blank value is refused too.
    /// </summary>
    public static string Require(IConfiguration configuration, string database)
    {
        string? value = configuration[$"Database:{database}:ConnectionString"];
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException(
                $"set Database__{database}__ConnectionString (or user-secrets) to run dotnet ef against a database");
        return value;
    }
}
