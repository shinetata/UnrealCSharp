using System.Runtime.CompilerServices;

namespace Script.Library;

public static class FNativeBufferKernelImplementation
{
    [MethodImpl(MethodImplOptions.InternalCall)]
    public static extern int FNativeBufferKernel_AddOneAndSumInt32Implementation(nint data, int length);
}

