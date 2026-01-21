#include "Binding/Class/FClassBuilder.h"
#include "CoreMacro/NamespaceMacro.h"
#include "Domain/FMonoDomain.h"
#include "HAL/PlatformTLS.h"
#include "Async/TaskGraphInterfaces.h"
#include "Misc/ScopeLock.h"

namespace
{
	struct FTaskGraph
	{
		struct FManagedJobScope
		{
			explicit FManagedJobScope()
				: bEntered(FMonoDomain::TryEnterManagedJobExecution())
				, bDetachOnExit(FMonoDomain::ShouldDetachAfterManagedJob() && !IsInGameThread())
			{
				if (bEntered)
				{
					FMonoDomain::EnsureThreadAttached();
				}
			}

			~FManagedJobScope()
			{
				if (!bEntered)
				{
					return;
				}

				if (bDetachOnExit)
				{
					FMonoDomain::EnsureThreadDetached();
				}

				FMonoDomain::LeaveManagedJobExecution();
			}

			bool IsEntered() const
			{
				return bEntered;
			}

		private:
			bool bEntered = false;
			bool bDetachOnExit = false;
		};

		struct FManagedMethodCache
		{
			FCriticalSection Mutex;
			uint64 CachedKey = 0;
			MonoMethod* CachedMethod = nullptr;
		};

		static uint64 GetManagedLookupCacheKey()
		{
			const uint64 DomainKey = reinterpret_cast<uint64>(FMonoDomain::Domain);
			const int32 ImageCount = FMonoDomain::Images.Num();
			const uint64 ImageCountKey = static_cast<uint64>(ImageCount);
			const uint64 FirstImageKey = ImageCount > 0 ? reinterpret_cast<uint64>(FMonoDomain::Images[0]) : 0;
			return DomainKey ^ (ImageCountKey << 1) ^ (FirstImageKey << 3);
		}

		static MonoMethod* GetExecuteTaskMethodCached(FManagedMethodCache& Cache, const TCHAR* InManagedClassName)
		{
			const uint64 Key = GetManagedLookupCacheKey();

			if (Cache.CachedMethod != nullptr && Cache.CachedKey == Key)
			{
				return Cache.CachedMethod;
			}

			FScopeLock ScopeLock(&Cache.Mutex);

			if (Cache.CachedMethod != nullptr && Cache.CachedKey == Key)
			{
				return Cache.CachedMethod;
			}

			Cache.CachedKey = Key;
			Cache.CachedMethod = nullptr;

			const auto FoundClass = FMonoDomain::Class_From_Name(TEXT("Script.Library"), InManagedClassName);

			if (FoundClass == nullptr)
			{
				return nullptr;
			}

			Cache.CachedMethod = FMonoDomain::Class_Get_Method_From_Name(FoundClass, TEXT("ExecuteTask"), 2);

			return Cache.CachedMethod;
		}

		static void ExecuteBatchWithMethod(const void* InStateHandle,
		                                   const int32 InTaskCount,
		                                   const bool bWait,
		                                   MonoMethod* InExecuteTaskMethod)
		{
			if (InTaskCount <= 0)
			{
				return;
			}

			if (!FMonoDomain::bLoadSucceed || FMonoDomain::Domain == nullptr)
			{
				return;
			}

			if (!FMonoDomain::IsManagedJobExecutionEnabled())
			{
				return;
			}

			if (InExecuteTaskMethod == nullptr)
			{
				return;
			}

			FGraphEventArray Events;
			Events.Reserve(InTaskCount);

			void* const StateHandle = const_cast<void*>(InStateHandle);

			for (int32 Index = 0; Index < InTaskCount; ++Index)
			{
				Events.Add(FFunctionGraphTask::CreateAndDispatchWhenReady([StateHandle, Index, InExecuteTaskMethod]()
				{
					FManagedJobScope ManagedScope;

					if (!ManagedScope.IsEntered())
					{
						return;
					}

					void* StateHandleParam = StateHandle;
					int32 IndexParam = Index;

					void* Params[2]{
						&StateHandleParam,
						&IndexParam
					};

					MonoObject* Exception = nullptr;

					(void)FMonoDomain::Runtime_Invoke(InExecuteTaskMethod, nullptr, Params, &Exception);

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

			if (!FMonoDomain::IsManagedJobExecutionEnabled())
			{
				return;
			}

			const auto FoundMethod = FMonoDomain::Class_Get_Method_From_Name(
				FMonoDomain::Class_From_Name(TEXT("Script.Library"), InManagedClassName),
				TEXT("ExecuteTask"),
				2);

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
					FManagedJobScope ManagedScope;

					if (!ManagedScope.IsEntered())
					{
						return;
					}

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
			static FManagedMethodCache Cache;
			const auto FoundMethod = GetExecuteTaskMethodCached(Cache, TEXT("TaskGraphBatchBaseline"));
			ExecuteBatchWithMethod(InStateHandle, InTaskCount, bWait, FoundMethod);
		}

		static void ExecuteBatchTestlineImplementation(const void* InStateHandle, const int32 InTaskCount, const bool bWait)
		{
			static FManagedMethodCache Cache;
			const auto FoundMethod = GetExecuteTaskMethodCached(Cache, TEXT("TaskGraphBatchTestline"));
			ExecuteBatchWithMethod(InStateHandle, InTaskCount, bWait, FoundMethod);
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

				if (!FMonoDomain::IsManagedJobExecutionEnabled())
				{
					return;
				}

				FManagedJobScope ManagedScope;

				if (!ManagedScope.IsEntered())
				{
					return;
				}

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
