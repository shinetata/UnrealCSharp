#include "CoreMinimal.h"
#include "Binding/Class/FClassBuilder.h"
#include "CoreMacro/NamespaceMacro.h"
#include "Domain/FMonoDomain.h"
#include "Tasks/Task.h"
#include "Misc/ScopeLock.h"
#include "HAL/PlatformTLS.h"
#include "Log/UnrealCSharpLog.h"

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

		struct FDelegateThunkCache
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

		static void* GetDelegateThunkCached(FDelegateThunkCache& Cache, MonoObject* InDelegate)
		{
			if (InDelegate == nullptr)
			{
				return nullptr;
			}

			const auto FoundMethod = FMonoDomain::Delegate_Get_Method(InDelegate);

			if (FoundMethod == nullptr)
			{
				return nullptr;
			}

			const uint64 Key = GetManagedLookupCacheKey() ^ reinterpret_cast<uint64>(FoundMethod);

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

			const auto Signature = FMonoDomain::Method_Signature(FoundMethod);

			if (Signature == nullptr)
			{
				return nullptr;
			}

			// 只支持 static：避免 instance/closure 带来的 target/this 传递与保活复杂度。
			if (FMonoDomain::Signature_Is_Instance(Signature))
			{
				return nullptr;
			}

			// 只支持固定签名：void Handler(nint data, int start, int count)
			if (FMonoDomain::Signature_Get_Param_Count(Signature) != 3)
			{
				return nullptr;
			}

			Cache.CachedThunk = FMonoDomain::Method_Get_Unmanaged_Thunk(FoundMethod);

			return Cache.CachedThunk;
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

		static void ExecuteBatchWithHandlerImplementation(const void* InData,
		                                                  const int32 InLength,
		                                                  const int32 InTaskCount,
		                                                  const bool bWait,
		                                                  MonoObject* InHandler)
		{
			if (InData == nullptr || InLength <= 0 || InTaskCount <= 0)
			{
				return;
			}

			// 为了避免 data/unpin 与 delegate 生命周期问题，当前只支持同步等待。
			if (!bWait)
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

			static FDelegateThunkCache HandlerCache;
			const auto FoundThunk = GetDelegateThunkCached(HandlerCache, InHandler);

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
				Task.Launch(TEXT("UETasksSlice.ExecuteBatchWithHandler"), [DataPtr, StartIndex, Count, FoundThunk]()
				{
					FManagedJobScope ManagedScope;

					if (!ManagedScope.IsEntered())
					{
						return;
					}

					void* DataParam = DataPtr;
					int32 StartParam = StartIndex;
					int32 CountParam = Count;

					using FHandlerThunk = void (*)(void*, int32, int32, MonoObject**);
					const auto Thunk = reinterpret_cast<FHandlerThunk>(FoundThunk);

					MonoObject* Exception = nullptr;
					Thunk(DataParam, StartParam, CountParam, &Exception);

					if (Exception != nullptr)
					{
						FMonoDomain::Unhandled_Exception(Exception);
					}
				});

				TaskList.Add(MoveTemp(Task));
			}

			UE::Tasks::Wait(TaskList);
		}

		// 极简示例：C++ 接收 C# delegate（MonoObject*），在 UE::Tasks worker 线程用 Runtime_Invoke 执行它。
		// 约束：delegate 必须指向 static 方法，且签名固定为：void Handler()
		static void ExecuteBatchWithDelegateInvokeImplementation(MonoObject* InDelegate, const bool bWait)
		{
			if (InDelegate == nullptr)
			{
				return;
			}

			// 为了避免 delegate 生命周期与 Domain 重载时序问题，这个极简示例只支持同步等待。
			if (!bWait)
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

			MonoMethod* const FoundMethod = FMonoDomain::Delegate_Get_Method(InDelegate);

			if (FoundMethod == nullptr)
			{
				return;
			}

			const auto Signature = FMonoDomain::Method_Signature(FoundMethod);

			if (Signature == nullptr)
			{
				return;
			}

			// 极简示例：仅支持 static，避免 target/this 传递与保活复杂度。
			if (FMonoDomain::Signature_Is_Instance(Signature))
			{
				return;
			}

			if (FMonoDomain::Signature_Get_Param_Count(Signature) != 0)
			{
				return;
			}

			UE_LOG(LogUnrealCSharp, Log,
			       TEXT("[UETasksSliceDelegateInvoke] schedule (GT=%d tid=%d)"),
			       IsInGameThread() ? 1 : 0,
			       static_cast<int32>(FPlatformTLS::GetCurrentThreadId()));

			UE::Tasks::FTask Task;
			Task.Launch(TEXT("UETasksSlice.ExecuteBatchWithDelegateInvoke"), [FoundMethod]()
			{
				FManagedJobScope ManagedScope;

				if (!ManagedScope.IsEntered())
				{
					return;
				}

				UE_LOG(LogUnrealCSharp, Log,
				       TEXT("[UETasksSliceDelegateInvoke] invoke on worker (GT=%d tid=%d)"),
				       IsInGameThread() ? 1 : 0,
				       static_cast<int32>(FPlatformTLS::GetCurrentThreadId()));

				MonoObject* Exception = nullptr;
				(void)FMonoDomain::Runtime_Invoke(FoundMethod, nullptr, nullptr, &Exception);

				if (Exception != nullptr)
				{
					FMonoDomain::Unhandled_Exception(Exception);
				}
			});

			UE::Tasks::Wait(Task);

			UE_LOG(LogUnrealCSharp, Log,
			       TEXT("[UETasksSliceDelegateInvoke] done (GT=%d tid=%d)"),
			       IsInGameThread() ? 1 : 0,
			       static_cast<int32>(FPlatformTLS::GetCurrentThreadId()));
		}

		FTasksSlice()
		{
			FClassBuilder(TEXT("FTasksSlice"), NAMESPACE_LIBRARY)
				.Function(TEXT("ExecuteBatch"), ExecuteBatchImplementation)
				.Function(TEXT("ExecuteBatchWithHandler"), ExecuteBatchWithHandlerImplementation)
				.Function(TEXT("ExecuteBatchWithDelegateInvoke"), ExecuteBatchWithDelegateInvokeImplementation);
		}
	};

	[[maybe_unused]] FTasksSlice TasksSlice;
}
