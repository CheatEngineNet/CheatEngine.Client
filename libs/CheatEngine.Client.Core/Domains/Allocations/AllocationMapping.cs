using System.Globalization;

using CheatEngine.Client.Allocations;
using CheatEngine.Client.Core.Infrastructure;
using CheatEngine.Client.Results;
using CheatEngine.SDK.Engine.Allocation;
using CheatEngine.SDK.Engine.Enums;
using CheatEngine.SDK.Engine.Objects;
using CheatEngine.SDK.Engine.Targets;
using CheatEngine.SDK.Engine.Values;

namespace CheatEngine.Client.Core.Domains.Allocations;

/// <summary>Maps every allocation outcome that CheatEngine.SDK 2.0.0 reports to the Client vocabulary.</summary>
/// <remarks>
///     <para>
///         Each mapping is total over its enum, and a value this Client version does not know fails closed
///         (<see cref="CheatEngineFailureKind.IndeterminateHostResult" />, and an effect that never claims that nothing
///         remains); the mapping-totality tests fail when the consumed SDK adds a value. Exceptions are classified by
///         <see cref="SdkBoundary" />. The effect of an allocation that published no owner comes from
///         <see cref="HostEffectMapping" />, and a release from <see cref="SdkReleaseOutcomes" />.
///     </para>
///     <para>
///         When Cheat Engine allocated but no owner could be published, the SDK makes one compensating release. A
///         compensation that is not confirmed leaves <see cref="CheatEngineHostEffect.CleanupUnconfirmed" />, and the
///         failure message carries the address and the size so the application can recover the memory by other means.
///     </para>
/// </remarks>
internal static class AllocationMapping
{
	/// <summary>Validates a Client request, as its constructor does, and converts it to the SDK request.</summary>
	/// <param name="request">The Client request.</param>
	/// <returns>The SDK request, with an explicit protection.</returns>
	/// <exception cref="ArgumentOutOfRangeException">
	///     The size is not positive (the <see langword="default" /> request), the protection is not a value this Client
	///     version defines, or the preferred address is the null address.
	/// </exception>
	internal static TargetAllocationRequest CreateRequest(AllocationRequest request)
	{
		if (request.Size <= 0)
		{
			throw new ArgumentOutOfRangeException(nameof(request), request.Size,
				"An allocation request requires a positive size; the default request has none.");
		}

		if (!TryGetSdkProtection(request.Protection, out MemoryProtection protection))
		{
			throw new ArgumentOutOfRangeException(nameof(request), request.Protection,
				"The allocation protection is not a value this Client version defines.");
		}

		if (request.PreferredAddress is { IsZero: true })
		{
			throw new ArgumentOutOfRangeException(nameof(request), request.PreferredAddress,
				"A preferred allocation address must be nonzero.");
		}

		return new TargetAllocationRequest(new TargetAllocationSize(request.Size), request.PreferredAddress,
			protection);
	}

	/// <summary>Maps a Client protection to the Cheat Engine <c>PAGE_*</c> value passed to <c>allocateMemory</c>.</summary>
	/// <param name="protection">The Client protection.</param>
	/// <param name="sdkProtection">The page protection, or <see cref="MemoryProtection.None" /> for an unknown value.</param>
	/// <returns><see langword="true" /> for a defined protection.</returns>
	internal static bool TryGetSdkProtection(AllocationProtection protection, out MemoryProtection sdkProtection)
	{
		sdkProtection = protection switch
		{
			AllocationProtection.ReadWrite => MemoryProtection.ReadWrite,
			AllocationProtection.ExecuteReadWrite => MemoryProtection.ExecuteReadWrite,
			_ => MemoryProtection.None
		};
		return sdkProtection != MemoryProtection.None;
	}

	/// <summary>Maps the category of an <c>allocateMemory</c> call that published no allocation.</summary>
	/// <param name="kind">The category reported by CheatEngine.SDK.</param>
	/// <returns>
	///     The failure kind; <see cref="CheatEngineFailureKind.IndeterminateHostResult" /> for a success or an unspecified
	///     category (neither publishes an owner without breaking the SDK contract) and for an unrecognized value.
	/// </returns>
	internal static CheatEngineFailureKind ToFailureKind(TargetMemoryOperationOutcomeKind kind)
	{
		return kind switch
		{
			TargetMemoryOperationOutcomeKind.ExpectedFailure => CheatEngineFailureKind.OperationRejected,
			TargetMemoryOperationOutcomeKind.GlobalUnavailable or TargetMemoryOperationOutcomeKind.CapabilityUnavailable =>
				CheatEngineFailureKind.CapabilityUnavailable,
			TargetMemoryOperationOutcomeKind.ProtectedLuaFailure => CheatEngineFailureKind.LuaError,
			TargetMemoryOperationOutcomeKind.BindingFailure => CheatEngineFailureKind.BindingError,
			TargetMemoryOperationOutcomeKind.MarshallingFailure => CheatEngineFailureKind.InvalidHostResult,
			TargetMemoryOperationOutcomeKind.TargetIdentityUnavailable => CheatEngineFailureKind.TargetIdentityUnavailable,
			TargetMemoryOperationOutcomeKind.TargetIdentityMismatch => CheatEngineFailureKind.TargetChanged,
			TargetMemoryOperationOutcomeKind.Unspecified or TargetMemoryOperationOutcomeKind.Succeeded =>
				CheatEngineFailureKind.IndeterminateHostResult,
			_ => CheatEngineFailureKind.IndeterminateHostResult
		};
	}

	/// <summary>Maps an allocation for which CheatEngine.SDK published no owner to its failure.</summary>
	/// <param name="attempt">The copied SDK outcome.</param>
	/// <param name="size">The requested size, reported with an allocation that may remain.</param>
	/// <param name="operation">The public Client operation name.</param>
	/// <returns>The classified failure.</returns>
	internal static CheatEngineFailure FromUnpublishedAllocation(AllocationAttempt attempt, long size, string operation)
	{
		if (attempt.Compensation is { } compensation)
		{
			return FromCompensation(compensation, attempt.Address, size, operation);
		}

		if (attempt.Effect == EngineEffectState.Applied)
		{
			// Cheat Engine allocated, and the SDK published neither an owner nor a compensation: a contract break that
			// may leave the allocation in the target.
			return Failure(CheatEngineFailureKind.IndeterminateHostResult, operation,
				CheatEngineHostEffect.CleanupUnconfirmed,
				$"Cheat Engine allocated {Describe(attempt.Address, size)} but CheatEngine.SDK published no owner and " +
				"released nothing: the allocation may remain in the target.");
		}

		CheatEngineFailureKind kind = ToFailureKind(attempt.Kind);
		string message = kind switch
		{
			CheatEngineFailureKind.OperationRejected => "Cheat Engine's allocateMemory returned nil: nothing was allocated.",
			CheatEngineFailureKind.CapabilityUnavailable =>
				"Cheat Engine's allocateMemory is unavailable: nothing was allocated.",
			CheatEngineFailureKind.LuaError => "Cheat Engine's allocateMemory raised a Lua error.",
			CheatEngineFailureKind.BindingError => "The CheatEngine.SDK allocation binding could not uphold its contract.",
			CheatEngineFailureKind.InvalidHostResult =>
				"Cheat Engine's allocateMemory returned a value that is neither a target address nor nil.",
			CheatEngineFailureKind.TargetIdentityUnavailable =>
				"The identity of Cheat Engine's selected target could not be established, so nothing was allocated.",
			CheatEngineFailureKind.TargetChanged => "Cheat Engine's selected target changed, so nothing was allocated.",
			_ => "CheatEngine.SDK reported no recognized allocation outcome and published no owner."
		};
		if (!attempt.Address.IsZero)
		{
			message += $" Cheat Engine still returned {Describe(attempt.Address, size)}: an allocation may remain in " +
					   "the target.";
		}

		return Failure(kind, operation, HostEffectMapping.FromSdk(attempt.Effect), message);
	}

	/// <summary>Maps the compensating release of an allocation that Cheat Engine made but no owner took.</summary>
	/// <param name="status">The status of the SDK's one compensating release.</param>
	/// <param name="address">The address Cheat Engine returned.</param>
	/// <param name="size">The requested size.</param>
	/// <param name="operation">The public Client operation name.</param>
	/// <returns>
	///     A failure whose kind is the reason of a refused release (<see cref="CheatEngineFailureKind.TargetNotAttached" />,
	///     <see cref="CheatEngineFailureKind.TargetChanged" />, <see cref="CheatEngineFailureKind.TargetIdentityUnavailable" />,
	///     <see cref="CheatEngineFailureKind.RuntimeChanged" />) or <see cref="CheatEngineFailureKind.IndeterminateHostResult" />;
	///     its effect is <see cref="CheatEngineHostEffect.Completed" /> when the release was confirmed, otherwise
	///     <see cref="CheatEngineHostEffect.CleanupUnconfirmed" /> with the address in the message.
	/// </returns>
	internal static CheatEngineFailure FromCompensation(TargetReleaseStatus status, Address address, long size,
		string operation)
	{
		LeaseReleaseOutcome released = SdkReleaseOutcomes.FromTarget(status);
		CheatEngineFailureKind kind = released.Kind switch
		{
			LeaseReleaseKind.RefusedTargetNotAttached => CheatEngineFailureKind.TargetNotAttached,
			LeaseReleaseKind.RefusedTargetChanged => CheatEngineFailureKind.TargetChanged,
			LeaseReleaseKind.RefusedTargetIdentityUnavailable => CheatEngineFailureKind.TargetIdentityUnavailable,
			LeaseReleaseKind.RefusedRuntimeChanged => CheatEngineFailureKind.RuntimeChanged,
			_ => CheatEngineFailureKind.IndeterminateHostResult
		};
		return released.IsComplete
			? Failure(kind, operation, CheatEngineHostEffect.Completed,
				"Cheat Engine allocated the memory but CheatEngine.SDK could not publish its owner; the allocation was " +
				"released at once.")
			: Failure(kind, operation, CheatEngineHostEffect.CleanupUnconfirmed,
				$"Cheat Engine allocated {Describe(address, size)} but CheatEngine.SDK could not publish its owner, and " +
				$"releasing it ended with {released.Kind}: the allocation may remain in the target.");
	}

	/// <summary>Maps a cancellation observed after Cheat Engine allocated, once the allocation was released.</summary>
	/// <param name="released">The outcome of the release made because of the cancellation.</param>
	/// <param name="address">The allocated address.</param>
	/// <param name="size">The requested size.</param>
	/// <param name="operation">The public Client operation name.</param>
	/// <returns>
	///     A <see cref="CheatEngineFailureKind.Cancelled" /> failure: <see cref="CheatEngineHostEffect.Completed" /> when the
	///     release was confirmed, otherwise <see cref="CheatEngineHostEffect.CleanupUnconfirmed" /> with the address in the
	///     message.
	/// </returns>
	internal static CheatEngineFailure CancelledAfterAllocation(LeaseReleaseOutcome released, Address address, long size,
		string operation)
	{
		return released.IsComplete
			? CancellationMapping.AfterNativeCall(operation,
				"The operation was cancelled after Cheat Engine allocated the memory; the allocation was released and no " +
				"lease was published.")
			: Failure(CheatEngineFailureKind.Cancelled, operation, CheatEngineHostEffect.CleanupUnconfirmed,
				$"The operation was cancelled after Cheat Engine allocated {Describe(address, size)}, and releasing it " +
				$"ended with {released.Kind}: the allocation may remain in the target.");
	}

	/// <summary>Describes an allocation for a failure message: its size and hexadecimal target address.</summary>
	internal static string Describe(Address address, long size)
	{
		return string.Create(CultureInfo.InvariantCulture, $"{size} bytes at 0x{address.Value:X}");
	}

	private static CheatEngineFailure Failure(CheatEngineFailureKind kind, string operation,
		CheatEngineHostEffect hostEffect, string message)
	{
		return new CheatEngineFailure(kind, operation, message, null, hostEffect);
	}
}
