using CheatEngine.SDK.Engine.Values;

namespace CheatEngine.Client.Debugger;

/// <summary>Describes one Client-owned breakpoint.</summary>
public readonly record struct BreakpointRequest
{
	/// <summary>Creates a breakpoint request.</summary>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="kind" /> is not a defined value.</exception>
	public BreakpointRequest(Address address, BreakpointKind kind = BreakpointKind.Execute)
	{
		if (!Enum.IsDefined(kind))
		{
			throw new ArgumentOutOfRangeException(nameof(kind));
		}

		Address = address;
		Kind = kind;
	}

	/// <summary>Gets the target address observed by the breakpoint.</summary>
	public Address Address
	{
		get;
	}

	/// <summary>Gets the access kind that triggers the breakpoint.</summary>
	public BreakpointKind Kind
	{
		get;
	}
}
