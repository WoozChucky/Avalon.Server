using System;
using System.Collections.Concurrent;
using Avalon.Client.Managers;
using Avalon.Client.UI;
using Avalon.Client.UI.CharacterSelection;
using Avalon.Network.Packets.Character;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Steamworks.Data;
using Color = Microsoft.Xna.Framework.Color;

namespace Avalon.Client.Scenes;

public class CharacterSelectionScene : Scene
{
    private SpriteFont _titleFont;
    private string _title;
    private Vector2 _titlePosition;
    private Vector2 _shadowOffset;
    
    private ConcurrentBag<CharacterSelectFrame> _characterSelectFrames; //TODO: ConcurrentBag iteration is doing a lot of allocations, maybe use a different collection?
    
    private Cursor _cursor;
    private ButtonComponent _createButton;
    private TextInputComponent _characterNameInput;
    
    private volatile bool _gotCharacterList;
    private volatile bool _characterSelected;
    private volatile bool _canCreateCharacter;

    public CharacterSelectionScene(SceneManager sceneManager) : base(sceneManager)
    {
        
    }

    public override async void Load()
    {
        Globals.Tcp.CharacterList += OnCharacterListReceived;
        Globals.Tcp.CharacterSelected += OnCharacterSelected;
        Globals.Tcp.CharacterCreated += OnCharacterCreated;
        Globals.Tcp.CharacterDeleted += OnCharacterDeleted;
        
        _gotCharacterList = false;
        _characterSelected = false;
        _canCreateCharacter = false;
        _characterSelectFrames = new ConcurrentBag<CharacterSelectFrame>();
        
        _titleFont = Globals.Content.Load<SpriteFont>("Fonts/BigTitle");
        _title = "Character Selection";
        _titlePosition = new Vector2(
            Globals.WindowSize.X / 2f - _titleFont.MeasureString(_title).X / 2f,
            30
        );
        _shadowOffset = _titlePosition + new Vector2(2, 2);
        
        _createButton = new ButtonComponent(
            Globals.Content.Load<Texture2D>("Images/Icons/Plus1"),
            new Vector2(Globals.WindowSize.X - 150, Globals.WindowSize.Y - 95),
            null,
            null,
            false,
            Globals.Content.Load<Texture2D>("Images/Icons/Plus")
        );
        _createButton.Clicked += OnCreateButtonClicked;
        
        _characterNameInput = new TextInputComponent(
            new Vector2(Globals.WindowSize.X / 2f - 125f, Globals.WindowSize.Y - 100),
            new Vector2(250, 40),
            2,
            Globals.Content.Load<SpriteFont>("Fonts/ArialSmall")
        )
        {
            AllowAlphabetic = true,
        };
        _characterNameInput.OnPressedEnter += OnCreateButtonClicked;
        _characterNameInput.OnTextChanged += OnCharacterNameValidation;
        
        _cursor = new Cursor(Globals.Content.Load<Texture2D>("Images/Icons/Mouse"), false);
        
        await Globals.Tcp.SendCharacterListPacket(Globals.AccountId);
    }

    public override void Unload()
    {
        Globals.Tcp.CharacterList -= OnCharacterListReceived;
        Globals.Tcp.CharacterSelected -= OnCharacterSelected;
        Globals.Tcp.CharacterCreated -= OnCharacterCreated;
        Globals.Tcp.CharacterDeleted -= OnCharacterDeleted;

        if (_createButton != null)
        {
            _createButton.Clicked -= OnCreateButtonClicked;
        }
        if (_characterNameInput != null)
        {
            _characterNameInput.OnPressedEnter -= OnCreateButtonClicked;
            _characterNameInput.OnTextChanged -= OnCharacterNameValidation;
        }
    }

    public override void Update(GameTime gameTime)
    {
        var deltaTime = (float)gameTime.ElapsedGameTime.TotalSeconds;

        if (_characterSelected)
        {
            _characterSelected = false;
            SceneManager.LoadScene(nameof(TutorialScene));
        }
        
        if (_gotCharacterList)
        {
            foreach (var frame in _characterSelectFrames)
            {
                frame.Update(deltaTime);
            }

            if (_canCreateCharacter)
            {
                _characterNameInput.Update(deltaTime);
                _createButton.Update();
            }
        }
        
        _cursor?.Update(deltaTime);
    }

    public override void Draw(SpriteBatch spriteBatch)
    {
        spriteBatch.GraphicsDevice.Clear(Color.Coral);
        spriteBatch.Begin(blendState: BlendState.AlphaBlend);
        
        // Draw title
        spriteBatch.DrawString(_titleFont, _title, _shadowOffset, Color.Black);
        spriteBatch.DrawString(_titleFont, _title, _titlePosition, Color.White);

        if (_gotCharacterList)
        {
            // Draw character select frames
            foreach (var frame in _characterSelectFrames)
            {
                frame.Draw(spriteBatch);
            }
            
            if (_canCreateCharacter)
            {
                _createButton.Draw(spriteBatch);
                _characterNameInput.Draw(spriteBatch);
            }
        }
        
        _cursor?.Draw(spriteBatch);
        
        spriteBatch.End();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _cursor?.Dispose();
            _createButton?.Dispose();
            _characterNameInput?.Dispose();
            if (_characterSelectFrames != null)
            {
                foreach (var frame in _characterSelectFrames)
                {
                    frame?.Dispose();
                }
            }
            
            Console.WriteLine("Character selection scene disposed");
        }
    }

    #region Character Server Events

    private async void OnCharacterCreated(object sender, SCharacterCreatedPacket packet)
    {
        if (packet.Result == SCharacterCreateResult.Success)
        {
            var ach = new Achievement("ACH_FIRST_CHARACTER");
            ach.Trigger();
            _gotCharacterList = false;
            await Globals.Tcp.SendCharacterListPacket(Globals.AccountId);    
        }
        else
        {
            Console.WriteLine("Failed to create character " + packet.Result);
        }
    }

    private async void OnCharacterDeleted(object sender, SCharacterDeletedPacket packet)
    {
        _gotCharacterList = false;
        _characterSelected = false;
        await Globals.Tcp.SendCharacterListPacket(Globals.AccountId);
    }
    
    private void OnCharacterSelected(object sender, SCharacterSelectedPacket packet)
    {
        _gotCharacterList = false;
        Globals.CharacterId = packet.Character.CharacterId;
        Globals.CharacterName = packet.Character.Name;
        Globals.CharacterRadius = packet.Character.Radius;
        Globals.MapInfo = packet.Map;
        Globals.StartPosition = new Vector2(packet.Character.X, packet.Character.Y);
        Globals.Tcp.CharacterId = packet.Character.CharacterId;
        _characterSelected = true;
    }
    
    private void OnCharacterListReceived(object sender, SCharacterListPacket packet)
    {
        Console.WriteLine("Received character list packet");
        Console.WriteLine($"Character count: {packet.CharacterCount}");

        var framePosition = new Vector2(150, 100);
        
        _characterSelectFrames.Clear();

        var currentIdx = 0;
        const int switchIdx = 2;

        if (packet.Characters != null)
        {
            foreach (var character in packet.Characters)
            {
                var frame = new CharacterSelectFrame(framePosition, character);
                frame.Selected += OnCharacterSelectedFrame;
                frame.Deleted += OnCharacterDeletedFrame;
                _characterSelectFrames.Add(frame);
                framePosition.Y += 120;
                if (currentIdx == switchIdx)
                {
                    framePosition.X = 450;
                    framePosition.Y = 100;
                }
                currentIdx++;
            }
        }

        _gotCharacterList = true;
        _canCreateCharacter = packet.CharacterCount < packet.MaxCharacterCount;
    }
    
    #endregion
    
    #region Create Character Events

    private bool OnCharacterNameValidation(string text)
    {
        if (string.IsNullOrEmpty(text) || text.Length < 3 || text.Length > 12)
        {
            return false;
        }
        return true;
    }

    private async void OnCreateButtonClicked(object sender)
    {
        //TODO: While classes are not implemented, we will always create a character of class 1
        var name = _characterNameInput.Text;
        
        if (!OnCharacterNameValidation(name))
        {
            return;
        }
        
        var @class = 1;
        await Globals.Tcp.SendCharacterCreatePacket(Globals.AccountId, name, @class);
    }

    #endregion
    
    #region Selection Frame Events

    private async void OnCharacterSelectedFrame(CharacterInfo charInfo)
    {
        Console.WriteLine($"Selected character {charInfo.Name}");
        await Globals.Tcp.SendCharacterSelectedPacket(charInfo.CharacterId);
    }
    
    private async void OnCharacterDeletedFrame(CharacterInfo charInfo)
    {
        Console.WriteLine($"Deleted character {charInfo.Name}");
        await Globals.Tcp.SendCharacterDeletePacket(charInfo.CharacterId);
        _gotCharacterList = false;
    }
    
    #endregion
    
}
