#include "CoreMinimal.h"
#include "Async/TaskGraphInterfaces.h"
#include "Binding/Class/FClassBuilder.h"
#include "CoreMacro/NamespaceMacro.h"

namespace
{
	struct FArchetypeDesc
	{
		int32* Position;
		int32* Velocity;
		int32 Length;
	};

	struct FSliceDesc
	{
		int32 ArchetypeIndex;
		int32 Start;
		int32 Length;
	};

	struct FNativeBufferTaskGraphEcs
	{
		static int64 UpdatePosVelSlicesParallelImplementation(
			const void* InArchetypes,
			const int32 InArchetypeCount,
			const void* InSlices,
			const int32 InSliceCount,
			const int32 InDt)
		{
			if (InArchetypes == nullptr || InSlices == nullptr || InArchetypeCount <= 0 || InSliceCount <= 0)
			{
				return 0;
			}

			const FArchetypeDesc* Archetypes = static_cast<const FArchetypeDesc*>(InArchetypes);
			const FSliceDesc* Slices = static_cast<const FSliceDesc*>(InSlices);

			TArray<int64> PartialSums;
			PartialSums.SetNumZeroed(InSliceCount);

			FGraphEventArray Events;
			Events.Reserve(InSliceCount);

			for (int32 SliceIndex = 0; SliceIndex < InSliceCount; ++SliceIndex)
			{
				Events.Add(FFunctionGraphTask::CreateAndDispatchWhenReady([Archetypes, Slices, InArchetypeCount, InDt, SliceIndex, &PartialSums]()
				{
					const FSliceDesc& Slice = Slices[SliceIndex];
					if (Slice.ArchetypeIndex < 0 || Slice.ArchetypeIndex >= InArchetypeCount)
					{
						PartialSums[SliceIndex] = 0;
						return;
					}

					const FArchetypeDesc& Arch = Archetypes[Slice.ArchetypeIndex];
					if (Arch.Position == nullptr || Arch.Velocity == nullptr || Arch.Length <= 0 || Slice.Length <= 0)
					{
						PartialSums[SliceIndex] = 0;
						return;
					}

					const int32 Start = Slice.Start;
					const int32 End = FMath::Min(Start + Slice.Length, Arch.Length);
					if (Start < 0 || Start >= End)
					{
						PartialSums[SliceIndex] = 0;
						return;
					}

					int64 LocalSum = 0;
					for (int32 i = Start; i < End; ++i)
					{
						Arch.Position[i] += Arch.Velocity[i] * InDt;
						LocalSum += Arch.Position[i];
					}

					PartialSums[SliceIndex] = LocalSum;
				}, TStatId(), nullptr, ENamedThreads::AnyBackgroundThreadNormalTask));
			}

			FTaskGraphInterface::Get().WaitUntilTasksComplete(
				Events,
				IsInGameThread() ? ENamedThreads::GameThread : ENamedThreads::AnyThread
			);

			int64 Sum = 0;
			for (const int64 Value : PartialSums)
			{
				Sum += Value;
			}

			return Sum;
		}

		FNativeBufferTaskGraphEcs()
		{
			FClassBuilder(TEXT("FNativeBufferTaskGraphEcs"), NAMESPACE_LIBRARY)
				.Function(TEXT("UpdatePosVelSlicesParallel"), UpdatePosVelSlicesParallelImplementation);
		}
	};

	[[maybe_unused]] FNativeBufferTaskGraphEcs NativeBufferTaskGraphEcs;
}
