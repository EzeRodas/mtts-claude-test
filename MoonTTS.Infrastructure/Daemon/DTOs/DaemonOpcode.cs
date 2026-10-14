namespace MoonTTS.Infrastructure.Daemon.DTOs;

public enum DaemonOpcode : byte
{
    Request = 0x01,
    Cancel = 0x02,
    
    Response = 0x10,
    AudioChunk = 0x11,
    AudioEnd = 0x12,
    Error = 0x13
}