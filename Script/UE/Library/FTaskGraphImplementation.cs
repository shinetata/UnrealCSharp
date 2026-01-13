using System.Runtime.CompilerServices;

namespace Script.Library;

public static unsafe class FTaskGraphImplementation
{
    [MethodImpl(MethodImplOptions.InternalCall)]
    public static extern void FTaskGraph_EnqueueProbeImplementation(int token);
}

