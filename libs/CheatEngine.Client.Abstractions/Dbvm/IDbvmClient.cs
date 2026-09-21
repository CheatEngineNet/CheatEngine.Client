using System.Diagnostics.CodeAnalysis;

using CheatEngine.Client.Events;
using CheatEngine.Client.Results;

namespace CheatEngine.Client.Dbvm;

/// <summary>Observes DBVM state and exposes explicit initialization and watch operations.</summary>
public interface IDbvmClient
{
	/// <summary>Tries to observe DBVM state without initializing DBVM.</summary>
	public bool TryGetStatus(out DbvmStatusSnapshot status, out CheatEngineFailure failure,
		CancellationToken cancellationToken = default);

	/// <summary>Observes DBVM state without initializing DBVM.</summary>
	public DbvmStatusSnapshot GetStatus(CancellationToken cancellationToken = default);

	/// <summary>Tries to initialize DBVM only after an explicit caller request.</summary>
	public bool TryInitialize(DbvmInitializationRequest request, out DbvmStatusSnapshot status,
		out CheatEngineFailure failure, CancellationToken cancellationToken = default);

	/// <summary>Initializes DBVM only after an explicit caller request.</summary>
	public DbvmStatusSnapshot Initialize(DbvmInitializationRequest request,
		CancellationToken cancellationToken = default);

	/// <summary>Tries to register a DBVM watch and its bounded copied event stream.</summary>
	public bool TryRegisterWatch(
		DbvmWatchRequest request,
		DbvmWatchHandler handler,
		EventStreamOptions streamOptions,
		[NotNullWhen(true)] out IDbvmWatchLease? lease,
		out CheatEngineFailure failure,
		CancellationToken cancellationToken = default);

	/// <summary>Registers a DBVM watch or throws when the capability is unavailable or registration fails.</summary>
	public IDbvmWatchLease RegisterWatch(
		DbvmWatchRequest request,
		DbvmWatchHandler handler,
		EventStreamOptions streamOptions,
		CancellationToken cancellationToken = default);
}
