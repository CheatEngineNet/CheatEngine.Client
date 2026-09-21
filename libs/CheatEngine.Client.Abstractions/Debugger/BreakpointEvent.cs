using System.Collections.Immutable;

using CheatEngine.SDK.Engine.Values;

namespace CheatEngine.Client.Debugger;

/// <summary>Contains copied data delivered synchronously for one breakpoint hit.</summary>
public readonly record struct BreakpointEvent
{
	/// <summary>Creates a copied breakpoint event.</summary>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="threadId" /> is negative.</exception>
	public BreakpointEvent(Address address, int threadId, ImmutableArray<BreakpointRegisterSnapshot> registers)
	{
		ArgumentOutOfRangeException.ThrowIfNegative(threadId);
		if (registers.IsDefault)
		{
			throw new ArgumentException("Register snapshots must be initialized.", nameof(registers));
		}

		Address = address;
		ThreadId = threadId;
		Registers = registers;
	}

	/// <summary>Gets the copied instruction address that triggered the breakpoint.</summary>
	public Address Address
	{
		get;
	}

	/// <summary>Gets the non-negative target thread identifier.</summary>
	public int ThreadId
	{
		get;
	}

	/// <summary>Gets immutable copied register observations.</summary>
	public ImmutableArray<BreakpointRegisterSnapshot> Registers
	{
		get;
	}
}
