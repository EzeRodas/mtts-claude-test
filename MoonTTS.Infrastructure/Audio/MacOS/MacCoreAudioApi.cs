#if !WINDOWS
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace MoonTTS.Infrastructure.Audio;

/* Ahh... Here we go again */

/* File Structure:
        - (one forwarding method per ICoreAudioApi member)
        - Native
            - CoreAudioLib : string
            - AudioToolboxLib : string
            - CoreFoundationLib : string
            - (one extern per ICoreAudioApi member)
*/

/* Real ICoreAudioApi: forwards every call to CoreAudio, AudioToolbox and CoreFoundation */
[SupportedOSPlatform("macos")]
internal sealed class MacCoreAudioApi : ICoreAudioApi
{
    /* Forwards to CoreAudio AudioObjectGetPropertyDataSize */
    public int AudioObjectGetPropertyDataSize(uint inObjectID, ref ICoreAudioApi.AudioObjectPropertyAddress inAddress,
        uint inQualifierDataSize, IntPtr inQualifierData, out uint outDataSize) =>
        Native.AudioObjectGetPropertyDataSize(inObjectID, ref inAddress, inQualifierDataSize, inQualifierData, out outDataSize);

    /* Forwards to CoreAudio AudioObjectGetPropertyData (caller-owned buffer) */
    public int AudioObjectGetPropertyData(uint inObjectID, ref ICoreAudioApi.AudioObjectPropertyAddress inAddress,
        uint inQualifierDataSize, IntPtr inQualifierData, ref uint ioDataSize, IntPtr outData) =>
        Native.AudioObjectGetPropertyData(inObjectID, ref inAddress, inQualifierDataSize, inQualifierData, ref ioDataSize, outData);

    /* Forwards to CoreAudio AudioObjectGetPropertyData (pointer-sized value) */
    public int AudioObjectGetPropertyData(uint inObjectID, ref ICoreAudioApi.AudioObjectPropertyAddress inAddress,
        uint inQualifierDataSize, IntPtr inQualifierData, ref uint ioDataSize, out IntPtr outData) =>
        Native.AudioObjectGetPropertyData(inObjectID, ref inAddress, inQualifierDataSize, inQualifierData, ref ioDataSize, out outData);

    /* Forwards to AudioToolbox AudioQueueNewOutput */
    public int AudioQueueNewOutput(ref ICoreAudioApi.AudioStreamBasicDescription inFormat,
        ICoreAudioApi.AudioQueueOutputCallback inCallBackProc, IntPtr inUserData, IntPtr inCallbackRunLoop,
        IntPtr inCallbackRunLoopMode, uint inFlags, out IntPtr outAQ) =>
        Native.AudioQueueNewOutput(ref inFormat, inCallBackProc, inUserData, inCallbackRunLoop, inCallbackRunLoopMode, inFlags, out outAQ);

    /* Forwards to AudioToolbox AudioQueueAllocateBuffer */
    public int AudioQueueAllocateBuffer(IntPtr inAQ, uint inBufferByteSize, out IntPtr outBuffer) =>
        Native.AudioQueueAllocateBuffer(inAQ, inBufferByteSize, out outBuffer);

    /* Forwards to AudioToolbox AudioQueueEnqueueBuffer */
    public int AudioQueueEnqueueBuffer(IntPtr inAQ, IntPtr inBuffer, uint inNumPacketDescs, IntPtr inPacketDescs) =>
        Native.AudioQueueEnqueueBuffer(inAQ, inBuffer, inNumPacketDescs, inPacketDescs);

    /* Forwards to AudioToolbox AudioQueueStart */
    public int AudioQueueStart(IntPtr inAQ, IntPtr inStartTime) => Native.AudioQueueStart(inAQ, inStartTime);

    /* Forwards to AudioToolbox AudioQueuePause */
    public int AudioQueuePause(IntPtr inAQ) => Native.AudioQueuePause(inAQ);

    /* Forwards to AudioToolbox AudioQueueStop */
    public int AudioQueueStop(IntPtr inAQ, bool inImmediate) => Native.AudioQueueStop(inAQ, inImmediate);

    /* Forwards to AudioToolbox AudioQueueDispose */
    public int AudioQueueDispose(IntPtr inAQ, bool inImmediate) => Native.AudioQueueDispose(inAQ, inImmediate);

    /* Forwards to AudioToolbox AudioQueueSetParameter */
    public int AudioQueueSetParameter(IntPtr inAQ, uint inParamID, float inValue) =>
        Native.AudioQueueSetParameter(inAQ, inParamID, inValue);

    /* Forwards to AudioToolbox AudioQueueSetProperty */
    public int AudioQueueSetProperty(IntPtr inAQ, uint inParamID, ref IntPtr inPropertyData, uint inDatasize) =>
        Native.AudioQueueSetProperty(inAQ, inParamID, ref inPropertyData, inDatasize);

    /* Forwards to CoreFoundation CFStringGetLength */
    public nint CFStringGetLenght(IntPtr theString) => Native.CFStringGetLenght(theString);

    /* Forwards to CoreFoundation CFStringGetCharacters */
    public void CFStringGetCharacters(IntPtr theString, ICoreAudioApi.CFRange range, IntPtr buffer) =>
        Native.CFStringGetCharacters(theString, range, buffer);

    /* Forwards to CoreFoundation CFStringCreateWithCharacters */
    public IntPtr CFStringCreateWithCharacters(IntPtr alloc, string chars, nint numChars) =>
        Native.CFStringCreateWithCharacters(alloc, chars, numChars);

    /* Forwards to CoreFoundation CFRelease */
    public void CFRelease(IntPtr cf) => Native.CFRelease(cf);

    private static class Native
    {
        private const string CoreAudioLib = "/System/Library/Frameworks/CodeAudio.framework/CoreAudio";
        private const string AudioToolboxLib = "/System/Library/Frameworks/AudioToolbox.framework/AudioToolbox";
        private const string CoreFoundationLib = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

        /* P/Invoke for CoreAudio */

        [DllImport(CoreAudioLib)]
        public static extern int AudioObjectGetPropertyDataSize(
            uint inObjectID,
            ref ICoreAudioApi.AudioObjectPropertyAddress inAddress,
            uint inQualifierDataSize,
            IntPtr inQualifierData,
            out uint outDataSize);

        [DllImport(CoreAudioLib)]
        public static extern int AudioObjectGetPropertyData(
            uint inObjectID,
            ref ICoreAudioApi.AudioObjectPropertyAddress inAddress,
            uint inQualifierDataSize,
            IntPtr inQualifierData,
            ref uint ioDataSize,
            IntPtr outData);

        [DllImport(CoreAudioLib, EntryPoint = "AudioObjectGetPropertyData")]
        public static extern int AudioObjectGetPropertyData(
            uint inObjectID,
            ref ICoreAudioApi.AudioObjectPropertyAddress inAddress,
            uint inQualifierDataSize,
            IntPtr inQualifierData,
            ref uint ioDataSize,
            out IntPtr outData);

        [DllImport(AudioToolboxLib)]
        public static extern int AudioQueueNewOutput(
            ref ICoreAudioApi.AudioStreamBasicDescription inFormat,
            ICoreAudioApi.AudioQueueOutputCallback inCallBackProc,
            IntPtr inUserData,
            IntPtr inCallbackRunLoop,
            IntPtr inCallbackRunLoopMode,
            uint inFlags,
            out IntPtr outAQ);

        [DllImport(AudioToolboxLib)]
        public static extern int AudioQueueAllocateBuffer(IntPtr inAQ, uint inBufferByteSize, out IntPtr outBuffer);

        [DllImport(AudioToolboxLib)]
        public static extern int AudioQueueEnqueueBuffer(
            IntPtr inAQ,
            IntPtr inBuffer,
            uint inNumPacketDescs,
            IntPtr inPacketDescs);

        [DllImport(AudioToolboxLib)]
        public static extern int AudioQueueStart(IntPtr inAQ, IntPtr inStartTime);

        [DllImport(AudioToolboxLib)]
        public static extern int AudioQueuePause(IntPtr inAQ);

        [DllImport(AudioToolboxLib)]
        public static extern int AudioQueueStop(IntPtr inAQ, bool inImmediate);

        [DllImport(AudioToolboxLib)]
        public static extern int AudioQueueDispose(IntPtr inAQ, bool inImmediate);

        [DllImport(AudioToolboxLib)]
        public static extern int AudioQueueSetParameter(IntPtr inAQ, uint inParamID, float inValue);

        [DllImport(AudioToolboxLib)]
        public static extern int AudioQueueSetProperty(IntPtr inAQ, uint inParamID, ref IntPtr inPropertyData, uint inDatasize);

        /* P/Invoke */

        [DllImport(CoreFoundationLib)]
        public static extern nint CFStringGetLenght(IntPtr theString);

        [DllImport(CoreFoundationLib)]
        public static extern void CFStringGetCharacters(IntPtr theString, ICoreAudioApi.CFRange range, IntPtr buffer);

        [DllImport(CoreFoundationLib)]
        public static extern IntPtr CFStringCreateWithCharacters(
            IntPtr alloc,
            [MarshalAs(UnmanagedType.LPWStr)] string chars,
            nint numChars);

        [DllImport(CoreFoundationLib)]
        public static extern void CFRelease(IntPtr cf);
    }
}
#endif
