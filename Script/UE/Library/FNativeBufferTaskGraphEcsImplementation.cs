using System.Runtime.CompilerServices;

namespace Script.Library;

public static class FNativeBufferTaskGraphEcsImplementation
{
    [MethodImpl(MethodImplOptions.InternalCall)]
    public static extern long FNativeBufferTaskGraphEcs_UpdatePosVelSlicesParallelImplementation(
        nint archetypes,
        int archetypeCount,
        nint slices,
        int sliceCount,
        int dt);
}
