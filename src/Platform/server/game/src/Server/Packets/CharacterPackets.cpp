#include <Game/Server/Packets/CharacterPackets.h>

WorldPacket const* WorldPackets::Character::LogoutResponse::Write()
{
    _worldPacket << U32(LogoutResult);
    _worldPacket << U8(Instant);
    return &_worldPacket;
}

void WorldPackets::Character::PlayedTimeClient::Read()
{
    _worldPacket >> TriggerScriptEvent;
}

WorldPacket const* WorldPackets::Character::PlayedTime::Write()
{
    _worldPacket << U32(TotalTime);
    _worldPacket << U32(LevelTime);
    _worldPacket << U8(TriggerScriptEvent);

    return &_worldPacket;
}
