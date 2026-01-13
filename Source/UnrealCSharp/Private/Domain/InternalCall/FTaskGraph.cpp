#include "Binding/Class/FClassBuilder.h"
#include "CoreMacro/NamespaceMacro.h"
#include "Domain/FMonoDomain.h"
#include "HAL/PlatformTLS.h"
#include "Async/TaskGraphInterfaces.h"

namespace
{
	struct FTaskGraph
	{
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
				.Function(TEXT("EnqueueProbe"), EnqueueProbeImplementation);
		}
	};

	[[maybe_unused]] FTaskGraph TaskGraph;
}
