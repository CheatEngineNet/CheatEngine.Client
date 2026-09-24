using System.Collections.Immutable;
using System.Runtime.CompilerServices;

using CheatEngine.Client.Core.Dispatching;
using CheatEngine.Client.Core.Domains;
using CheatEngine.Client.Core.Infrastructure;
using CheatEngine.Client.Core.Tests.TestSupport;
using CheatEngine.Client.Memory;
using CheatEngine.Client.Results;
using CheatEngine.SDK.Annotations.Lua;
using CheatEngine.SDK.Engine.Errors;
using CheatEngine.SDK.Engine.Inspection;
using CheatEngine.SDK.Engine.Memory;
using CheatEngine.SDK.Engine.Objects;
using CheatEngine.SDK.Engine.Scanning.Values;
using CheatEngine.SDK.Engine.Values;
using CheatEngine.SDK.Hosting.Plugin;
using CheatEngine.SDK.Lua.Calls;
using CheatEngine.SDK.Lua.Runtime;

namespace CheatEngine.Client.Core.Tests.SdkContract;

/// <summary>
///     Proves that every SDK enum value and exception type the Client translates maps to a known Client failure kind (Q48,
///     A11-30): a new value or type in a candidate SDK package fails here instead of silently becoming <c>Unknown</c>.
/// </summary>
public sealed class SdkMappingContractTests
{
	/// <summary>
	///     The Client failure kind and host effects of every SDK memory failure (plan L10). A pointer value wider than the
	///     target is refused after Cheat Engine returned it on a read, and before Cheat Engine is called on a write.
	/// </summary>
	private static Dictionary<MemoryAccessFailure, ExpectedMemoryFailure> ExpectedMemoryFailures => new()
	{
		[MemoryAccessFailure.None] = new ExpectedMemoryFailure(CheatEngineFailureKind.IndeterminateHostResult,
			CheatEngineHostEffect.Unknown, CheatEngineHostEffect.Unknown),
		[MemoryAccessFailure.GlobalUnavailable] = new ExpectedMemoryFailure(
			CheatEngineFailureKind.CapabilityUnavailable, CheatEngineHostEffect.NotStarted,
			CheatEngineHostEffect.NotStarted),
		[MemoryAccessFailure.LuaError] = new ExpectedMemoryFailure(CheatEngineFailureKind.LuaError,
			CheatEngineHostEffect.Unknown, CheatEngineHostEffect.Unknown),
		[MemoryAccessFailure.ReadFailed] = new ExpectedMemoryFailure(CheatEngineFailureKind.MemoryReadFailed,
			CheatEngineHostEffect.Unknown, CheatEngineHostEffect.Unknown),
		[MemoryAccessFailure.PartialRead] = new ExpectedMemoryFailure(CheatEngineFailureKind.MemoryReadFailed,
			CheatEngineHostEffect.Unknown, CheatEngineHostEffect.Unknown),
		[MemoryAccessFailure.DestinationTooSmall] = new ExpectedMemoryFailure(
			CheatEngineFailureKind.ResultLimitExceeded, CheatEngineHostEffect.Unknown, CheatEngineHostEffect.Unknown),
		[MemoryAccessFailure.PointerWidthUnknown] = new ExpectedMemoryFailure(CheatEngineFailureKind.InvalidState,
			CheatEngineHostEffect.NotStarted, CheatEngineHostEffect.NotStarted),
		[MemoryAccessFailure.PointerValueExceedsTargetWidth] = new ExpectedMemoryFailure(
			CheatEngineFailureKind.OperationRejected, CheatEngineHostEffect.Completed, CheatEngineHostEffect.NotStarted),
		[MemoryAccessFailure.WriteFailed] = new ExpectedMemoryFailure(CheatEngineFailureKind.MemoryWriteFailed,
			CheatEngineHostEffect.Unknown, CheatEngineHostEffect.Unknown),
		[MemoryAccessFailure.InvalidResult] = new ExpectedMemoryFailure(CheatEngineFailureKind.InvalidHostResult,
			CheatEngineHostEffect.Unknown, CheatEngineHostEffect.Unknown)
	};

	/// <summary>The public exception types of the consumed CheatEngine.SDK: every EngineException subclass, the
	///     memory-scan exceptions and the Lua call exception.</summary>
	private static Type[] SdkExceptionTypes =>
	[
		.. new[]
			{
				typeof(EngineException).Assembly, typeof(LuaException).Assembly, typeof(CheatEnginePlugin).Assembly,
				typeof(LuaGlobalAttribute).Assembly
			}
			.SelectMany(static assembly => assembly.GetExportedTypes())
			.Where(static type => typeof(Exception).IsAssignableFrom(type) && !type.IsAbstract)
			.OrderBy(static type => type.FullName, StringComparer.Ordinal)
	];

	[Fact]
	[Trait("Qualification", "Q48")]
	public void EveryPublicSdkExceptionTypeMapsToAKnownFailureKind()
	{
		Type[] exceptionTypes = SdkExceptionTypes;
		List<string> unmapped = [];
		foreach (Type exceptionType in exceptionTypes)
		{
			Exception instance = (Exception) RuntimeHelpers.GetUninitializedObject(exceptionType);
			if (CoreFailureFactory.GetKind(instance) == CheatEngineFailureKind.Unknown)
			{
				unmapped.Add(exceptionType.FullName!);
			}
		}

		Assert.True(exceptionTypes.Length >= 13, "The SDK exception inventory unexpectedly shrank.");
		Assert.Contains(typeof(MemoryScanException), exceptionTypes);
		Assert.Contains(typeof(MemoryScanStateException), exceptionTypes);
		Assert.Contains(typeof(EngineTargetIdentityException), exceptionTypes);
		Assert.True(unmapped.Count == 0,
			"These SDK exception types map to CheatEngineFailureKind.Unknown: " + string.Join(", ", unmapped));
	}

	[Fact]
	[Trait("Qualification", "Q48")]
	public void EveryEngineExceptionSubclassIsClassifiedByItsOwnFailureCategory()
	{
		Type[] engineExceptions =
			[.. SdkExceptionTypes.Where(static type => typeof(EngineException).IsAssignableFrom(type))];
		List<string> mismatched = [];
		foreach (Type exceptionType in engineExceptions)
		{
			EngineException instance = (EngineException) RuntimeHelpers.GetUninitializedObject(exceptionType);
			if (CoreFailureFactory.GetKind(instance) != CoreFailureFactory.FromEngineFailureKind(instance.Kind))
			{
				mismatched.Add(exceptionType.FullName!);
			}
		}

		Assert.True(engineExceptions.Length >= 10, "The SDK EngineException inventory unexpectedly shrank.");
		Assert.True(mismatched.Count == 0,
			"These EngineException types are not classified by EngineException.Kind: " + string.Join(", ", mismatched));
	}

	[Fact]
	[Trait("Qualification", "Q48")]
	public void EveryEngineFailureKindMapsToAKnownFailureKind()
	{
		MappingTotality.AssertTotal<EngineFailureKind>(
			static kind => CoreFailureFactory.FromEngineFailureKind(kind) != CheatEngineFailureKind.Unknown,
			static kind => CoreFailureFactory.FromEngineFailureKind(kind) == CheatEngineFailureKind.Unknown);
	}

	[Fact]
	[Trait("Qualification", "Q48")]
	public void EveryMemoryScanFailureKindMapsToAKnownFailureKind()
	{
		MappingTotality.AssertTotal<MemoryScanFailureKind>(
			static kind => CoreFailureFactory.FromMemoryScanFailureKind(kind) != CheatEngineFailureKind.Unknown,
			static kind => CoreFailureFactory.FromMemoryScanFailureKind(kind) == CheatEngineFailureKind.Unknown);
	}

	[Fact]
	[Trait("Qualification", "Q48")]
	public void EveryLuaAdmissionStatusIsClassifiedAndOnlyAdmittedSucceeds()
	{
		MappingTotality.AssertTotal<LuaAdmissionStatus>(IsClassifiedAdmission, static status =>
			!LuaAdmission.TryClassify(status, "Lua.Contract", out CheatEngineFailure failure) &&
			failure.Kind == CheatEngineFailureKind.InvalidState &&
			failure.HostEffect == CheatEngineHostEffect.NotStarted);
	}

	[Fact]
	[Trait("Qualification", "Q48")]
	public void EveryEngineEffectStateMapsToItsHostEffect()
	{
		MappingTotality.AssertTotal<EngineEffectState>(
			static state => state == EngineEffectState.Unknown
				? HostEffectMapping.FromSdk(state) == CheatEngineHostEffect.Unknown
				: HostEffectMapping.FromSdk(state) != CheatEngineHostEffect.Unknown,
			static state => HostEffectMapping.FromSdk(state) == CheatEngineHostEffect.Unknown);
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
	public void EveryMemoryAccessFailureMapsToItsClientKindAndHostEffect()
	{
		MappingTotality.AssertTotal<MemoryAccessFailure>(
			static access => ExpectedMemoryFailures.TryGetValue(access, out ExpectedMemoryFailure expected) &&
							 MemoryAccessFailureMapping.ToFailureKind(access) == expected.Kind &&
							 MemoryAccessFailureMapping.ToHostEffect(access, false) == expected.ReadEffect &&
							 MemoryAccessFailureMapping.ToHostEffect(access, true) == expected.WriteEffect,
			static access =>
				MemoryAccessFailureMapping.ToFailureKind(access) == CheatEngineFailureKind.IndeterminateHostResult &&
				MemoryAccessFailureMapping.ToHostEffect(access, false) == CheatEngineHostEffect.Unknown &&
				MemoryAccessFailureMapping.ToHostEffect(access, true) == CheatEngineHostEffect.Unknown);
	}

	/// <summary>
	///     End to end through <see cref="MemoryClient" />: every SDK failure of a byte read and a byte write reaches the
	///     caller with its mapped kind and effect, and none of them, <see cref="MemoryAccessFailure.None" /> included,
	///     is ever a success.
	/// </summary>
	[Fact]
	[Trait("Qualification", "Q20")]
	public void EveryMemoryAccessFailureReachesTheCallerWithItsMappedKindAndNeverSucceeds()
	{
		CoreLifetime lifetime = InertCoreLifetime.Create();
		foreach (MemoryAccessFailure access in Enum.GetValues<MemoryAccessFailure>())
		{
			RefusingPort port = new(access);
			MemoryClient client = new(new SdkMainThreadDispatcher(lifetime, new InlineMainThreadInvoker()), lifetime, port);
			ExpectedMemoryFailure expected = ExpectedMemoryFailures[access];

			Assert.False(client.TryReadBytes(new MemoryBytesReadRequest(0x1000, 4), out ImmutableArray<byte> bytes,
				out CheatEngineFailure readFailure, TestContext.Current.CancellationToken));
			Assert.False(client.TryWriteBytes(new MemoryBytesWriteRequest(0x1000, [1]),
				out CheatEngineFailure writeFailure, TestContext.Current.CancellationToken));

			Assert.True(bytes.IsEmpty);
			Assert.Equal((expected.Kind, expected.ReadEffect, "Memory.ReadBytes"),
				(readFailure.Kind, readFailure.HostEffect, readFailure.Operation));
			Assert.Equal((expected.Kind, expected.WriteEffect, "Memory.WriteBytes"),
				(writeFailure.Kind, writeFailure.HostEffect, writeFailure.Operation));
		}
	}

	/// <summary>
	///     Admitted is the only success; every refusal is NotStarted and one of the three admission kinds, never a
	///     rejection.
	/// </summary>
	private static bool IsClassifiedAdmission(LuaAdmissionStatus status)
	{
		bool admitted = LuaAdmission.TryClassify(status, "Lua.Contract", out CheatEngineFailure failure);
		if (status == LuaAdmissionStatus.Admitted)
		{
			return admitted && failure == default;
		}

		return !admitted &&
			   failure.Kind is CheatEngineFailureKind.ActivationExpired or CheatEngineFailureKind.InvalidState
				   or CheatEngineFailureKind.RuntimeChanged &&
			   failure.HostEffect == CheatEngineHostEffect.NotStarted &&
			   failure.Operation == "Lua.Contract";
	}

	/// <summary>Reports every access as refused with one SDK failure, as <c>SdkMemoryCodecContextPort</c> passes it on.</summary>
	private sealed class RefusingPort(MemoryAccessFailure failure) : TargetObservationDouble, IMemoryCodecContextPort
	{
		public bool TryReadBytes(Address address, Span<byte> destination, out MemoryAccessFailure hostFailure)
		{
			hostFailure = failure;
			return false;
		}

		public bool TryWriteBytes(Address address, ReadOnlySpan<byte> source, out MemoryAccessFailure hostFailure)
		{
			hostFailure = failure;
			return false;
		}
	}

	private readonly record struct ExpectedMemoryFailure(
		CheatEngineFailureKind Kind,
		CheatEngineHostEffect ReadEffect,
		CheatEngineHostEffect WriteEffect);
}
