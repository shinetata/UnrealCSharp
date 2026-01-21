#include "CoreMinimal.h"
#include "Binding/Class/FClassBuilder.h"
#include "CoreMacro/NamespaceMacro.h"
#include "Domain/FMonoDomain.h"
#include "Tasks/Task.h"
#include "Misc/ScopeLock.h"

namespace
{
	struct FTasks
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

			TArray<UE::Tasks::FTask> Tasks;
			Tasks.Reserve(InTaskCount);

			void* const StateHandle = const_cast<void*>(InStateHandle);

			for (int32 Index = 0; Index < InTaskCount; ++Index)
			{
				UE::Tasks::FTask Task;
				Task.Launch(TEXT("UETasks.ExecuteBatch"), [StateHandle, Index, InExecuteTaskMethod]()
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
				});

				Tasks.Add(MoveTemp(Task));
			}

			if (bWait)
			{
				UE::Tasks::Wait(Tasks);
			}
		}

		static void ExecuteBatchImplementation(const void* InStateHandle, const int32 InTaskCount, const bool bWait)
		{
			static FManagedMethodCache Cache;
			const auto FoundMethod = GetExecuteTaskMethodCached(Cache, TEXT("UETasksBatch"));
			ExecuteBatchWithMethod(InStateHandle, InTaskCount, bWait, FoundMethod);
		}

		FTasks()
		{
			FClassBuilder(TEXT("FTasks"), NAMESPACE_LIBRARY)
				.Function(TEXT("ExecuteBatch"), ExecuteBatchImplementation);
		}
	};

	[[maybe_unused]] FTasks Tasks;
}
