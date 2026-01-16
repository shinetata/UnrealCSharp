#include "CoreMinimal.h"
#include "Async/TaskGraphInterfaces.h"
#include "Binding/Class/FClassBuilder.h"
#include "CoreMacro/NamespaceMacro.h"
#include "HAL/PlatformTLS.h"
#include "Log/UnrealCSharpLog.h"

namespace
{
	struct FNativeBufferTaskGraph
	{
		static int64 AddOneAndSumInt32ParallelImplementation(const void* InData, const int32 InLength, const int32 InTaskCount)
		{
			if (InData == nullptr || InLength <= 0)
			{
				return 0;
			}

			const int32 SafeTaskCount = FMath::Clamp(InTaskCount, 1, InLength);

			int32* Data = static_cast<int32*>(const_cast<void*>(InData));

			TArray<int64> PartialSums;
			PartialSums.SetNumZeroed(SafeTaskCount);

			FGraphEventArray Events;
			Events.Reserve(SafeTaskCount);

			const uint32 DispatchThreadId = FPlatformTLS::GetCurrentThreadId();

			const int32 ChunkSize = (InLength + SafeTaskCount - 1) / SafeTaskCount;

			for (int32 TaskIndex = 0; TaskIndex < SafeTaskCount; ++TaskIndex)
			{
				const int32 Start = TaskIndex * ChunkSize;
				const int32 End = FMath::Min(Start + ChunkSize, InLength);

				Events.Add(FFunctionGraphTask::CreateAndDispatchWhenReady([Data, Start, End, TaskIndex, DispatchThreadId, &PartialSums]()
				{
					const uint32 WorkerThreadId = FPlatformTLS::GetCurrentThreadId();

					UE_LOG(LogUnrealCSharp, Log,
					       TEXT("[NativeBufferTaskGraph] task=%d range=[%d,%d) DispatchTid=%u WorkerTid=%u"),
					       TaskIndex,
					       Start,
					       End,
					       DispatchThreadId,
					       WorkerThreadId);

					int64 LocalSum = 0;
					for (int32 i = Start; i < End; ++i)
					{
						Data[i] += 1;
						LocalSum += Data[i];
					}

					PartialSums[TaskIndex] = LocalSum;
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

		FNativeBufferTaskGraph()
		{
			FClassBuilder(TEXT("FNativeBufferTaskGraph"), NAMESPACE_LIBRARY)
				.Function(TEXT("AddOneAndSumInt32Parallel"), AddOneAndSumInt32ParallelImplementation);
		}
	};

	[[maybe_unused]] FNativeBufferTaskGraph NativeBufferTaskGraph;
}
