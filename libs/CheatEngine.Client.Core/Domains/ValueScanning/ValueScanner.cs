using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

using CheatEngine.Client.Core.Dispatching;
using CheatEngine.Client.Core.Infrastructure;
using CheatEngine.Client.Results;
using CheatEngine.Client.Scanning;
using CheatEngine.SDK.Engine.Scanning.Values;

namespace CheatEngine.Client.Core.Domains.ValueScanning;

/// <summary>Creates value-scan sessions through CheatEngine.SDK's scan-session factory, on Cheat Engine's main thread.</summary>
/// <remarks>
///     <para>
///         A created session is registered with the activation and with the target selection it was created for, in the
///         same main-thread callback that created it, so no path leaves its Cheat Engine objects without an owner: a
///         registration that fails, and a cancellation observed after the creation, release them at once. An ended or
///         stopping activation is refused in that callback before Cheat Engine creates anything, since no lease could own
///         it; a registration refused after the creation reports the release (<see cref="LeaseRegistration" />).
///     </para>
///     <para>
///         The target selection is the one of the process incarnation that CheatEngine.SDK bound the session to
///         (<see cref="ITargetSelectionBinder" />), not the last selection the Client observed: a process selected in Cheat
///         Engine's own window since then advances the epoch before the session is registered, so the next observation
///         never releases a session whose own process is still selected.
///     </para>
/// </remarks>
internal sealed class ValueScanner : IValueScanner
{
	/// <summary>The public operation name of a session creation.</summary>
	internal const string CreateOperation = "ValueScans.CreateSession";

	private readonly SdkMainThreadDispatcher _dispatcher;
	private readonly IValueScanPort _port;
	private readonly ITargetSelectionBinder _selection;

	/// <summary>Creates the value scanner of an activation.</summary>
	/// <param name="dispatcher">The activation dispatcher.</param>
	/// <param name="selection">The owner of the observed target selection, the activation's process client.</param>
	/// <param name="port">The session factory; CheatEngine.SDK's when omitted.</param>
	internal ValueScanner(SdkMainThreadDispatcher dispatcher, ITargetSelectionBinder selection,
		IValueScanPort? port = null)
	{
		_dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
		_selection = selection ?? throw new ArgumentNullException(nameof(selection));
		_port = port ?? SdkValueScanPort.Instance;
	}

	public bool TryCreateSession([NotNullWhen(true)] out IValueScanSession? session, out CheatEngineFailure failure,
		CancellationToken cancellationToken = default)
	{
		session = null;
		// Creating a lease-owned resource is admitted only while the activation is active, never from the cleanup scope:
		// an ended or stopping activation throws before a cancellation is reported, never the reverse.
		_dispatcher.Lifetime.ThrowIfInactive(CreateOperation);
		if (cancellationToken.IsCancellationRequested)
		{
			failure = CancellationMapping.BeforeNativeCall(CreateOperation);
			return false;
		}

		if (!_dispatcher.TryInvoke(() => CreateOnMainThread(cancellationToken), out CreateOutcome outcome,
				out failure, cancellationToken))
		{
			return false;
		}

		_selection.ReportBinding(outcome.Binding, CreateOperation);
		session = outcome.Session;
		failure = outcome.Failure;
		return session is not null;
	}

	public IValueScanSession CreateSession(CancellationToken cancellationToken = default)
	{
		if (TryCreateSession(out IValueScanSession? session, out CheatEngineFailure failure, cancellationToken))
		{
			return session;
		}

		failure.Throw(cancellationToken);
		throw new UnreachableException();
	}

	private CreateOutcome CreateOnMainThread(CancellationToken cancellationToken)
	{
		if (cancellationToken.IsCancellationRequested)
		{
			return new CreateOutcome(null, CancellationMapping.BeforeNativeCall(CreateOperation));
		}

		CoreLifetime lifetime = _dispatcher.Lifetime;
		// No lease can be registered once the activation stops or ends (a deactivation cleanup scope included): refuse
		// before Cheat Engine creates objects that no lease could own.
		lifetime.ThrowIfInactive(CreateOperation);
		MemoryScanCreationStatus status;
		IValueScanSessionHandle? handle;
		try
		{
			status = _port.TryCreate(out handle);
		}
		catch (Exception fault) when (SdkBoundary.IsSdkFault(fault))
		{
			// The SDK rolls back what it created before it throws; what the host did is not known.
			return new CreateOutcome(null,
				SdkBoundary.Translate(CreateOperation, fault, CheatEngineHostEffect.Unknown, lifetime));
		}

		if (status != MemoryScanCreationStatus.Success || handle is null)
		{
			// A handle next to a failed creation is released at once; a release that is not complete leaves the
			// failure's cleanup unconfirmed, as on every other failure path.
			CheatEngineFailure failure = ValueScanMapping.FromCreationStatus(status, CreateOperation);
			if (handle is not null && !ValueScanMapping.FromRelease(handle.Release()).IsComplete)
			{
				failure = CoreFailureFactory.WithHostEffect(failure, CheatEngineHostEffect.CleanupUnconfirmed);
			}

			return new CreateOutcome(null, failure);
		}

		if (cancellationToken.IsCancellationRequested)
		{
			// Nothing is published after a late cancellation: the new objects are released at once.
			LeaseReleaseOutcome released = ValueScanMapping.FromRelease(handle.Release());
			return new CreateOutcome(null, released.IsComplete
				? CancellationMapping.AfterNativeCall(CreateOperation)
				: new CheatEngineFailure(CheatEngineFailureKind.Cancelled, CreateOperation,
					"The operation was cancelled after Cheat Engine created the scan session, and its release ended " +
					$"with {released.Kind}: a scanner may remain in Cheat Engine.", null,
					CheatEngineHostEffect.CleanupUnconfirmed));
		}

		TargetSelectionBinding binding = default;
		try
		{
			binding = _selection.BindOwner(handle.TargetIncarnation, CreateOperation);
			ValueScanSession session = new(_dispatcher, handle, binding.SelectionEpoch);
			session.Register(lifetime, binding.SelectionEpoch);
			return new CreateOutcome(session, default)
			{
				Binding = binding
			};
		}
		catch (Exception registration)
		{
			// The activation or the target selection ended while the session was created: release it here, on the
			// main thread, since no registry will.
			LeaseReleaseOutcome released = ValueScanMapping.FromRelease(handle.Release());
			if (registration is not (CheatEngineClientException or ObjectDisposedException))
			{
				throw;
			}

			return new CreateOutcome(null, LeaseRegistration.Refused(lifetime, CreateOperation, registration,
				released, "the new scan session"))
			{
				Binding = binding
			};
		}
	}

	private readonly record struct CreateOutcome(ValueScanSession? Session, CheatEngineFailure Failure)
	{
		/// <summary>Gets the selection binding of a published session, reported after the callback returned.</summary>
		internal TargetSelectionBinding Binding
		{
			get;
			init;
		}
	}
}
