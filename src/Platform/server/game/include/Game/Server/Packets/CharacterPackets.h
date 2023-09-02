#pragma once

#include <Common/Types.h>

#include "../Packet.h"

namespace WorldPackets
{
    namespace Character
    {
        class LogoutRequest final : public ClientPacket
        {
        public:
            LogoutRequest(WorldPacket&& packet) : ClientPacket(std::move(packet)) { }

            void Read() override { }
        };

        class LogoutResponse final : public ServerPacket
        {
        public:
            LogoutResponse() : ServerPacket(SMSG_LOGOUT_RESPONSE, 4 + 1) { }

            WorldPacket const* Write() override;

            U32 LogoutResult = 0;
            bool Instant = false;
        };

        class LogoutComplete final : public ServerPacket
        {
        public:
            LogoutComplete() : ServerPacket(SMSG_LOGOUT_COMPLETE, 0) { }

            WorldPacket const* Write() override { return &_worldPacket; }
        };

        class LogoutCancel final : public ClientPacket
        {
        public:
            LogoutCancel(WorldPacket&& packet) : ClientPacket(std::move(packet)) { }

            void Read() override { }
        };

        class LogoutCancelAck final : public ServerPacket
        {
        public:
            LogoutCancelAck() : ServerPacket(SMSG_LOGOUT_CANCEL_ACK, 0) { }

            WorldPacket const* Write() override { return &_worldPacket; }
        };

        class PlayerLogout final : public ClientPacket
        {
        public:
            PlayerLogout(WorldPacket&& packet) : ClientPacket(std::move(packet)) { }

            void Read() override { }
        };

        class PlayedTimeClient final : public ClientPacket
        {
        public:
            PlayedTimeClient(WorldPacket&& packet) : ClientPacket(CMSG_PLAYED_TIME, std::move(packet)) { }

            void Read() override;

            bool TriggerScriptEvent = false;
        };

        class PlayedTime final : public ServerPacket
        {
        public:
            PlayedTime() : ServerPacket(SMSG_PLAYED_TIME, 9) { }

            WorldPacket const* Write() override;

            U32 TotalTime = 0;
            U32 LevelTime = 0;
            bool TriggerScriptEvent = false;
        };
    }
}
