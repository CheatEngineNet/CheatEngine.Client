using CheatEngine.SDK.Engine.Runtime;

namespace CheatEngine.Client.Memory;

/// <summary>
///     Companion of <see cref="IMemoryReadContext" /> and <see cref="IMemoryWriteContext" /> that exposes the pointer-width
///     facts of the selected target to an application codec.
/// </summary>
/// <remarks>
///     <para>
///         <b>Call-only.</b> The Client implements this interface and applications call it. A minor release can add
///         members to it, so implement it only in a test double.
///     </para>
///     <para>
///         The Client's codec contexts implement this interface; test for it with a type check
///         (<c>context is IMemoryPointerWidthContext widths</c>). The facts are observed once per codec invocation, with
///         the opened process identifier read first; like the context itself, they expire when the codec returns.
///     </para>
///     <para>
///         Cheat Engine's <c>readPointer</c> follows the process width, not the configured pointer size (a Lua-only host
///         observation; not host-qualified). The configured pointer size is per-attachment Cheat Engine state that any
///         (re)attach resets; what it affects besides the value that Cheat Engine reports is not established. The
///         Client's own pointer-typed operations are refused when the two differ; an application codec decides for
///         itself.
///     </para>
/// </remarks>
public interface IMemoryPointerWidthContext
{
	/// <summary>
	///     Gets the process width of the selected target (the width Cheat Engine's <c>readPointer</c> uses), or unknown
	///     when no target is selected or the width could not be observed.
	/// </summary>
	public PointerSize ProcessPointerSize
	{
		get;
	}

	/// <summary>
	///     Gets the raw value of Cheat Engine's configured pointer size, or <see langword="null" /> when it was not
	///     observed. It can hold a value other than 4 or 8.
	/// </summary>
	public int? ConfiguredPointerSizeBytes
	{
		get;
	}

	/// <summary>Gets Cheat Engine's configured pointer size as a width when it is 4 or 8 bytes, otherwise unknown.</summary>
	public PointerSize ConfiguredPointerSize
	{
		get;
	}

	/// <summary>
	///     Gets whether the observed configured pointer size differs from a known process width; <see langword="false" />
	///     when either value is unknown.
	/// </summary>
	public bool ConfiguredPointerSizeDiffersFromProcessWidth
	{
		get;
	}
}
