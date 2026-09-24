using CheatEngine.Client.Core.Infrastructure;
using CheatEngine.Client.Dispatching;
using CheatEngine.Client.Inspection;
using CheatEngine.Client.Results;
using CheatEngine.SDK.Engine.Values;

namespace CheatEngine.Client.Core.Domains;

/// <summary>The activation-owned lease of one symbol that CheatEngine.SDK's ownership coordinator registered.</summary>
/// <remarks>
///     <para>
///         <see cref="InspectionClient" /> ran the collision pre-check before the registration (audit A14-25, A14-39);
///         the release delegates to the SDK lease (<c>SymbolRegistrationLease.Release</c>), which unregisters the name
///         only while it still resolves to the leased address and no newer registration of the name through the SDK
///         coordinator superseded it. <see cref="SdkReleaseOutcomes.FromSymbolRegistration" /> maps its kind:
///     </para>
///     <list type="table">
///         <listheader>
///             <term>SDK kind</term>
///             <description>Client outcome</description>
///         </listheader>
///         <item><term><c>Released</c></term><description><c>Released</c>, <c>Completed</c></description></item>
///         <item><term><c>AlreadyReleased</c></term><description><c>AlreadyReleased</c>, <c>NotStarted</c></description></item>
///         <item><term><c>Superseded</c></term><description><c>Superseded</c>, <c>NotStarted</c></description></item>
///         <item><term><c>StaleRuntime</c></term><description><c>RefusedRuntimeChanged</c>, <c>NotStarted</c></description></item>
///         <item><term><c>CleanupUnavailable</c></term><description><c>CleanupUnavailable</c>, <c>NotStarted</c> (retryable)</description></item>
///         <item><term><c>CleanupIndeterminate</c></term><description><c>CleanupUnconfirmed</c>, <c>Started</c></description></item>
///         <item><term><c>Replaced</c></term><description><c>Replaced</c>, <c>NotStarted</c></description></item>
///         <item><term><c>ExternallyRemoved</c></term><description><c>ExternallyRemoved</c>, <c>NotStarted</c></description></item>
///         <item><term><c>Unknown</c> or an undefined kind</term><description><c>Unknown</c>, <c>Unknown</c> (retryable)</description></item>
///     </list>
///     <para>
///         <see cref="HostResourceLease" /> runs the release on Cheat Engine's main thread, keeps a retryable outcome
///         registered with the activation and reports an incomplete one at deactivation (audit Q43). Once an outcome is
///         no longer retryable the lease owns nothing that a later attempt could release, so it gives the
///         activation-local name reservation back, including after an SDK fault; a retryable outcome keeps it.
///     </para>
/// </remarks>
internal sealed class SymbolRegistrationLease : HostResourceLease, ISymbolRegistrationLease
{
	/// <summary>The stable operation name of the release, the only text its logs and reports carry.</summary>
	internal const string ReleaseOperation = "Inspection.ReleaseSymbol";

	/// <summary>What the base lease records when the SDK release faults: a call may have begun.</summary>
	private static readonly LeaseReleaseOutcome FaultedOutcome =
		new(LeaseReleaseKind.CleanupUnconfirmed, CheatEngineHostEffect.Unknown);

	private readonly ISymbolRegistrationHandle _handle;
	private readonly Action<string> _releaseName;

	/// <summary>Creates the lease of one registration.</summary>
	/// <param name="registration">The registered name and address.</param>
	/// <param name="handle">The SDK release handle of the registration.</param>
	/// <param name="dispatcher">The activation dispatcher that runs the release on Cheat Engine's main thread.</param>
	/// <param name="releaseName">Gives the activation-local name reservation back.</param>
	/// <param name="diagnostics">The activation diagnostics; nothing is logged when omitted.</param>
	internal SymbolRegistrationLease(SymbolRegistration registration, ISymbolRegistrationHandle handle,
		ICheatEngineDispatcher dispatcher, Action<string> releaseName, ICoreDiagnostics? diagnostics = null)
		: base(ReleaseOperation, dispatcher, diagnostics)
	{
		_handle = handle ?? throw new ArgumentNullException(nameof(handle));
		_releaseName = releaseName ?? throw new ArgumentNullException(nameof(releaseName));
		Name = registration.Name;
		Address = registration.Address;
	}

	public string Name
	{
		get;
	}

	public Address Address
	{
		get;
	}

	protected override LeaseReleaseOutcome ReleaseOnMainThread()
	{
		LeaseReleaseOutcome outcome = FaultedOutcome;
		try
		{
			outcome = SdkReleaseOutcomes.FromSymbolRegistration(_handle.Release());
			return outcome;
		}
		finally
		{
			if (!outcome.IsRetryable)
			{
				_releaseName(Name);
			}
		}
	}
}
