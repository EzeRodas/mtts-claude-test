#if !WINDOWS
using System.Runtime.InteropServices;

namespace MoonTTS.Infrastructure.Audio;

/* File Structure:
        - Constants (property selectors, scopes, formats, queue properties / parameters)
        - AudioObjectPropertyAddress
        - AudioStreamBasicDescription
        - AudioBuffer
        - AudioBufferList
        - AudioQueueBuffer
        - CFRange
        - AudioQueueOutputCallback
        - AudioObjectGetPropertyDataSize(uint inObjectID, ref AudioObjectPropertyAddress inAddress, uint inQualifierDataSize, nint inQualifierData, out uint outDataSize) : int
        - AudioObjectGetPropertyData(uint inObjectID, ref AudioObjectPropertyAddress inAddress, uint inQualifierDataSize, nint inQualifierData, ref uint ioDataSize, nint outData) : int
        - AudioObjectGetPropertyData(uint inObjectID, ref AudioObjectPropertyAddress inAddress, uint inQualifierDataSize, nint inQualifierData, ref uint ioDataSize, out nint outData) : int
        - AudioQueueNewOutput(ref AudioStreamBasicDescription inFormat, AudioQueueOutputCallback inCallBackProc, nint inUserData, nint inCallbackRunLoop, nint inCallbackRunLoopMode, uint inFlags, out nint outAQ) : int
        - AudioQueueAllocateBuffer(nint inAQ, uint inBufferByteSize, out nint outBuffer) : int
        - AudioQueueEnqueueBuffer(nint inAQ, nint inBuffer, uint inNumPacketDescs, nint inPacketDescs) : int
        - AudioQueueStart(nint inAQ, nint inStartTime) : int
        - AudioQueuePause(nint inAQ) : int
        - AudioQueueStop(nint inAQ, bool inImmediate) : int
        - AudioQueueDispose(nint inAQ, bool inImmediate) : int
        - AudioQueueSetParameter(nint inAQ, uint inParamID, float inValue) : int
        - AudioQueueSetProperty(nint inAQ, uint inParamID, ref nint inPropertyData, uint inDatasize) : int
        - CFStringGetLenght(nint theString) : nint
        - CFStringGetCharacters(nint theString, CFRange range, nint buffer) : void
        - CFStringCreateWithCharacters(nint alloc, string chars, nint numChars) : nint
        - CFRelease(nint cf) : void
*/

/* The CoreAudio / AudioToolbox / CoreFoundation calls MacAudioPlayer makes, so the player can run against the real
   frameworks (MacCoreAudioApi) or against a dummy that answers like macOS would */
public interface ICoreAudioApi
{
    /* Constants */

    public const uint kAudioObjectSystemObject = 1;
    public const uint kAudioHardwarePropertyDevices = 0x64657623; /* dev# */
    public const uint kAudioObjectPropertyScopeGlobal = 0x676c6f62; /* glob */
    public const uint kAudioObjectPropertyScopeOutput = 0x6f757470; /* outp */
    public const uint kAudioObjectPropertyElementMain = 0;

    public const uint kAudioDevicePropertyDeviceNameCFString = 0x6c6e616d; /* lnam */
    public const uint kAudioDevicePropertyDeviceUID = 0x75696420; /* uid */
    public const uint kAudioDevicePropertyStreamConfiguration = 0x736c7074; /* slpt */

    public const uint kAudioFormatLinearPCM = 0x6c70636d; /* lpcm */
    public const uint kAudioFormatFlagIsSignedInteger = 4;
    public const uint kAudioFormatFlagIsPacked = 8;

    public const uint kAudioQueueProperty_CurrentDevice  = 0x61716364; /* aqcd */
    public const uint kAudioQueueParam_Volume  = 1;

    /* Structs */

    [StructLayout(LayoutKind.Sequential)]
    public struct AudioObjectPropertyAddress
    {
        public uint mSelector;
        public uint mScope;
        public uint mElement;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct AudioStreamBasicDescription
    {
        public double mSampleRate;
        public uint mFormatID;
        public uint mFormatFlags;
        public uint mBytesPerPacket;
        public uint mFramesPerPacket;
        public uint mBytesPerFrame;
        public uint mChannelsPerFrame;
        public uint mBitsPerChannel;
        public uint mReserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct AudioBuffer
    {
        public uint mNumberChannels;
        public uint mDataByteSize;
        public IntPtr mData;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct AudioBufferList
    {
        public uint mNumberBuffers;
        public AudioBuffer mBuffers; /* Inline first buffer */
    }

    /* Header of a buffer returned by AudioQueueAllocateBuffer (AudioToolbox/AudioQueue.h) */
    [StructLayout(LayoutKind.Sequential)]
    public struct AudioQueueBuffer
    {
        public uint mAudioDataBytesCapacity;
        public IntPtr mAudioData;
        public uint mAudioDataByteSize;
        public IntPtr mUserData;
        public uint mPacketDescriptionCapacity;
        public IntPtr mPacketDescriptions;
        public uint mPacketDescriptionCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct CFRange
    {
        public nint Location;
        public nint Lenght;
    }

    /* Invoked by the queue when it has finished playing a buffer */
    public delegate void AudioQueueOutputCallback(IntPtr inUserData, IntPtr inAQ, IntPtr inBuffer);

    /* CoreAudio */

    /* Size in bytes of an audio object property */
    int AudioObjectGetPropertyDataSize(
        uint inObjectID,
        ref AudioObjectPropertyAddress inAddress,
        uint inQualifierDataSize,
        IntPtr inQualifierData,
        out uint outDataSize);

    /* Copies an audio object property into a caller-owned buffer */
    int AudioObjectGetPropertyData(
        uint inObjectID,
        ref AudioObjectPropertyAddress inAddress,
        uint inQualifierDataSize,
        IntPtr inQualifierData,
        ref uint ioDataSize,
        IntPtr outData);

    /* Reads a pointer-sized audio object property (e.g. a CFStringRef the caller must release) */
    int AudioObjectGetPropertyData(
        uint inObjectID,
        ref AudioObjectPropertyAddress inAddress,
        uint inQualifierDataSize,
        IntPtr inQualifierData,
        ref uint ioDataSize,
        out IntPtr outData);

    /* AudioToolbox */

    /* Creates a playback queue for the given PCM format */
    int AudioQueueNewOutput(
        ref AudioStreamBasicDescription inFormat,
        AudioQueueOutputCallback inCallBackProc,
        IntPtr inUserData,
        IntPtr inCallbackRunLoop,
        IntPtr inCallbackRunLoopMode,
        uint inFlags,
        out IntPtr outAQ);

    /* Allocates a buffer (AudioQueueBuffer header + data) owned by the queue */
    int AudioQueueAllocateBuffer(IntPtr inAQ, uint inBufferByteSize, out IntPtr outBuffer);

    /* Adds a filled buffer to the queue's playback list */
    int AudioQueueEnqueueBuffer(
        IntPtr inAQ,
        IntPtr inBuffer,
        uint inNumPacketDescs,
        IntPtr inPacketDescs);

    /* Starts (or resumes) playback */
    int AudioQueueStart(IntPtr inAQ, IntPtr inStartTime);

    /* Pauses playback, keeping the queued buffers */
    int AudioQueuePause(IntPtr inAQ);

    /* Stops playback */
    int AudioQueueStop(IntPtr inAQ, bool inImmediate);

    /* Releases the queue and its buffers */
    int AudioQueueDispose(IntPtr inAQ, bool inImmediate);

    /* Sets a queue parameter (e.g. volume) */
    int AudioQueueSetParameter(IntPtr inAQ, uint inParamID, float inValue);

    /* Sets a queue property (e.g. the output device UID) */
    int AudioQueueSetProperty(IntPtr inAQ, uint inParamID, ref IntPtr inPropertyData, uint inDatasize);

    /* CoreFoundation */

    /* Number of UTF-16 code units of a CFString */
    nint CFStringGetLenght(IntPtr theString);

    /* Copies a range of UTF-16 code units of a CFString into a buffer */
    void CFStringGetCharacters(IntPtr theString, CFRange range, IntPtr buffer);

    /* Creates a CFString the caller must release */
    IntPtr CFStringCreateWithCharacters(
        IntPtr alloc,
        string chars,
        nint numChars);

    /* Releases a CoreFoundation object */
    void CFRelease(IntPtr cf);
}
#endif
