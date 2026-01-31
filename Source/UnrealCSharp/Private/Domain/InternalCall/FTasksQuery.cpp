#include "CoreMinimal.h"
#include "Binding/Class/FClassBuilder.h"
#include "CoreMacro/NamespaceMacro.h"
#include "Domain/FMonoDomain.h"
#include "Tasks/Task.h"
#include "Async/TaskGraphInterfaces.h"
#include "Misc/ScopeLock.h"
#include "Setting/UnrealCSharpSetting.h"
#include "Common/FUnrealCSharpFunctionLibrary.h"
#include "Containers/Array.h"
#include <atomic>

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

		static std::atomic<int64> NextHandleId;
		static FCriticalSection HandleMutex;
		static TMap<int64, TArray<UE::Tasks::FTask>> HandleTasks;

		static bool IsDebugSingleThread()
		{
#if UE_BUILD_DEBUG
			return true;
#else
			if (const auto Setting = FUnrealCSharpFunctionLibrary::GetMutableDefaultSafe<UUnrealCSharpSetting>())
			{
				return Setting->IsEnableDebug();
			}
			return false;
#endif
		}

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

		static bool ValidateManagedContext()
		{
			if (!FMonoDomain::bLoadSucceed || FMonoDomain::Domain == nullptr)
			{
				return false;
			}

			if (!FMonoDomain::IsManagedJobExecutionEnabled())
			{
				return false;
			}

			return true;
		}

		static bool GetExecuteThunk(FManagedThunkCache& Cache, void*& OutThunk)
		{
			OutThunk = GetManagedThunkCached(
				Cache, TEXT("UETasksQueryRunner"), TEXT("ExecuteTask"), 2);
			return OutThunk != nullptr;
		}

		static void ExecuteOneTask(void* StateHandle, void* Thunk, int32 TaskIndex)
		{
			using FExecuteTaskThunk = void (*)(void*, int32, MonoObject**);
			const auto TypedThunk = reinterpret_cast<FExecuteTaskThunk>(Thunk);

			MonoObject* Exception = nullptr;
			TypedThunk(StateHandle, TaskIndex, &Exception);
			if (Exception != nullptr)
			{
				FMonoDomain::Unhandled_Exception(Exception);
			}
		}

		static void ExecuteBatchImplementation(const void* InStateHandle,
		                                       const int32 InTaskCount,
		                                       const bool bWait)
		{
			if (InTaskCount <= 0)
			{
				return;
			}

			if (!ValidateManagedContext())
			{
				return;
			}

			static FManagedThunkCache ExecuteCache;
			void* FoundThunk = nullptr;
			if (!GetExecuteThunk(ExecuteCache, FoundThunk))
			{
				return;
			}

			void* const StateHandle = const_cast<void*>(InStateHandle);
			if (IsDebugSingleThread())
			{
				for (int32 TaskIndex = 0; TaskIndex < InTaskCount; ++TaskIndex)
				{
					ExecuteOneTask(StateHandle, FoundThunk, TaskIndex);
				}
				return;
			}

			TArray<UE::Tasks::FTask> TaskList;
			TaskList.Reserve(InTaskCount);

			for (int32 TaskIndex = 0; TaskIndex < InTaskCount; ++TaskIndex)
			{
				UE::Tasks::FTask Task;
				Task.Launch(TEXT("UETasksQuery.ExecuteBatch"), [StateHandle, FoundThunk, TaskIndex]()
				{
					FManagedJobScope ManagedScope;

					if (!ManagedScope.IsEntered())
					{
						return;
					}

					ExecuteOneTask(StateHandle, FoundThunk, TaskIndex);
				});

				TaskList.Add(MoveTemp(Task));
			}

			if (bWait)
			{
				UE::Tasks::Wait(TaskList);
			}
		}

		static int64 ScheduleBatchImplementation(const void* InStateHandle, const int32 InTaskCount)
		{
			if (InTaskCount <= 0)
			{
				return 0;
			}

			if (!ValidateManagedContext())
			{
				return 0;
			}

			static FManagedThunkCache ExecuteCache;
			void* FoundThunk = nullptr;
			if (!GetExecuteThunk(ExecuteCache, FoundThunk))
			{
				return 0;
			}

			void* const StateHandle = const_cast<void*>(InStateHandle);
			if (IsDebugSingleThread())
			{
				for (int32 TaskIndex = 0; TaskIndex < InTaskCount; ++TaskIndex)
				{
					ExecuteOneTask(StateHandle, FoundThunk, TaskIndex);
				}
				return 0;
			}

			TArray<UE::Tasks::FTask> TaskList;
			TaskList.Reserve(InTaskCount);

			for (int32 TaskIndex = 0; TaskIndex < InTaskCount; ++TaskIndex)
			{
				UE::Tasks::FTask Task;
				Task.Launch(TEXT("UETasksQuery.ScheduleBatch"), [StateHandle, FoundThunk, TaskIndex]()
				{
					FManagedJobScope ManagedScope;

					if (!ManagedScope.IsEntered())
					{
						return;
					}

					ExecuteOneTask(StateHandle, FoundThunk, TaskIndex);
				});

				TaskList.Add(MoveTemp(Task));
			}

			const int64 HandleId = NextHandleId.fetch_add(1);
			{
				FScopeLock ScopeLock(&HandleMutex);
				HandleTasks.Add(HandleId, MoveTemp(TaskList));
			}

			return HandleId;
		}

		static bool IsHandleCompletedImplementation(const int64 InHandleId)
		{
			if (InHandleId <= 0)
			{
				return true;
			}

			FScopeLock ScopeLock(&HandleMutex);
			const auto TasksPtr = HandleTasks.Find(InHandleId);
			if (TasksPtr == nullptr)
			{
				return true;
			}

			for (const auto& Task : *TasksPtr)
			{
				if (!Task.IsCompleted())
				{
					return false;
				}
			}

			return true;
		}

		static void WaitHandleImplementation(const int64 InHandleId)
		{
			if (InHandleId <= 0)
			{
				return;
			}

			TArray<UE::Tasks::FTask> TasksCopy;
			{
				FScopeLock ScopeLock(&HandleMutex);
				const auto TasksPtr = HandleTasks.Find(InHandleId);
				if (TasksPtr == nullptr)
				{
					return;
				}
				TasksCopy = *TasksPtr;
			}

			if (TasksCopy.Num() > 0)
			{
				UE::Tasks::Wait(TasksCopy);
			}
		}

		static void ReleaseHandleImplementation(const int64 InHandleId)
		{
			if (InHandleId <= 0)
			{
				return;
			}

			TArray<UE::Tasks::FTask> TaskList;
			{
				FScopeLock ScopeLock(&HandleMutex);
				const auto TasksPtr = HandleTasks.Find(InHandleId);
				if (TasksPtr == nullptr)
				{
					return;
				}
				TaskList = MoveTemp(*TasksPtr);
				HandleTasks.Remove(InHandleId);
			}

			if (TaskList.Num() > 0)
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
				.Function(TEXT("ScheduleBatch"), ScheduleBatchImplementation)
				.Function(TEXT("IsHandleCompleted"), IsHandleCompletedImplementation)
				.Function(TEXT("WaitHandle"), WaitHandleImplementation)
				.Function(TEXT("ReleaseHandle"), ReleaseHandleImplementation)
				.Function(TEXT("GetNumWorkerThreads"), GetNumWorkerThreadsImplementation)
				.Function(TEXT("GetCurrentNativeThreadId"), GetCurrentNativeThreadIdImplementation);
		}
	};

	std::atomic<int64> FTasksQuery::NextHandleId{1};
	FCriticalSection FTasksQuery::HandleMutex;
	TMap<int64, TArray<UE::Tasks::FTask>> FTasksQuery::HandleTasks;

	[[maybe_unused]] FTasksQuery TasksQuery;
}
