namespace LivePlugin.Qualification.Harness;

/// <summary>A target range a mutating qualification function may write: declared, bounded, never inferred.</summary>
/// <param name="Id">The region's name in observations, for example <c>scratch</c>.</param>
/// <param name="Base">The first byte of the region in the target.</param>
/// <param name="Length">The number of writable bytes.</param>
internal readonly record struct WritableRegion(string Id, ulong Base, int Length)
{
	/// <summary>Whether <paramref name="length" /> bytes from <paramref name="address" /> lie inside the region.</summary>
	internal bool Contains(ulong address, int length)
	{
		if (length <= 0 || Length <= 0 || address < Base)
		{
			return false;
		}

		ulong offset = address - Base;
		return offset <= (ulong) Length && (ulong) length <= (ulong) Length - offset;
	}
}

/// <summary>
///     The writable regions declared for one target process by <c>cheatengine_client_qualification_target_declare</c>
///     after it verified them through the Client inspection API.
/// </summary>
/// <param name="ProcessId">The process the regions belong to.</param>
/// <param name="Regions">The declared regions.</param>
internal sealed record TargetDeclaration(int ProcessId, IReadOnlyList<WritableRegion> Regions);

/// <summary>Why a write was refused, or <see cref="None" /> when it is allowed.</summary>
internal enum WriteRefusal
{
	/// <summary>The write is allowed.</summary>
	None = 0,

	/// <summary>The qualification gate did not authorize the run.</summary>
	NotAuthorized,

	/// <summary>The Client observes no target, or another process than the authorized one.</summary>
	TargetNotAuthorized,

	/// <summary>No writable region was declared.</summary>
	NoDeclaration,

	/// <summary>The declaration names another process than the authorized, Client-visible one.</summary>
	DeclarationForAnotherProcess,

	/// <summary>The range is empty or outside every declared region.</summary>
	OutsideDeclaredRegion
}

/// <summary>
///     The fail-closed rule of every mutating harness function: a write reaches the Client memory API only when the gate
///     authorized the run, the Client observes exactly the authorized target, a declaration exists for that process, and
///     the whole written range lies inside one declared region. Pure, so every refusal is proven without a host.
/// </summary>
internal static class QualificationWriteGuard
{
	/// <summary>
	///     The first 64 KiB of a Windows user address space are never mapped. The partial-batch scenario (Q33) writes one
	///     element there on purpose, so that Cheat Engine refuses it with no effect anywhere.
	/// </summary>
	internal const ulong NeverMappedLimit = 0x10000;

	/// <summary>Whether an address lies in the never-mapped null region.</summary>
	internal static bool IsNeverMapped(ulong address)
	{
		return address < NeverMappedLimit;
	}

	/// <summary>Evaluates one intended write of <paramref name="length" /> bytes at <paramref name="address" />.</summary>
	internal static WriteRefusal Evaluate(AuthorizationDecision authorization, int clientProcessId,
		TargetDeclaration? declaration, ulong address, int length)
	{
		ArgumentNullException.ThrowIfNull(authorization);
		if (!authorization.IsAllowed)
		{
			return WriteRefusal.NotAuthorized;
		}

		if (!authorization.Allows(clientProcessId))
		{
			return WriteRefusal.TargetNotAuthorized;
		}

		if (declaration is null || declaration.Regions.Count == 0)
		{
			return WriteRefusal.NoDeclaration;
		}

		if (declaration.ProcessId != clientProcessId)
		{
			return WriteRefusal.DeclarationForAnotherProcess;
		}

		foreach (WritableRegion region in declaration.Regions)
		{
			if (region.Contains(address, length))
			{
				return WriteRefusal.None;
			}
		}

		return WriteRefusal.OutsideDeclaredRegion;
	}
}
