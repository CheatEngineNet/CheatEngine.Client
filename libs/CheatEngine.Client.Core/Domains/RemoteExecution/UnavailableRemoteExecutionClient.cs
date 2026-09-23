using CheatEngine.Client.Core.Domains.Events;
using CheatEngine.Client.Core.Infrastructure;
using CheatEngine.Client.RemoteExecution;
using CheatEngine.Client.Results;
using CheatEngine.Client.Runtime;

namespace CheatEngine.Client.Core.Domains.RemoteExecution;

/// <summary>Preserves remote-execution intent contracts until allocation and thread-affinity live gates are complete.</summary>
internal sealed class UnavailableRemoteExecutionClient : IRemoteExecutionClient
{
	private readonly CoreLifetime? _lifetime;

	internal UnavailableRemoteExecutionClient(CoreLifetime? lifetime = null)
	{
		_lifetime = lifetime;
	}

	public bool TryInjectLibrary(RemoteDllInjectionRequest request, out CheatEngineFailure failure,
		CancellationToken cancellationToken = default)
	{
		failure = CreateFailure("RemoteExecution.InjectLibrary", cancellationToken);
		return false;
	}

	public void InjectLibrary(RemoteDllInjectionRequest request, CancellationToken cancellationToken = default)
	{
		_ = TryInjectLibrary(request, out CheatEngineFailure failure, cancellationToken);
		UnavailableCapabilityFailure.Throw(failure);
	}

	public bool TryInvoke(RemoteCallRequest request, out RemoteCallResult result, out CheatEngineFailure failure,
		CancellationToken cancellationToken = default)
	{
		result = default;
		failure = CreateFailure("RemoteExecution.Invoke", cancellationToken);
		return false;
	}

	public RemoteCallResult Invoke(RemoteCallRequest request, CancellationToken cancellationToken = default)
	{
		_ = TryInvoke(request, out _, out CheatEngineFailure failure, cancellationToken);
		return UnavailableCapabilityFailure.Throw<RemoteCallResult>(failure);
	}

	private CheatEngineFailure CreateFailure(string operation, CancellationToken cancellationToken)
	{
		return UnavailableCapabilityFailure.Create(_lifetime, ClientCapabilityId.RemoteExecution,
			"Remote execution and injection", operation,
			cancellationToken);
	}
}
