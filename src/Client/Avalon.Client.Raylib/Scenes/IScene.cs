// Licensed to the Avalon MMORPG Game under one or more agreements.
// Avalon MMORPG Game licenses this file to you under the MIT license.

namespace Avalon.Client.Scenes;

public interface IScene
{
    void Setup();
    void Update();
    void Render();
    void Unload();
}
