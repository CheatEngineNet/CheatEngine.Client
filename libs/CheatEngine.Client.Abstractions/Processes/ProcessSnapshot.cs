using CheatEngine.SDK.Engine.Inspection;
using CheatEngine.SDK.Engine.Runtime;

namespace CheatEngine.Client.Processes;

/// <summary>An immutable snapshot of the process currently selected in Cheat Engine.</summary>
public readonly record struct ProcessSnapshot
{
	/// <summary>Creates a selected-process snapshot without a target-architecture observation.</summary>
	public ProcessSnapshot(TargetProcessId id, string? name, string? executablePath)
		: this(id, name, executablePath, CheatEngineArchitecture.Unknown, 0)
	{
	}

	/// <summary>Creates a selected-process snapshot from copied host observations.</summary>
	public ProcessSnapshot(
		TargetProcessId id,
		string? name,
		string? executablePath,
		CheatEngineArchitecture targetArchitecture,
		long selectionEpoch)
	{
		if (name is { Length: 0 })
		{
			throw new ArgumentException("A process name must be null or non-empty.", nameof(name));
		}

		if (executablePath is { Length: 0 })
		{
			throw new ArgumentException("An executable path must be null or non-empty.", nameof(executablePath));
		}

		ArgumentOutOfRangeException.ThrowIfNegative(selectionEpoch);

		Id = id;
		Name = name;
		ExecutablePath = executablePath;
		TargetArchitecture = targetArchitecture;
		SelectionEpoch = selectionEpoch;
	}

	/// <summary>Gets the selected process identifier.</summary>
	public TargetProcessId Id
	{
		get;
	}

	/// <summary>Gets the process display name when the host supplied one.</summary>
	public string? Name
	{
		get;
	}

	/// <summary>Gets the executable path when the host supplied one.</summary>
	public string? ExecutablePath
	{
		get;
	}

	/// <summary>Gets the target architecture observed by Cheat Engine, or unknown when no probe established it.</summary>
	public CheatEngineArchitecture TargetArchitecture
	{
		get;
	}

	/// <summary>Gets the pointer width implied by <see cref="TargetArchitecture" />, or unknown.</summary>
	public PointerSize TargetPointerSize => PointerSize.FromArchitecture(TargetArchitecture);

	/// <summary>
	///     Gets the target-selection epoch. A change means target-bound sessions and leases captured for an earlier
	///     selection are no longer valid.
	/// </summary>
	public long SelectionEpoch
	{
		get;
	}
}
