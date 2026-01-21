#include "CoreMinimal.h"
#include "Binding/Class/FClassBuilder.h"
#include "CoreMacro/NamespaceMacro.h"
#include "Domain/FMonoDomain.h"
#include "Tasks/Task.h"
#include "Misc/ScopeLock.h"

namespace
{
	struct FTasksSlice
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

		static void ExecuteBatchImplementation(const void* InData,
		                                       const int32 InLength,
		                                       const int32 InTaskCount,
		                                       const bool bWait)
		{
			if (InData == nullptr || InLength <= 0 || InTaskCount <= 0)
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
			const auto FoundThunk = GetManagedThunkCached(ExecuteCache, TEXT("UETasksSliceBatch"), TEXT("ExecuteSlice"), 3);

			if (FoundThunk == nullptr)
			{
				return;
			}

			const int32 SafeTaskCount = FMath::Clamp(InTaskCount, 1, InLength);
			const int32 ChunkSize = FMath::DivideAndRoundUp(InLength, SafeTaskCount);

			TArray<UE::Tasks::FTask> TaskList;
			TaskList.Reserve(SafeTaskCount);

			void* const DataPtr = const_cast<void*>(InData);

			for (int32 TaskIndex = 0; TaskIndex < SafeTaskCount; ++TaskIndex)
			{
				const int32 StartIndex = TaskIndex * ChunkSize;
				const int32 Count = FMath::Min(ChunkSize, InLength - StartIndex);

				if (Count <= 0)
				{
					continue;
				}

				UE::Tasks::FTask Task;
				Task.Launch(TEXT("UETasksSlice.ExecuteBatch"), [DataPtr, StartIndex, Count, FoundThunk]()
				{
					FManagedJobScope ManagedScope;

					if (!ManagedScope.IsEntered())
					{
						return;
					}

					void* DataParam = DataPtr;
					int32 StartParam = StartIndex;
					int32 CountParam = Count;

					using FExecuteSliceThunk = void (*)(void*, int32, int32, MonoObject**);
					const auto Thunk = reinterpret_cast<FExecuteSliceThunk>(FoundThunk);

					MonoObject* Exception = nullptr;

					Thunk(DataParam, StartParam, CountParam, &Exception);

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

		FTasksSlice()
		{
			FClassBuilder(TEXT("FTasksSlice"), NAMESPACE_LIBRARY)
				.Function(TEXT("ExecuteBatch"), ExecuteBatchImplementation);
		}
	};

	[[maybe_unused]] FTasksSlice TasksSlice;
}
