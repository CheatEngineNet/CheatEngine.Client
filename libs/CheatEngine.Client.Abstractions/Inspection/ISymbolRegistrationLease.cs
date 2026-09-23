using CheatEngine.SDK.Engine.Values;

namespace CheatEngine.Client.Inspection;

/// <summary>Owns one Client-created Cheat Engine symbol registration for the current activation epoch.</summary>
/// <remarks>
///     <para>
///         Disposing the lease removes only the symbol registered through this lease, and only while the name still
///         resolves to <see cref="Address" />: a name that a third party replaced is left in place, and a name removed
///         outside the lease is not touched. The check and the unregistration are best effort, not atomic: a third party
///         can replace the name in between, and a third-party registration of the same name at the same address is
///         indistinguishable and is removed. <see cref="IDetailedSymbolRegistrationLease" /> reports which case happened.
///     </para>
///     <para>
///         When the ownership check or the unregistration fails, <see cref="IDisposable.Dispose" /> throws a
///         <see cref="CheatEngine.Client.Results.CheatEngineOperationException" /> whose failure has kind
///         <see cref="CheatEngine.Client.Results.CheatEngineFailureKind.IndeterminateHostResult" /> and host effect
///         <see cref="CheatEngine.Client.Results.CheatEngineHostEffect.CleanupUnconfirmed" />; the lease stays active so the
///         activation cleanup can try again. The activation owner also disposes a forgotten lease before the SDK detaches
///         the Lua runtime.
///     </para>
/// </remarks>
public interface ISymbolRegistrationLease : IDisposable
{
	/// <summary>Gets the exact symbol name registered in Cheat Engine.</summary>
	public string Name
	{
		get;
	}

	/// <summary>Gets the target address bound to <see cref="Name" />.</summary>
	public Address Address
	{
		get;
	}

	/// <summary>Gets a value indicating whether this lease has already released its registration.</summary>
	public bool IsReleased
	{
		get;
	}
}
