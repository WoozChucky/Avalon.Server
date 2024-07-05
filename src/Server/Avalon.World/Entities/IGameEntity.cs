namespace Avalon.World.Entities;

/**
 * Represents an entity in the game world.
 *
 * @typeparam TKey The type of the entity's unique identifier.
 */
public interface IGameEntity<TKey>
{
    public TKey Id { get; set; }
}
