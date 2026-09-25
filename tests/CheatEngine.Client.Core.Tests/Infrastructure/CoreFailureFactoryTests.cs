using System.Reflection;
using System.Runtime.CompilerServices;

using CheatEngine.Client.Core.Infrastructure;
using CheatEngine.Client.Results;
using CheatEngine.SDK.Engine.Errors;
using CheatEngine.SDK.Engine.Inspection;
using CheatEngine.SDK.Engine.Scanning.Values;
using CheatEngine.SDK.Engine.Targets;
using CheatEngine.SDK.Lua.Calls;

namespace CheatEngine.Client.Core.Tests.Infrastructure;

public sealed class CoreFailureFactoryTests
{
	[Fact]
	public void LifecycleCreatesTheStableInvalidStateFailure()
	{
		CheatEngineFailure failure = CoreFailureFactory.InvalidState("Client.DrainResources", "The client is stopping.");

		Assert.Equal(CheatEngineFailureKind.InvalidState, failure.Kind);
		Assert.Equal("Client.DrainResources", failure.Operation);
		Assert.Equal("The client is stopping.", failure.Message);
		Assert.Null(failure.Exception);
	}

	[Fact]
	public void FromExceptionPreservesDedicatedActivationExpiryClassification()
	{
		Exception exception = new CheatEngineFailure(CheatEngineFailureKind.ActivationExpired, "Memory.Read",
			"The epoch changed.").ToException(TestContext.Current.CancellationToken);

		CheatEngineFailure failure = CoreFailureFactory.FromException("Dispatcher.Invoke", exception);

		Assert.Equal(CheatEngineFailureKind.ActivationExpired, failure.Kind);
		Assert.Equal("Dispatcher.Invoke", failure.Operation);
		Assert.Equal(exception.Message, failure.Message);
		Assert.Same(exception, failure.Exception);
	}

	[Theory]
	[InlineData("lifecycle", CheatEngineFailureKind.InvalidState)]
	[InlineData("capability", CheatEngineFailureKind.CapabilityUnavailable)]
	[InlineData("global", CheatEngineFailureKind.CapabilityUnavailable)]
	[InlineData("operation", CheatEngineFailureKind.OperationRejected)]
	[InlineData("lua", CheatEngineFailureKind.LuaError)]
	[InlineData("binding", CheatEngineFailureKind.BindingError)]
	[InlineData("marshalling", CheatEngineFailureKind.InvalidHostResult)]
	[InlineData("disposed", CheatEngineFailureKind.InvalidState)]
	[InlineData("argument", CheatEngineFailureKind.OperationRejected)]
	[InlineData("invalid-operation", CheatEngineFailureKind.OperationRejected)]
	[InlineData("unknown", CheatEngineFailureKind.Unknown)]
	public void FromExceptionMapsEachStableClientFailureCategory(string scenario, CheatEngineFailureKind expectedKind)
	{
		Exception exception = scenario switch
		{
			"lifecycle" => new CheatEngineFailure(CheatEngineFailureKind.InvalidState, "Client.Test",
				"Lifecycle stopped.").ToException(TestContext.Current.CancellationToken),
			"capability" => new EngineCapabilityUnavailableException("Client.Test"),
			"global" => new EngineGlobalUnavailableException("Client.Test"),
			"operation" => new EngineOperationFailedException("Client.Test", "Host rejected the operation."),
			"lua" => new EngineLuaException("Client.Test", LuaStatus.RuntimeError),
			"binding" => new EngineBindingException("Client.Test"),
			"marshalling" => new EngineMarshallingException("Client.Test", EngineMarshallingDirection.Result,
				"a stable result", "an invalid result"),
			"disposed" => new ObjectDisposedException("Client.Test"),
			"argument" => new ArgumentException("Invalid request.", nameof(scenario)),
			"invalid-operation" => new InvalidOperationException("Invalid state."),
			_ => new NotSupportedException("Unexpected host failure.")
		};

		CheatEngineFailure failure = CoreFailureFactory.FromException("Client.MapFailure", exception);

		Assert.Equal(expectedKind, failure.Kind);
		Assert.Equal("Client.MapFailure", failure.Operation);
		Assert.Same(exception, failure.Exception);
	}

	[Theory]
	[InlineData("target-changed", CheatEngineFailureKind.TargetChanged, CheatEngineHostEffect.Unknown)]
	[InlineData("target-process-reused", CheatEngineFailureKind.TargetChanged, CheatEngineHostEffect.Unknown)]
	[InlineData("target-unqualified", CheatEngineFailureKind.TargetIdentityUnavailable, CheatEngineHostEffect.Unknown)]
	[InlineData("target-unspecified", CheatEngineFailureKind.TargetIdentityUnavailable, CheatEngineHostEffect.Unknown)]
	[InlineData("resource-handoff", CheatEngineFailureKind.BindingError, CheatEngineHostEffect.CleanupUnconfirmed)]
	[InlineData("symbol-handoff", CheatEngineFailureKind.BindingError, CheatEngineHostEffect.CleanupUnconfirmed)]
	[InlineData("symbol-list-handoff", CheatEngineFailureKind.BindingError, CheatEngineHostEffect.CleanupUnconfirmed)]
	[InlineData("memory-scan-state", CheatEngineFailureKind.InvalidState, CheatEngineHostEffect.Unknown)]
	public void FromExceptionClassifiesTheSdkTwoExceptionTypes(string scenario, CheatEngineFailureKind expectedKind,
		CheatEngineHostEffect expectedHostEffect)
	{
		Exception exception = CreateSdkTwoException(scenario);

		CheatEngineFailure failure = CoreFailureFactory.FromException("Client.MapFailure", exception);

		Assert.Equal(expectedKind, failure.Kind);
		Assert.Equal(expectedHostEffect, failure.HostEffect);
		Assert.Equal("Client.MapFailure", failure.Operation);
		Assert.Same(exception, failure.Exception);
	}

	[Theory]
	[InlineData("resource-handoff")]
	[InlineData("symbol-handoff")]
	[InlineData("symbol-list-handoff")]
	public void FromExceptionKeepsAKnownHostEffectOfAHandoffException(string scenario)
	{
		CheatEngineFailure failure = CoreFailureFactory.FromException("Client.MapFailure", CreateSdkTwoException(scenario),
			CheatEngineHostEffect.Started);

		Assert.Equal(CheatEngineFailureKind.BindingError, failure.Kind);
		Assert.Equal(CheatEngineHostEffect.Started, failure.HostEffect);
	}

	[Fact]
	public void MemoryScanExceptionsAreClassifiedBeforeTheInvalidOperationArm()
	{
		Exception state = CreateSdkTwoException("memory-scan-state");
		MemoryScanException scan = CreateMemoryScanException(MemoryScanFailureKind.RuntimeInvalidated);

		Assert.IsType<InvalidOperationException>(state, exactMatch: false);
		Assert.IsType<InvalidOperationException>(scan, exactMatch: false);
		Assert.Equal(CheatEngineFailureKind.InvalidState, CoreFailureFactory.GetKind(state));
		Assert.Equal(CheatEngineFailureKind.RuntimeChanged, CoreFailureFactory.GetKind(scan));
		Assert.Equal(CheatEngineFailureKind.OperationRejected,
			CoreFailureFactory.GetKind(new InvalidOperationException("Invalid state.")));
	}

	[Theory]
	[InlineData(MemoryScanFailureKind.MissingCapability, CheatEngineFailureKind.CapabilityUnavailable)]
	[InlineData(MemoryScanFailureKind.LuaError, CheatEngineFailureKind.LuaError)]
	[InlineData(MemoryScanFailureKind.UnexpectedResult, CheatEngineFailureKind.InvalidHostResult)]
	[InlineData(MemoryScanFailureKind.RuntimeInvalidated, CheatEngineFailureKind.RuntimeChanged)]
	[InlineData(MemoryScanFailureKind.TargetIdentityUnavailable, CheatEngineFailureKind.TargetIdentityUnavailable)]
	[InlineData(MemoryScanFailureKind.TargetIdentityMismatch, CheatEngineFailureKind.TargetChanged)]
	[InlineData((MemoryScanFailureKind) 99, CheatEngineFailureKind.Unknown)]
	public void MemoryScanExceptionIsClassifiedByItsFailureKind(MemoryScanFailureKind scanKind,
		CheatEngineFailureKind expectedKind)
	{
		MemoryScanException exception = CreateMemoryScanException(scanKind);

		Assert.Equal(expectedKind, CoreFailureFactory.FromException("ValueScans.Next", exception).Kind);
		Assert.Equal(expectedKind, CoreFailureFactory.FromMemoryScanFailureKind(scanKind));
	}

	[Theory]
	[InlineData(EngineFailureKind.ExpectedOperationFailure, CheatEngineFailureKind.OperationRejected)]
	[InlineData(EngineFailureKind.GlobalUnavailable, CheatEngineFailureKind.CapabilityUnavailable)]
	[InlineData(EngineFailureKind.CapabilityUnavailable, CheatEngineFailureKind.CapabilityUnavailable)]
	[InlineData(EngineFailureKind.ProtectedLuaFailure, CheatEngineFailureKind.LuaError)]
	[InlineData(EngineFailureKind.BindingFailure, CheatEngineFailureKind.BindingError)]
	[InlineData(EngineFailureKind.MarshallingFailure, CheatEngineFailureKind.InvalidHostResult)]
	[InlineData(EngineFailureKind.TargetIdentityUnavailable, CheatEngineFailureKind.TargetIdentityUnavailable)]
	[InlineData(EngineFailureKind.TargetIdentityMismatch, CheatEngineFailureKind.TargetChanged)]
	[InlineData((EngineFailureKind) 99, CheatEngineFailureKind.Unknown)]
	public void EngineFailureKindMapsToItsClientKind(EngineFailureKind engineKind, CheatEngineFailureKind expectedKind)
	{
		Assert.Equal(expectedKind, CoreFailureFactory.FromEngineFailureKind(engineKind));
	}

	[Fact]
	public void CancelledIsTheBeforeNativeCallCancellation()
	{
		Assert.Equal(CancellationMapping.BeforeNativeCall("Dispatcher.Invoke"),
			CoreFailureFactory.Cancelled("Dispatcher.Invoke"));
	}

	private static Exception CreateSdkTwoException(string scenario)
	{
		return scenario switch
		{
			"target-changed" => new EngineTargetIdentityException("Client.Test",
				CreateCheck(TargetIdentityCheckKind.TargetChanged)),
			"target-process-reused" => new EngineTargetIdentityException("Client.Test",
				CreateCheck(TargetIdentityCheckKind.ProcessReused)),
			"target-unqualified" => new EngineTargetIdentityException("Client.Test",
				CreateCheck(TargetIdentityCheckKind.CurrentTargetUnqualified)),
			"target-unspecified" => new EngineTargetIdentityException("Client.Test", default),
			"resource-handoff" => new EngineResourceHandoffException("Client.Test", default, null),
			"symbol-handoff" => new SymbolRegistrationHandoffException(default, null),
			"symbol-list-handoff" => new SymbolListRegistrationHandoffException(default, null),
			// The SDK constructs MemoryScanStateException internally only; the classification reads its type alone.
			"memory-scan-state" => (Exception) RuntimeHelpers.GetUninitializedObject(typeof(MemoryScanStateException)),
			_ => throw new ArgumentOutOfRangeException(nameof(scenario), scenario, null)
		};
	}

	/// <summary>Builds the SDK's target validation result, whose constructor is internal to CheatEngine.SDK.</summary>
	private static TargetIdentityCheck CreateCheck(TargetIdentityCheckKind kind)
	{
		ConstructorInfo constructor = typeof(TargetIdentityCheck).GetConstructor(
				BindingFlags.Instance | BindingFlags.NonPublic,
				[typeof(TargetIdentityCheckKind), typeof(TargetSelectionObservation)])
			?? throw new InvalidOperationException("TargetIdentityCheck has no (kind, observed) constructor.");
		return (TargetIdentityCheck) constructor.Invoke([kind, default(TargetSelectionObservation)]);
	}

	/// <summary>Builds a memory-scan failure; CheatEngine.SDK constructs it internally only.</summary>
	private static MemoryScanException CreateMemoryScanException(MemoryScanFailureKind kind)
	{
		MemoryScanException exception =
			(MemoryScanException) RuntimeHelpers.GetUninitializedObject(typeof(MemoryScanException));
		FieldInfo field = typeof(MemoryScanException).GetField("<FailureKind>k__BackingField",
				BindingFlags.Instance | BindingFlags.NonPublic)
			?? throw new InvalidOperationException("MemoryScanException.FailureKind is no longer an auto-property.");
		field.SetValue(exception, kind);
		return exception;
	}
}
