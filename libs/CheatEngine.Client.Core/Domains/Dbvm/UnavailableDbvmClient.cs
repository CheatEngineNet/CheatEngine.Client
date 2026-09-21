using System.Diagnostics.CodeAnalysis;

using CheatEngine.Client.Core.Domains.Events;
using CheatEngine.Client.Dbvm;
using CheatEngine.Client.Events;
using CheatEngine.Client.Results;

namespace CheatEngine.Client.Core.Domains.Dbvm;

/// <summary>Observes no inferred DBVM state and never initializes DBVM before its explicit live-host gate passes.</summary>
internal sealed class UnavailableDbvmClient : IDbvmClient
{
	public bool TryGetStatus(out DbvmStatusSnapshot status, out CheatEngineFailure failure,
		CancellationToken cancellationToken = default)
	{
		status = default;
		failure = CreateFailure("Dbvm.GetStatus", cancellationToken);
		return false;
	}

	public DbvmStatusSnapshot GetStatus(CancellationToken cancellationToken = default)
	{
		_ = TryGetStatus(out _, out CheatEngineFailure failure, cancellationToken);
		return UnavailableCapabilityFailure.Throw<DbvmStatusSnapshot>(failure);
	}

	public bool TryInitialize(DbvmInitializationRequest request, out DbvmStatusSnapshot status,
		out CheatEngineFailure failure, CancellationToken cancellationToken = default)
	{
		status = default;
		failure = CreateFailure("Dbvm.Initialize", cancellationToken);
		return false;
	}

	public DbvmStatusSnapshot Initialize(DbvmInitializationRequest request,
		CancellationToken cancellationToken = default)
	{
		_ = TryInitialize(request, out _, out CheatEngineFailure failure, cancellationToken);
		return UnavailableCapabilityFailure.Throw<DbvmStatusSnapshot>(failure);
	}

	public bool TryRegisterWatch(DbvmWatchRequest request, DbvmWatchHandler handler, EventStreamOptions streamOptions,
		[NotNullWhen(true)] out IDbvmWatchLease? lease, out CheatEngineFailure failure,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(handler);
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(streamOptions.Capacity);
		lease = null;
		failure = CreateFailure("Dbvm.RegisterWatch", cancellationToken);
		return false;
	}

	public IDbvmWatchLease RegisterWatch(DbvmWatchRequest request, DbvmWatchHandler handler,
		EventStreamOptions streamOptions, CancellationToken cancellationToken = default)
	{
		_ = TryRegisterWatch(request, handler, streamOptions, out _, out CheatEngineFailure failure, cancellationToken);
		return UnavailableCapabilityFailure.Throw<IDbvmWatchLease>(failure);
	}

	private static CheatEngineFailure CreateFailure(string operation, CancellationToken cancellationToken)
	{
		return UnavailableCapabilityFailure.Create("DBVM observation, explicit initialization, and watches", operation,
			cancellationToken);
	}
}
