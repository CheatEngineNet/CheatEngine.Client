using CheatEngine.Client.Core.Infrastructure;
using CheatEngine.Client.Results;
using CheatEngine.SDK.Engine.Errors;
using CheatEngine.SDK.Lua.Calls;

namespace CheatEngine.Client.Core.Tests.Infrastructure;

public sealed class CoreFailureFactoryTests
{
	[Fact]
	public void LifecycleCreatesTheStableInvalidStateFailure()
	{
		CheatEngineFailure failure = CoreFailureFactory.Lifecycle("Client.DrainResources", "The client is stopping.");

		Assert.Equal(CheatEngineFailureKind.InvalidState, failure.Kind);
		Assert.Equal("Client.DrainResources", failure.Operation);
		Assert.Equal("The client is stopping.", failure.Message);
		Assert.Null(failure.Exception);
	}

	[Fact]
	public void FromExceptionPreservesDedicatedActivationExpiryClassification()
	{
		CheatEngineActivationExpiredException exception = new("Memory.Read", "The epoch changed.");

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
			"lifecycle" => new CheatEngineClientLifecycleException("Client.Test", "Lifecycle stopped."),
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
}
