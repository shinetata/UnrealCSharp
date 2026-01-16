#include "Binding/Class/FClassBuilder.h"
#include "CoreMacro/NamespaceMacro.h"

namespace
{
	struct FNativeBufferKernel
	{
		static int32 AddOneAndSumInt32Implementation(const void* InData, const int32 InLength)
		{
			if (InData == nullptr || InLength <= 0)
			{
				return 0;
			}

			int32* Data = static_cast<int32*>(const_cast<void*>(InData));

			int64 Sum = 0;
			for (int32 i = 0; i < InLength; ++i)
			{
				Data[i] += 1;
				Sum += Data[i];
			}

			return static_cast<int32>(Sum);
		}

		FNativeBufferKernel()
		{
			FClassBuilder(TEXT("FNativeBufferKernel"), NAMESPACE_LIBRARY)
				.Function(TEXT("AddOneAndSumInt32"), AddOneAndSumInt32Implementation);
		}
	};

	[[maybe_unused]] FNativeBufferKernel NativeBufferKernel;
}

