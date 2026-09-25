using System.Collections.Immutable;

namespace CheatEngine.Client.Processes;

/// <summary>A copied, bounded local-process enumeration result.</summary>
public readonly record struct ProcessEnumerationResult
{
	private readonly ImmutableArray<ProcessInfoSnapshot> _processes;

	/// <summary>Creates a copied process enumeration result.</summary>
	public ProcessEnumerationResult(ImmutableArray<ProcessInfoSnapshot> processes, bool isTruncated)
	{
		_processes = processes.IsDefault ? ImmutableArray<ProcessInfoSnapshot>.Empty : processes;
		if (isTruncated && _processes.IsEmpty)
		{
			throw new ArgumentException("A truncated process result must retain at least one copied process.",
				nameof(processes));
		}

		IsTruncated = isTruncated;
	}

	/// <summary>Gets copied local-process metadata, ordered by process identifier.</summary>
	/// <remarks>Empty for the <see langword="default" /> value, never a default array.</remarks>
	public ImmutableArray<ProcessInfoSnapshot> Processes =>
		_processes.IsDefault ? ImmutableArray<ProcessInfoSnapshot>.Empty : _processes;

	/// <summary>Gets whether matching local processes were omitted at the caller's explicit limit.</summary>
	public bool IsTruncated
	{
		get;
	}
}
