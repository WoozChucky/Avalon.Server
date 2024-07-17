using Avalon.Network.Packets.Auth;
using Avalon.Network.Packets.Character;
using Avalon.Network.Packets.Map;
using Avalon.Network.Packets.Movement;
using Avalon.Network.Packets.Social;

namespace Avalon.Network.Packets;

public delegate void AccountRegisterHandler(object sender, SRegisterResultPacket packet);
public delegate void PlayerConnectedHandler(object sender, SPlayerConnectedPacket packet);
public delegate void PlayerDisconnectedHandler(object sender, SPlayerDisconnectedPacket packet);
public delegate void PlayerMovedHandler(object sender, SPlayerPositionUpdatePacket packet);
public delegate void LatencyUpdatedHandler(object sender, double latency);
public delegate void ChatMessageHandler(object sender, SChatMessagePacket packet);
public delegate void AuthResultHandler(object sender, SAuthResultPacket packet);
public delegate void GroupInviteHandler(object sender, SGroupInvitePacket packet);
public delegate void GroupResultHandler(object sender, SGroupResultPacket packet);
public delegate void CharacterListHandler(object sender, SCharacterListPacket packet);
public delegate void CharacterSelectedHandler(object sender, SCharacterSelectedPacket packet);
public delegate void CharacterCreatedHandler(object sender, SCharacterCreatedPacket packet);
public delegate void CharacterDeletedHandler(object sender, SCharacterDeletedPacket packet);
public delegate void LogoutHandler(object sender, SLogoutPacket packet);
public delegate void MapTeleportHandler(object sender, SMapTeleportPacket packet);
