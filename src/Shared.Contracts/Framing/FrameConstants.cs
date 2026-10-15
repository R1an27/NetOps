namespace Shared.Contracts.Framing;

public static class FrameConstants
{
    public const int LengthPrefixSize = 4;
    public const int MaxFrameSize = 1024 * 1024; // 1 MB
}

public readonly record struct Frame(byte Type, byte[] Payload);
