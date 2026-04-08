namespace Avalon.Network.Packets.Auth;

public enum MFAOperationResult : ushort
{
    Success,
    AlreadyEnabled,
    InvalidCode,
    NotEnabled,
    Error
}
