namespace Avalon.World.Public.Characters;

public interface ICharacter : IGameEntity<ulong>
{
    string Name { get; set; }
    
    ushort Map { get; set; }
    ushort Level { get; set; }
}
