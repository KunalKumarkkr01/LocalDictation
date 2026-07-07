using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace LocalDictation.Infrastructure.Mac.Interop;

/// <summary>
/// P/Invoke surface for the AudioToolbox <c>AudioQueue</c> C API used to capture microphone input on
/// macOS (the C-level equivalent of what NAudio's WaveIn does on Windows).
/// </summary>
[SupportedOSPlatform("macos")]
internal static class AudioToolbox
{
    private const string Lib = "/System/Library/Frameworks/AudioToolbox.framework/AudioToolbox";

    internal const uint FormatLinearPCM = 0x6C70636D;          // 'lpcm'
    internal const uint FormatFlagIsFloat = 0x1;
    internal const uint FormatFlagIsPacked = 0x8;

    /// <summary>Core Audio stream format descriptor.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct AudioStreamBasicDescription
    {
        internal double mSampleRate;
        internal uint mFormatID;
        internal uint mFormatFlags;
        internal uint mBytesPerPacket;
        internal uint mFramesPerPacket;
        internal uint mBytesPerFrame;
        internal uint mChannelsPerFrame;
        internal uint mBitsPerChannel;
        internal uint mReserved;
    }

    /// <summary>Header of an AudioQueue buffer; <c>mAudioData</c> points at the raw samples.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct AudioQueueBuffer
    {
        internal uint mAudioDataBytesCapacity;
        internal IntPtr mAudioData;
        internal uint mAudioDataByteSize;
        internal IntPtr mUserData;
        internal uint mPacketDescriptionCapacity;
        internal IntPtr mPacketDescriptions;
        internal uint mPacketDescriptionCount;
    }

    /// <summary>Input callback: <c>void(void* userData, AudioQueueRef, AudioQueueBufferRef, ...)</c>.</summary>
    internal delegate void AudioQueueInputCallback(
        IntPtr userData, IntPtr aq, IntPtr buffer, IntPtr startTime, uint numPackets, IntPtr packetDescs);

    [DllImport(Lib)]
    internal static extern int AudioQueueNewInput(
        ref AudioStreamBasicDescription format,
        AudioQueueInputCallback callback,
        IntPtr userData,
        IntPtr callbackRunLoop,
        IntPtr callbackRunLoopMode,
        uint flags,
        out IntPtr audioQueue);

    [DllImport(Lib)]
    internal static extern int AudioQueueAllocateBuffer(IntPtr aq, uint bufferByteSize, out IntPtr buffer);

    [DllImport(Lib)]
    internal static extern int AudioQueueEnqueueBuffer(IntPtr aq, IntPtr buffer, uint numPacketDescs, IntPtr packetDescs);

    [DllImport(Lib)]
    internal static extern int AudioQueueStart(IntPtr aq, IntPtr startTime);

    [DllImport(Lib)]
    internal static extern int AudioQueueStop(IntPtr aq, [MarshalAs(UnmanagedType.I1)] bool immediate);

    [DllImport(Lib)]
    internal static extern int AudioQueueDispose(IntPtr aq, [MarshalAs(UnmanagedType.I1)] bool immediate);
}
