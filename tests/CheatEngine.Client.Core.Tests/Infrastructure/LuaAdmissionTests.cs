using CheatEngine.Client.Core.Infrastructure;
using CheatEngine.Client.Results;
using CheatEngine.SDK.Lua.Runtime;

namespace CheatEngine.Client.Core.Tests.Infrastructure;

/// <summary>
///     Proves the Lua admission classification: only an admitted operation succeeds, and every refusal is reported from
///     the SDK's admission status with <see cref="CheatEngineHostEffect.NotStarted" />, never as a rejection.
/// </summary>
/// <remarks>
///     <see cref="LuaRuntime" /> is a static SDK class that cannot be faked; the classification seam
///     (<see cref="LuaAdmission.TryClassify" />) is exercised for every status, and the real SDK call is exercised in the
///     one state a unit test can reach: no Lua runtime attached.
/// </remarks>
public sealed class LuaAdmissionTests
{
	[Theory]
	[InlineData(LuaAdmissionStatus.Detached, CheatEngineFailureKind.ActivationExpired)]
	[InlineData(LuaAdmissionStatus.TransitionInProgress, CheatEngineFailureKind.ActivationExpired)]
	[InlineData(LuaAdmissionStatus.ExternalStateReset, CheatEngineFailureKind.RuntimeChanged)]
	[InlineData(LuaAdmissionStatus.ThreadNotAdmitted, CheatEngineFailureKind.InvalidState)]
	[InlineData(LuaAdmissionStatus.NoStateForThread, CheatEngineFailureKind.InvalidState)]
	[InlineData(LuaAdmissionStatus.Unknown, CheatEngineFailureKind.IndeterminateHostResult)]
	[InlineData((LuaAdmissionStatus) 99, CheatEngineFailureKind.IndeterminateHostResult)]
	public void EachRefusalIsClassifiedAsNotStarted(LuaAdmissionStatus status, CheatEngineFailureKind expectedKind)
	{
		bool admitted = LuaAdmission.TryClassify(status, "UnsafeLua.Execute", out CheatEngineFailure failure);

		Assert.False(admitted);
		Assert.Equal(expectedKind, failure.Kind);
		Assert.Equal(CheatEngineHostEffect.NotStarted, failure.HostEffect);
		Assert.Equal("UnsafeLua.Execute", failure.Operation);
		Assert.Null(failure.Exception);
		Assert.NotEqual(CheatEngineFailureKind.OperationRejected, failure.Kind);
	}

	[Theory]
	[InlineData(LuaAdmissionStatus.ThreadNotAdmitted)]
	[InlineData(LuaAdmissionStatus.NoStateForThread)]
	public void AnOffMainThreadRefusalIsReportedAsAClientBug(LuaAdmissionStatus status)
	{
		Assert.False(LuaAdmission.TryClassify(status, "UnsafeLua.Execute", out CheatEngineFailure failure));

		Assert.StartsWith("Client bug: called off the main thread.", failure.Message, StringComparison.Ordinal);
	}

	[Fact]
	public void OnlyAdmittedIsASuccess()
	{
		Assert.True(LuaAdmission.TryClassify(LuaAdmissionStatus.Admitted, "UnsafeLua.Execute",
			out CheatEngineFailure failure));
		Assert.Equal(default, failure);
	}

	[Fact]
	public void TryAcquireReportsADetachedRuntimeAsAnExpiredActivation()
	{
		// No Lua runtime is attached in unit tests: the real SDK admission reports Detached.
		bool admitted = LuaAdmission.TryAcquire("UnsafeLua.Execute", out LuaRuntimeOperation operation,
			out CheatEngineFailure failure);
		operation.Dispose();

		Assert.False(admitted);
		Assert.Equal(CheatEngineFailureKind.ActivationExpired, failure.Kind);
		Assert.Equal(CheatEngineHostEffect.NotStarted, failure.HostEffect);
		Assert.Equal("UnsafeLua.Execute", failure.Operation);
	}

	[Theory]
	[InlineData("")]
	[InlineData(" ")]
	public void TryAcquireRequiresAnOperationName(string operation)
	{
		Assert.Throws<ArgumentException>(() =>
		{
			_ = LuaAdmission.TryAcquire(operation, out LuaRuntimeOperation admitted, out _);
			admitted.Dispose();
		});
	}
}
