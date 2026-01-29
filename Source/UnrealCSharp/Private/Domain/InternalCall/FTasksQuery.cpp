#include "CoreMinimal.h"
#include "Binding/Class/FClassBuilder.h"
#include "CoreMacro/NamespaceMacro.h"
#include "Domain/FMonoDomain.h"
#include "Tasks/Task.h"
#include "Async/TaskGraphInterfaces.h"
#include "Misc/ScopeLock.h"

namespace
{
	struct FTasksQuery
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

		struct FManagedThunkCache
		{
			FCriticalSection Mutex;
			uint64 CachedKey = 0;
			void* CachedThunk = nullptr;
		};

		static uint64 GetManagedLookupCacheKey()
		{
			const uint64 DomainKey = reinterpret_cast<uint64>(FMonoDomain::Domain);
			const int32 ImageCount = FMonoDomain::Images.Num();
			const uint64 ImageCountKey = static_cast<uint64>(ImageCount);
			const uint64 FirstImageKey = ImageCount > 0 ? reinterpret_cast<uint64>(FMonoDomain::Images[0]) : 0;
			return DomainKey ^ (ImageCountKey << 1) ^ (FirstImageKey << 3);
		}

		static void* GetManagedThunkCached(FManagedThunkCache& Cache,
		                                   const TCHAR* InManagedClassName,
		                                   const TCHAR* InMethodName,
		                                   const int32 InParamCount)
		{
			const uint64 Key = GetManagedLookupCacheKey();

			if (Cache.CachedThunk != nullptr && Cache.CachedKey == Key)
			{
				return Cache.CachedThunk;
			}

			FScopeLock ScopeLock(&Cache.Mutex);

			if (Cache.CachedThunk != nullptr && Cache.CachedKey == Key)
			{
				return Cache.CachedThunk;
			}

			Cache.CachedKey = Key;
			Cache.CachedThunk = nullptr;

			const auto FoundClass = FMonoDomain::Class_From_Name(TEXT("Script.Library"), InManagedClassName);

			if (FoundClass == nullptr)
			{
				return nullptr;
			}

			const auto FoundMethod = FMonoDomain::Class_Get_Method_From_Name(FoundClass, InMethodName, InParamCount);

			if (FoundMethod == nullptr)
			{
				return nullptr;
			}

			Cache.CachedThunk = FMonoDomain::Method_Get_Unmanaged_Thunk(FoundMethod);

			return Cache.CachedThunk;
		}

		static void ExecuteBatchImplementation(const void* InStateHandle,
		                                       const int32 InTaskCount,
		                                       const bool bWait)
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

			static FManagedThunkCache ExecuteCache;
			const auto FoundThunk = GetManagedThunkCached(
				ExecuteCache, TEXT("UETasksQueryRunner"), TEXT("ExecuteTask"), 2);

			if (FoundThunk == nullptr)
			{
				return;
			}

			TArray<UE::Tasks::FTask> TaskList;
			TaskList.Reserve(InTaskCount);

			void* const StateHandle = const_cast<void*>(InStateHandle);

			for (int32 TaskIndex = 0; TaskIndex < InTaskCount; ++TaskIndex)
			{
				UE::Tasks::FTask Task;
				Task.Launch(TEXT("UETasksQuery.ExecuteBatch"), [StateHandle, TaskIndex, FoundThunk]()
				{
					FManagedJobScope ManagedScope;

					if (!ManagedScope.IsEntered())
					{
						return;
					}

					using FExecuteTaskThunk = void (*)(void*, int32, MonoObject**);
					const auto Thunk = reinterpret_cast<FExecuteTaskThunk>(FoundThunk);

					MonoObject* Exception = nullptr;
					Thunk(StateHandle, TaskIndex, &Exception);

					if (Exception != nullptr)
					{
						FMonoDomain::Unhandled_Exception(Exception);
					}
				});

				TaskList.Add(MoveTemp(Task));
			}

			if (bWait)
			{
				UE::Tasks::Wait(TaskList);
			}
		}

		static int32 GetNumWorkerThreadsImplementation()
		{
			return FTaskGraphInterface::Get().GetNumWorkerThreads();
		}

		static int32 GetCurrentNativeThreadIdImplementation()
		{
			return static_cast<int32>(FPlatformTLS::GetCurrentThreadId());
		}

		FTasksQuery()
		{
			FClassBuilder(TEXT("FTasksQuery"), NAMESPACE_LIBRARY)
				.Function(TEXT("ExecuteBatch"), ExecuteBatchImplementation)
				.Function(TEXT("GetNumWorkerThreads"), GetNumWorkerThreadsImplementation)
				.Function(TEXT("GetCurrentNativeThreadId"), GetCurrentNativeThreadIdImplementation);
		}
	};

	[[maybe_unused]] FTasksQuery TasksQuery;
}
