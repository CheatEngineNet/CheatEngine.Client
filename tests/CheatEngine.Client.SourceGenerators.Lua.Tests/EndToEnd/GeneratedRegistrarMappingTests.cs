using System.Globalization;
using System.Reflection;

using CheatEngine.Client.Results;
using CheatEngine.SDK.Lua.Registration;
using CheatEngine.SDK.Lua.Runtime;

namespace CheatEngine.Client.SourceGenerators.Lua.Tests.EndToEnd;

/// <summary>
///     Totality of the mappings the generated registrar applies to CheatEngine.SDK outcome enums: every value the
///     consumed SDK defines reaches its dedicated Client value, and a value a later SDK could add fails closed. The
///     mappings are read from the real generated registrar of the EndToEnd harness, never from a copy.
/// </summary>
public sealed class GeneratedRegistrarMappingTests
{
	// The registrar maps only the release of a lease it has consumed, so no kind may be retryable.
	private static readonly Dictionary<LuaRegistrationReleaseKind, LeaseReleaseKind> ReleaseKinds = new()
	{
		// The SDK's value before any release: outside the result of one, and the lease is consumed all the same.
		[LuaRegistrationReleaseKind.NotAttempted] = LeaseReleaseKind.CleanupUnconfirmed,
		[LuaRegistrationReleaseKind.Released] = LeaseReleaseKind.Released,
		[LuaRegistrationReleaseKind.PartiallyReleased] = LeaseReleaseKind.PartiallyReleased,
		// The SDK counts Stale as complete (no cleanup call failed), but it reports every entry as remaining: a release
		// after re-enable leaves the earlier attachment's functions in the same Lua state. RequiresManualRecovery holds.
		[LuaRegistrationReleaseKind.Stale] = LeaseReleaseKind.RefusedRuntimeChanged,
		[LuaRegistrationReleaseKind.AlreadyReleased] = LeaseReleaseKind.AlreadyReleased
	};

	private static readonly Dictionary<LuaAdmissionStatus, CheatEngineFailureKind> Admissions = new()
	{
		[LuaAdmissionStatus.Unknown] = CheatEngineFailureKind.IndeterminateHostResult,
		[LuaAdmissionStatus.Detached] = CheatEngineFailureKind.ActivationExpired,
		[LuaAdmissionStatus.TransitionInProgress] = CheatEngineFailureKind.ActivationExpired,
		[LuaAdmissionStatus.NoStateForThread] = CheatEngineFailureKind.InvalidState,
		[LuaAdmissionStatus.ThreadNotAdmitted] = CheatEngineFailureKind.InvalidState,
		[LuaAdmissionStatus.ExternalStateReset] = CheatEngineFailureKind.RuntimeChanged
	};

	private static readonly Dictionary<LuaRegistrationResultKind, CheatEngineFailureKind> ResultKinds = new()
	{
		[LuaRegistrationResultKind.Unspecified] = CheatEngineFailureKind.IndeterminateHostResult,
		// Reached only for a success that carries no registration lease.
		[LuaRegistrationResultKind.Succeeded] = CheatEngineFailureKind.IndeterminateHostResult,
		[LuaRegistrationResultKind.Collision] = CheatEngineFailureKind.OperationRejected,
		[LuaRegistrationResultKind.PreflightFailed] = CheatEngineFailureKind.LuaError,
		[LuaRegistrationResultKind.PublicationFailed] = CheatEngineFailureKind.LuaError
	};

	private static ModuleHarness Harness => ModuleHarness.Shared;

	[Fact]
	public void EveryLuaRegistrationReleaseKindIsMappedAndAnUnknownKindFailsClosed()
	{
		AssertTotal(ReleaseKinds, "MapReleaseKind", LeaseReleaseKind.CleanupUnconfirmed);
		Assert.True(new LeaseReleaseOutcome(LeaseReleaseKind.RefusedRuntimeChanged, CheatEngineHostEffect.NotStarted)
			.RequiresManualRecovery);
		Assert.True(new LeaseReleaseOutcome(LeaseReleaseKind.CleanupUnconfirmed, CheatEngineHostEffect.Started)
			.RequiresManualRecovery);
		Assert.DoesNotContain(ReleaseKinds.Values, static kind => kind is LeaseReleaseKind.Unknown
			or LeaseReleaseKind.CleanupUnavailable);
	}

	[Fact]
	public void EveryRefusedLuaAdmissionIsClassifiedAndAnUnknownStatusFailsClosed()
	{
		AssertTotal(Admissions, "MapAdmission", CheatEngineFailureKind.IndeterminateHostResult,
			LuaAdmissionStatus.Admitted);
	}

	[Fact]
	public void EveryLuaRegistrationResultKindIsClassifiedAndAnUnknownKindFailsClosed()
	{
		AssertTotal(ResultKinds, "MapResultKind", CheatEngineFailureKind.IndeterminateHostResult);
	}

	private static void AssertTotal<TSdk, TClient>(Dictionary<TSdk, TClient> expected, string method,
		TClient fallback, params TSdk[] notMapped)
		where TSdk : struct, Enum
	{
		TSdk[] values = [.. Enum.GetValues<TSdk>().Except(notMapped)];
		Assert.Equal(values.Order(), expected.Keys.Order());
		foreach (TSdk value in values)
		{
			Assert.Equal(expected[value], Harness.InvokeRegistrar<TClient>(method, value));
		}

		long largest = Enum.GetValues<TSdk>().Select(static value => Convert.ToInt64(value, CultureInfo.InvariantCulture))
			.Max();
		TSdk undefined = (TSdk) Enum.ToObject(typeof(TSdk), largest + 1);
		Assert.False(Enum.IsDefined(undefined));
		Assert.Equal(fallback, Harness.InvokeRegistrar<TClient>(method, undefined));
		Assert.NotNull(Harness.RegistrarType.GetMethod(method, BindingFlags.NonPublic | BindingFlags.Static));
	}
}
