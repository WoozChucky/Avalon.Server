using System;
using Microsoft.Xna.Framework.Graphics;

namespace Avalon.Client.UI;

public interface IGameComponent : IDisposable
{
    public void Update(float deltaTime);
    public void Draw(SpriteBatch spriteBatch);
    public void ToggleVisibility();
}
