namespace Avalon.Domain;

public interface IDbEntity<TKey>
{
    TKey Id { get; set; }
}
