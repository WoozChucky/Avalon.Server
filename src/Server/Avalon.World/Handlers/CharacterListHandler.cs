using System.Diagnostics;
using Avalon.Common.Telemetry;
using Avalon.Database.Character.Repositories;
using Avalon.Domain.Characters;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Character;
using Microsoft.Extensions.Logging;

namespace Avalon.World.Handlers;

[PacketHandler(NetworkPacketType.CMSG_CHARACTER_LIST)]
public class CharacterListHandler(
    ILogger<CharacterListHandler> logger,
    ICharacterRepository characterRepository,
    IWorld world) : WorldPacketHandler<CCharacterListPacket>
{
    private Activity? _parentActivity;

    public override void Execute(WorldConnection connection, CCharacterListPacket packet)
    {
        using Activity? activity =
            DiagnosticsConfig.World.Source.StartActivity(nameof(CharacterListHandler), ActivityKind.Server);
        activity?.SetTag(nameof(connection.AccountId), connection.AccountId);

        if (connection.AccountId == null)
        {
            logger.LogWarning("Connection tried to request character list without being authenticated");
            activity?.AddEvent(new ActivityEvent("UnauthorizedRequestAttempt"));
            connection.Close();
            return;
        }

        if (connection.Character != null)
        {
            logger.LogWarning("Connection tried to request character list while already having a character selected");
            activity?.AddEvent(new ActivityEvent("DuplicateRequestAttempt"));
            connection.Close();
            return;
        }

        connection.AddQueryCallback(characterRepository.FindByAccountAsync(connection.AccountId),
            characters => { OnCharactersReceived(connection, characters); });

        _parentActivity = activity;
    }

    private void OnCharactersReceived(WorldConnection connection, IList<Character> characters)
    {
        using Activity? activity = DiagnosticsConfig.World.Source.StartActivity(nameof(OnCharactersReceived),
            ActivityKind.Internal, _parentActivity?.Context ?? default);
        activity?.SetTag(nameof(connection.AccountId), connection.AccountId);
        activity?.SetTag("Characters.Count", characters.Count);

        CharacterInfo[] characterInfo = characters.Select<Character, CharacterInfo>(
            character => new CharacterInfo
            {
                CharacterId = character.Id!.Value,
                Name = character.Name,
                Level = character.Level,
                Class = (ushort)character.Class,
                X = character.X,
                Y = character.Y,
                Z = character.Z
            }).ToArray();

        NetworkPacket result = SCharacterListPacket.Create(
            characterInfo.Length,
            world.Configuration.MaxCharactersPerAccount,
            characterInfo,
            connection.CryptoSession.Encrypt
        );

        connection.Send(result);
    }
}
