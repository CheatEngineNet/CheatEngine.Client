using CheatEngine.SDK.Engine.Values;

namespace CheatEngine.Client.Inspection;

/// <summary>Owns one Client-created Cheat Engine symbol registration for the current activation epoch.</summary>
/// <remarks>
///     Disposing the lease removes only the symbol registered through this lease. The activation owner also disposes a
///     forgotten lease before the SDK detaches the Lua runtime.
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
