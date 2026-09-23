using System.Runtime.CompilerServices;

using CheatEngine.Client.Core.Dispatching;
using CheatEngine.Client.Core.Domains;
using CheatEngine.Client.Core.Infrastructure;
using CheatEngine.Client.Core.Tests.TestSupport;
using CheatEngine.Client.Memory;
using CheatEngine.Client.Results;
using CheatEngine.SDK.Engine.Errors;
using CheatEngine.SDK.Engine.Inspection;
using CheatEngine.SDK.Engine.Memory;
using CheatEngine.SDK.Engine.Values;
using CheatEngine.SDK.Lua.Calls;

namespace CheatEngine.Client.Core.Tests.SdkContract;

/// <summary>
///     Proves that every SDK enum value and exception type the Client translates maps to a known Client failure kind (Q48,
///     A11-30): a new value or type in a candidate SDK package fails here instead of silently becoming <c>Unknown</c>.
/// </summary>
public sealed class SdkMappingContractTests
{
	[Fact]
	[Trait("Qualification", "Q48")]
	public void EveryPublicEngineExceptionTypeMapsToAKnownFailureKind()
	{
		Type[] exceptionTypes =
		[
			.. typeof(EngineException).Assembly.GetExportedTypes()
				.Where(static type => typeof(EngineException).IsAssignableFrom(type) && !type.IsAbstract),
			typeof(LuaException)
		];
		List<string> unmapped = [];
		foreach (Type exceptionType in exceptionTypes)
		{
			Exception instance = (Exception) RuntimeHelpers.GetUninitializedObject(exceptionType);
			if (CoreFailureFactory.GetKind(instance) == CheatEngineFailureKind.Unknown)
			{
				unmapped.Add(exceptionType.FullName!);
			}
		}

		Assert.True(exceptionTypes.Length >= 7, "The SDK exception inventory unexpectedly shrank.");
		Assert.True(unmapped.Count == 0,
			"These SDK exception types map to CheatEngineFailureKind.Unknown: " + string.Join(", ", unmapped));
	}

	[Fact]
	[Trait("Qualification", "Q48")]
	public void EveryInspectionStatusMapsToAKnownFailureKind()
	{
		List<string> unmapped = [];
		foreach (InspectionStatus status in Enum.GetValues<InspectionStatus>())
		{
			bool succeeded = InspectionClient.TryMap(status, "Inspection.Contract", out CheatEngineFailure failure);
			if (status == InspectionStatus.Success)
			{
				Assert.True(succeeded);
				continue;
			}

			if (succeeded || failure.Kind == CheatEngineFailureKind.Unknown)
			{
				unmapped.Add(status.ToString());
			}
		}

		Assert.True(unmapped.Count == 0, "These InspectionStatus values are not mapped: " + string.Join(", ", unmapped));
	}

	[Fact]
	[Trait("Qualification", "Q48")]
	public void EveryMemoryAccessFailureIsReportedAsAMemoryFailure()
	{
		CoreLifetime lifetime = InertCoreLifetime.Create();
		foreach (MemoryAccessFailure access in Enum.GetValues<MemoryAccessFailure>().Where(static value =>
					 value != MemoryAccessFailure.None))
		{
			RefusingPort port = new(access.ToString());
			MemoryClient client = new(new SdkMainThreadDispatcher(lifetime, new InlineMainThreadInvoker()), lifetime, port);

			Assert.False(client.TryReadBytes(new MemoryBytesReadRequest(0x1000, 4), out _,
				out CheatEngineFailure readFailure, TestContext.Current.CancellationToken));
			Assert.False(client.TryWriteBytes(new MemoryBytesWriteRequest(0x1000, [1]),
				out CheatEngineFailure writeFailure, TestContext.Current.CancellationToken));

			Assert.Equal(CheatEngineFailureKind.MemoryReadFailed, readFailure.Kind);
			Assert.Equal(CheatEngineFailureKind.MemoryWriteFailed, writeFailure.Kind);
			Assert.Equal(access.ToString(), readFailure.Message);
		}
	}

	/// <summary>Reports every access as refused with the SDK's own failure name, as <c>SdkMemoryCodecContextPort</c> does.</summary>
	private sealed class RefusingPort(string failure) : IMemoryCodecContextPort
	{
		public long GetOpenedProcessId()
		{
			return 42;
		}

		public bool TargetIs64Bit()
		{
			return true;
		}

		public bool TargetIsX86()
		{
			return true;
		}

		public bool TargetIsArm()
		{
			return false;
		}

		public int GetConfiguredPointerSize()
		{
			return sizeof(ulong);
		}

		public bool TryReadBytes(Address address, Span<byte> destination, out string? hostFailure)
		{
			hostFailure = failure;
			return false;
		}

		public bool TryWriteBytes(Address address, ReadOnlySpan<byte> source, out string? hostFailure)
		{
			hostFailure = failure;
			return false;
		}
	}
}
