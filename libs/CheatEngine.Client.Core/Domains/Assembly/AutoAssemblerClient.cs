using System.Diagnostics.CodeAnalysis;

using CheatEngine.Client.Assembly;
using CheatEngine.Client.Core.Dispatching;
using CheatEngine.Client.Core.Infrastructure;
using CheatEngine.Client.Dispatching;
using CheatEngine.Client.Results;
using CheatEngine.Client.Runtime;
using CheatEngine.SDK.Engine.Assembly;

namespace CheatEngine.Client.Core.Domains.Assembly;

/// <summary>The opt-in Auto Assembler client over CheatEngine.SDK's <c>AutoAssemblerPatcher</c>.</summary>
/// <remarks>
///     <para>
///         Every call follows the Client order: request validation, activation admission, cancellation observed before
///         dispatch, the <c>EnableAutoAssemblerPatches</c> policy gate (audit Q44: a refusal makes no Cheat Engine call),
///         then one dispatched callback on Cheat Engine's main thread. A cancellation is never observed after the
///         dispatch: an applied patch is always returned as a lease, because a token cannot undo it.
///     </para>
///     <para>
///         Inside the callback the Client refuses an activation that stopped or ended since its admission, before Cheat
///         Engine applies a patch that no lease could own, and asks the port to apply the script. It then registers
///         the lease with the activation and with the target selection of the process incarnation that CheatEngine.SDK
///         bound the patch to (<see cref="ITargetSelectionBinder" />), before the callback returns: a process selected
///         in Cheat Engine's own window since the last observation advances the epoch first, so the next observation
///         never releases a patch whose own process is still selected. When the registration is refused, the patch is
///         released at once through its owner and <see cref="LeaseRegistration" /> reports what that release left.
///         Outcomes are mapped by <see cref="AutoAssemblerMapping" />; SDK faults are translated by
///         <see cref="SdkBoundary" />.
///     </para>
///     <para>
///         The SDK options are bounded here: host text (rejection detail, warnings, check messages) is copied up to
///         <see cref="HostTextByteLimit" /> UTF-8 bytes, and the SDK's disable-info snapshot, which the Client does not
///         project, is copied at the SDK minimum.
///     </para>
/// </remarks>
internal sealed class AutoAssemblerClient : IAutoAssemblerClient
{
	/// <summary>The operation name of a syntax check.</summary>
	internal const string CheckOperation = "AutoAssembler.Check";

	/// <summary>The operation name of an activation.</summary>
	internal const string ApplyOperation = "AutoAssembler.ApplyPatch";

	/// <summary>The largest number of UTF-8 bytes copied from one Cheat Engine host text.</summary>
	internal const int HostTextByteLimit = 4096;

	private const string PolicyRefusalMessage =
		"Auto Assembler patches were not enabled for this activation; call EnableAutoAssemblerPatches() on the " +
		"Client builder.";

	private readonly ICheatEngineDispatcher _dispatcher;
	private readonly CoreLifetime _lifetime;
	private readonly CoreClientPolicy _policy;
	private readonly IAutoAssemblerPort _port;
	private readonly ITargetSelectionBinder _selection;

	/// <summary>Creates the client of an activation over CheatEngine.SDK's patcher.</summary>
	/// <param name="dispatcher">The activation dispatcher.</param>
	/// <param name="policy">The activation policy, whose opt-in gates every call.</param>
	/// <param name="lifetime">The activation lifetime.</param>
	/// <param name="selection">The owner of the observed target selection, the activation's process client.</param>
	internal AutoAssemblerClient(SdkMainThreadDispatcher dispatcher, CoreClientPolicy policy, CoreLifetime lifetime,
		ITargetSelectionBinder selection)
		: this(dispatcher, policy, lifetime, selection, SdkAutoAssemblerPort.Instance)
	{
	}

	/// <summary>Creates the client over an explicit port; tests supply a fake port and dispatcher.</summary>
	internal AutoAssemblerClient(ICheatEngineDispatcher dispatcher, CoreClientPolicy policy, CoreLifetime lifetime,
		ITargetSelectionBinder selection, IAutoAssemblerPort port)
	{
		_dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
		_policy = policy ?? throw new ArgumentNullException(nameof(policy));
		_lifetime = lifetime ?? throw new ArgumentNullException(nameof(lifetime));
		_selection = selection ?? throw new ArgumentNullException(nameof(selection));
		_port = port ?? throw new ArgumentNullException(nameof(port));
	}

	/// <summary>Gets the bounded SDK options every call uses.</summary>
	internal static AutoAssemblerOptions Options
	{
		get;
	} = new()
	{
		CaptureHostText = true,
		MaxHostTextBytes = HostTextByteLimit,
		MaxDisableInfoEntries = AutoAssemblerOptions.MinDisableInfoEntries,
		MaxDisableInfoNameBytes = AutoAssemblerOptions.MinDisableInfoNameBytes
	};

	public bool TryCheck(AutoAssemblerScript script, out AutoAssemblerCheckResult result,
		out CheatEngineFailure failure, CancellationToken cancellationToken = default)
	{
		string source = RequireSource(script);
		result = default;
		if (!TryAdmit(CheckOperation, out failure, cancellationToken))
		{
			return false;
		}

		AutoAssemblerCheckFacts facts = default;
		CheatEngineFailure admissionFailure = default;
		bool admitted = false;
		if (!SdkBoundary.TryInvoke(_dispatcher, CheckOperation,
				() => admitted = _port.TryCheck(CheckOperation, source, Options, out facts, out admissionFailure),
				CheatEngineHostEffect.Unknown, _lifetime, out failure, cancellationToken))
		{
			return false;
		}

		if (!admitted)
		{
			failure = admissionFailure;
			return false;
		}

		return AutoAssemblerMapping.TryMapCheck(CheckOperation, facts, out result, out failure);
	}

	public AutoAssemblerCheckResult Check(AutoAssemblerScript script, CancellationToken cancellationToken = default)
	{
		if (TryCheck(script, out AutoAssemblerCheckResult result, out CheatEngineFailure failure, cancellationToken))
		{
			return result;
		}

		failure.Throw(cancellationToken);
		return default;
	}

	public bool TryApplyPatch(AutoAssemblerScript script, [NotNullWhen(true)] out IAutoAssemblerPatchLease? lease,
		out CheatEngineFailure failure, CancellationToken cancellationToken = default)
	{
		string source = RequireSource(script);
		string? name = script.Name;
		lease = null;
		if (!TryAdmit(ApplyOperation, out failure, cancellationToken))
		{
			return false;
		}

		ApplyAttempt attempt = default;
		if (!SdkBoundary.TryInvoke(_dispatcher, ApplyOperation, () => attempt = ApplyOnMainThread(source, name),
				CheatEngineHostEffect.Unknown, _lifetime, out failure, cancellationToken))
		{
			return false;
		}

		_selection.ReportBinding(attempt.Binding, ApplyOperation);
		if (attempt.Lease is not { } published)
		{
			failure = attempt.Failure;
			return false;
		}

		if (published.AppliedAfterTargetChange)
		{
			// After the dispatched work returned, never inside it; the operation and the epoch only.
			_lifetime.Diagnostics.AutoAssemblerPatchAppliedAfterTargetChange(ApplyOperation, published.SelectionEpoch);
		}

		lease = published;
		failure = default;
		return true;
	}

	public IAutoAssemblerPatchLease ApplyPatch(AutoAssemblerScript script,
		CancellationToken cancellationToken = default)
	{
		if (TryApplyPatch(script, out IAutoAssemblerPatchLease? lease, out CheatEngineFailure failure,
				cancellationToken))
		{
			return lease;
		}

		failure.Throw(cancellationToken);
		throw new InvalidOperationException("Unreachable failure flow.");
	}

	private static string RequireSource(AutoAssemblerScript script)
	{
		return script.Source ?? throw new ArgumentException("The default Auto Assembler script has no source.",
			nameof(script));
	}

	/// <summary>Admits the activation, observes cancellation, then applies the policy gate; nothing is dispatched.</summary>
	private bool TryAdmit(string operation, out CheatEngineFailure failure, CancellationToken cancellationToken)
	{
		_lifetime.ThrowIfInactive(operation);
		if (cancellationToken.IsCancellationRequested)
		{
			failure = CancellationMapping.BeforeNativeCall(operation);
			return false;
		}

		if (!_policy.EnableAutoAssemblerPatches)
		{
			_lifetime.Diagnostics.CapabilityRefused(ClientCapabilityId.AutoAssemblerPatches.Value, operation,
				ClientCapabilityEvidenceReasonCode.Policy, ClientCapabilityEvidenceState.Missing);
			failure = new CheatEngineFailure(CheatEngineFailureKind.CapabilityUnavailable, operation,
				PolicyRefusalMessage, null, CheatEngineHostEffect.NotStarted);
			return false;
		}

		failure = default;
		return true;
	}

	/// <summary>Applies the script and publishes its lease, on Cheat Engine's main thread.</summary>
	private ApplyAttempt ApplyOnMainThread(string source, string? name)
	{
		// No lease can be registered once the activation stops or ends: refuse before Cheat Engine applies a patch that
		// no lease could own.
		_lifetime.ThrowIfInactive(ApplyOperation);
		if (!_port.TryApply(ApplyOperation, source, Options, out AutoAssemblerApplyFacts facts,
				out IAutoAssemblerPatchOwner? patch, out CheatEngineFailure admissionFailure))
		{
			return new ApplyAttempt(null, admissionFailure);
		}

		if (AutoAssemblerMapping.ToApplyFailure(ApplyOperation, facts) is { } failure)
		{
			// The SDK publishes an owner only for an applied script; an owner next to a failure is released at once.
			_ = patch?.Release();
			return new ApplyAttempt(null, failure);
		}

		if (patch is null)
		{
			return new ApplyAttempt(null, new CheatEngineFailure(CheatEngineFailureKind.InvalidHostResult,
				ApplyOperation,
				"CheatEngine.SDK reported an applied Auto Assembler script without publishing its owner; the patch may " +
				"remain in the target.", null, CheatEngineHostEffect.CleanupUnconfirmed));
		}

		TargetSelectionBinding binding = default;
		try
		{
			// The lease belongs to the selection of the process CheatEngine.SDK bound the patch to, not to the last
			// selection the Client observed.
			binding = _selection.BindOwner(patch.TargetIncarnation, ApplyOperation);
			AutoAssemblerPatchLease created = new(patch, name, binding.SelectionEpoch,
				facts.Kind == AutoAssemblerApplyOutcomeKind.AppliedTargetChanged, facts.HostWarnings,
				facts.HostWarningsTruncated, _dispatcher, _lifetime.Diagnostics);
			created.Register(_lifetime, binding.SelectionEpoch);
			return new ApplyAttempt(created, default)
			{
				Binding = binding
			};
		}
		catch (Exception registration)
		{
			// The lease was never published: its owner makes the one disable attempt here, on the main thread.
			LeaseReleaseOutcome released = AutoAssemblerMapping.ToReleaseOutcome(patch.Release());
			if (registration is not (CheatEngineClientException or ObjectDisposedException))
			{
				throw;
			}

			return new ApplyAttempt(null, LeaseRegistration.Refused(_lifetime, ApplyOperation, registration, released,
				"the applied Auto Assembler patch"))
			{
				Binding = binding
			};
		}
	}

	/// <summary>The lease published by one dispatched activation, or the failure that replaced it.</summary>
	private readonly record struct ApplyAttempt(AutoAssemblerPatchLease? Lease, CheatEngineFailure Failure)
	{
		/// <summary>Gets the selection binding of an applied patch, reported after the callback returned.</summary>
		internal TargetSelectionBinding Binding
		{
			get;
			init;
		}
	}
}
