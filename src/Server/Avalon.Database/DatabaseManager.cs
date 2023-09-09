using System;
using System.Linq;
using System.Reflection;
using Avalon.Database.Auth;
using Avalon.Database.Characters;
using Avalon.Database.World;
using Avalon.Database.World.Model;

// docker run --detach --name avalon-mariadb -p 3306:3306 --env MARIADB_USER=admin --env MARIADB_PASSWORD=123 --env MARIADB_ROOT_PASSWORD=123  mariadb:latest

namespace Avalon.Database;

public interface IDatabaseManager
{
    IAuthDatabase Auth { get; }
        
    ICharactersDatabase Characters { get; }
        
    IWorldDatabase World { get; }
}

public class DatabaseManager : IDatabaseManager
{
    public IAuthDatabase Auth { get; }
    public ICharactersDatabase Characters { get; }
    public IWorldDatabase World { get; }
    
    public DatabaseManager()
    {
        Auth = new AuthDatabase();
        Characters = new CharactersDatabase();
        World = new WorldDatabase();
        
        RegisterMappings();
    }
    
    public static void RegisterMappings()
    {
        Dapper.SqlMapper.SetTypeMap(typeof(Account), new Dapper.CustomPropertyTypeMap(typeof(Account), 
            (type, columnName) => type
                .GetProperties()
                .FirstOrDefault(prop => GetDescriptionFromAttribute(prop) == columnName.ToLower()))
        );
        
        Dapper.SqlMapper.SetTypeMap(typeof(Character), new Dapper.CustomPropertyTypeMap(typeof(Character), 
            (type, columnName) => type
                .GetProperties()
                .FirstOrDefault(prop => GetDescriptionFromAttribute(prop) == columnName.ToLower()))
        );
        
        Dapper.SqlMapper.SetTypeMap(typeof(Map), new Dapper.CustomPropertyTypeMap(typeof(Map), 
            (type, columnName) => type
                .GetProperties()
                .FirstOrDefault(prop => GetDescriptionFromAttribute(prop) == columnName.ToLower()))
        );
        
        Dapper.SqlMapper.SetTypeMap(typeof(CreatureTemplate), new Dapper.CustomPropertyTypeMap(typeof(CreatureTemplate), 
            (type, columnName) => type
                .GetProperties()
                .FirstOrDefault(prop => GetDescriptionFromAttribute(prop) == columnName.ToLower()))
        );
    }

    private static string? GetDescriptionFromAttribute(MemberInfo? member)
    {
        if (member == null) return null;

        var attrib = (Column)Attribute.GetCustomAttribute(member, typeof(Column), false);
        return (attrib?.Name ?? member.Name).ToLower();
    }
}
