#include "Binding/Class/FClassBuilder.h"
#include "CoreMacro/NamespaceMacro.h"
#include "Domain/FMonoDomain.h"
#include "HAL/PlatformTLS.h"
#include "Async/TaskGraphInterfaces.h"

namespace
{
	struct FTaskGraph
	{
		static int32 GetCurrentThreadIdImplementation()
		{
			return static_cast<int32>(FPlatformTLS::GetCurrentThreadId());
		}

		static void ExecuteBatchInternal(const void* InStateHandle,
		                                 const int32 InTaskCount,
		                                 const bool bWait,
		                                 const TCHAR* InManagedClassName)
		{
			if (InTaskCount <= 0)
			{
				return;
			}

			if (!FMonoDomain::bLoadSucceed || FMonoDomain::Domain == nullptr)
			{
				return;
			}

			const auto FoundClass = FMonoDomain::Class_From_Name(TEXT("Script.Library"), InManagedClassName);

			if (FoundClass == nullptr)
			{
				return;
			}

			const auto FoundMethod = FMonoDomain::Class_Get_Method_From_Name(FoundClass, TEXT("ExecuteTask"), 2);

			if (FoundMethod == nullptr)
			{
				return;
			}

			FGraphEventArray Events;
			Events.Reserve(InTaskCount);

			void* const StateHandle = const_cast<void*>(InStateHandle);

			for (int32 Index = 0; Index < InTaskCount; ++Index)
			{
				Events.Add(FFunctionGraphTask::CreateAndDispatchWhenReady([StateHandle, Index, FoundMethod]()
				{
					if (!FMonoDomain::bLoadSucceed || FMonoDomain::Domain == nullptr)
					{
						return;
					}

					FMonoDomain::EnsureThreadAttached();

					void* StateHandleParam = StateHandle;
					int32 IndexParam = Index;

					void* Params[2]{
						&StateHandleParam,
						&IndexParam
					};

					MonoObject* Exception = nullptr;

					(void)FMonoDomain::Runtime_Invoke(FoundMethod, nullptr, Params, &Exception);

					if (Exception != nullptr)
					{
						FMonoDomain::Unhandled_Exception(Exception);
					}
				}, TStatId(), nullptr, ENamedThreads::AnyBackgroundThreadNormalTask));
			}

			if (bWait)
			{
				FTaskGraphInterface::Get().WaitUntilTasksComplete(
					Events,
					IsInGameThread() ? ENamedThreads::GameThread : ENamedThreads::AnyThread
				);
			}
		}

		static void ExecuteBatchBaselineImplementation(const void* InStateHandle, const int32 InTaskCount, const bool bWait)
		{
			ExecuteBatchInternal(InStateHandle, InTaskCount, bWait, TEXT("TaskGraphBatchBaseline"));
		}

		static void ExecuteBatchTestlineImplementation(const void* InStateHandle, const int32 InTaskCount, const bool bWait)
		{
			ExecuteBatchInternal(InStateHandle, InTaskCount, bWait, TEXT("TaskGraphBatchTestline"));
		}

		static void ExecuteBatchImplementation(const void* InStateHandle, const int32 InTaskCount, const bool bWait)
		{
			ExecuteBatchInternal(InStateHandle, InTaskCount, bWait, TEXT("TaskGraphBatch"));
		}

		static void EnqueueProbeImplementation(const int32 InToken)
		{
			const uint32 GameThreadId = FPlatformTLS::GetCurrentThreadId();

			FFunctionGraphTask::CreateAndDispatchWhenReady([InToken, GameThreadId]()
			{
				if (!FMonoDomain::bLoadSucceed || FMonoDomain::Domain == nullptr)
				{
					return;
				}

				FMonoDomain::EnsureThreadAttached();

				const uint32 WorkerThreadId = FPlatformTLS::GetCurrentThreadId();

				const auto FoundClass = FMonoDomain::Class_From_Name(TEXT("Script.Library"), TEXT("TaskGraphProbe"));

				if (FoundClass == nullptr)
				{
					return;
				}

				const auto FoundMethod = FMonoDomain::Class_Get_Method_From_Name(FoundClass, TEXT("OnWorker"), 3);

				if (FoundMethod == nullptr)
				{
					return;
				}

				int32 TokenParam = InToken;
				int32 GameThreadIdParam = static_cast<int32>(GameThreadId);
				int32 WorkerThreadIdParam = static_cast<int32>(WorkerThreadId);

				void* Params[3]{
					&TokenParam,
					&GameThreadIdParam,
					&WorkerThreadIdParam
				};

				MonoObject* Exception = nullptr;

				(void)FMonoDomain::Runtime_Invoke(FoundMethod, nullptr, Params, &Exception);

				if (Exception != nullptr)
				{
					FMonoDomain::Unhandled_Exception(Exception);
				}
			}, TStatId(), nullptr, ENamedThreads::AnyBackgroundThreadNormalTask);
		}

		FTaskGraph()
		{
			FClassBuilder(TEXT("FTaskGraph"), NAMESPACE_LIBRARY)
				.Function(TEXT("GetCurrentThreadId"), GetCurrentThreadIdImplementation)
				.Function(TEXT("ExecuteBatchBaseline"), ExecuteBatchBaselineImplementation)
				.Function(TEXT("ExecuteBatchTestline"), ExecuteBatchTestlineImplementation)
				.Function(TEXT("ExecuteBatch"), ExecuteBatchImplementation)
				.Function(TEXT("EnqueueProbe"), EnqueueProbeImplementation);
		}
	};

	[[maybe_unused]] FTaskGraph TaskGraph;
}
