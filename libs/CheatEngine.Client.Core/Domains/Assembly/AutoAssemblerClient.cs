#pragma warning disable CECLIENT5004 // Core implements the experimental Auto Assembler surface it serves.

using System.Diagnostics.CodeAnalysis;

using CheatEngine.Client.Assembly;
using CheatEngine.Client.Core.Dispatching;
using CheatEngine.Client.Core.Infrastructure;
using CheatEngine.Client.Dispatching;
using CheatEngine.Client.Results;
using CheatEngine.Client.Runtime;
using CheatEngine.SDK.Engine.Assembly;
using CheatEngine.SDK.Engine.Targets;

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
///         Inside the callback the Client captures the target-selection epoch, asks the port to apply the script, and
///         registers the lease with the activation and that target selection before the callback returns. When the
///         registration is refused (the target selection or the activation changed meanwhile), the patch is released at
///         once through its owner and the failure reports what that release did. Outcomes are mapped by
///         <see cref="AutoAssemblerMapping" />; SDK faults are translated by <see cref="SdkBoundary" />.
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

	internal AutoAssemblerClient(SdkMainThreadDispatcher dispatcher, CoreClientPolicy policy, CoreLifetime lifetime)
		: this(dispatcher, policy, lifetime, SdkAutoAssemblerPort.Instance)
	{
	}

	/// <summary>Creates the client over an explicit port; tests supply a fake port and dispatcher.</summary>
	internal AutoAssemblerClient(ICheatEngineDispatcher dispatcher, CoreClientPolicy policy, CoreLifetime lifetime,
		IAutoAssemblerPort port)
	{
		_dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
		_policy = policy ?? throw new ArgumentNullException(nameof(policy));
		_lifetime = lifetime ?? throw new ArgumentNullException(nameof(lifetime));
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

	/// <summary>The effect of an applied patch that the Client released again before returning it.</summary>
	private static CheatEngineHostEffect CompensationEffect(TargetReleaseStatus status)
	{
		return status == TargetReleaseStatus.Released
			? CheatEngineHostEffect.Completed
			: CheatEngineHostEffect.CleanupUnconfirmed;
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
		long selectionEpoch = _lifetime.TargetSelection.Epoch;
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

		AutoAssemblerPatchLease created = new(patch, name, selectionEpoch,
			facts.Kind == AutoAssemblerApplyOutcomeKind.AppliedTargetChanged, facts.HostWarnings,
			facts.HostWarningsTruncated, _dispatcher, _lifetime.Diagnostics);
		try
		{
			created.Register(_lifetime, selectionEpoch);
		}
		catch (Exception exception) when (exception is CheatEngineClientException or ObjectDisposedException)
		{
			// The lease was never published: its owner makes the one disable attempt here, on the main thread.
			TargetReleaseStatus status = patch.Release();
			return new ApplyAttempt(null, CreateRegistrationFailure(exception, selectionEpoch, status));
		}

		return new ApplyAttempt(created, default);
	}

	private CheatEngineFailure CreateRegistrationFailure(Exception exception, long selectionEpoch,
		TargetReleaseStatus status)
	{
		CheatEngineFailureKind kind = _lifetime.TargetSelection.Epoch != selectionEpoch
			? CheatEngineFailureKind.TargetChanged
			: _lifetime.IsActivationCurrent
				? CheatEngineFailureKind.InvalidState
				: CheatEngineFailureKind.ActivationExpired;
		return new CheatEngineFailure(kind, ApplyOperation,
			"Cheat Engine applied the script, but the Client could not register its lease because the target " +
			$"selection or the activation changed; the patch was released at once and the release ended as {status}.",
			exception, CompensationEffect(status));
	}

	/// <summary>The lease published by one dispatched activation, or the failure that replaced it.</summary>
	private readonly record struct ApplyAttempt(AutoAssemblerPatchLease? Lease, CheatEngineFailure Failure);
}
