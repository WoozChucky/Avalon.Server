using Avalon.Common;

namespace Avalon.Game.Entities;

public interface IEntity : IHideObjectMembers
{
    Guid Id { get; }
    
    void Update(TimeSpan deltaTime);
}