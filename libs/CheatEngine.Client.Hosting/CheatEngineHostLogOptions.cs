namespace CheatEngine.Client.Hosting;

/// <summary>Options of the opt-in logging provider that writes to CheatEngine.SDK's host log.</summary>
/// <remarks>
///     They are set by the configuration delegate of <see cref="CheatEngineHostLogExtensions" />. The level threshold
///     is not an option: an entry is written when the logging filters admit it and the host log accepts its level.
/// </remarks>
public sealed class CheatEngineHostLogOptions
{
	/// <summary>Initializes the default options, which write message templates only.</summary>
	public CheatEngineHostLogOptions()
	{
	}

	/// <summary>
	///     Gets or sets whether an entry carries the formatted message and the exception instead of the message template.
	/// </summary>
	/// <remarks>
	///     <see langword="false" /> by default (audit Q46): an entry then carries the logger category, the event id, the
	///     message template with its placeholders (for example <c>{Epoch}</c>) and the exception type name, never a
	///     placeholder value or an exception message, because those can hold addresses, values, symbol expressions, paths
	///     or Lua text. The template is written as the caller passed it: a message built by string interpolation or
	///     concatenation is its own template and carries its values, so plugin code keeps them out of the host log only
	///     by logging constant structured templates or <c>LoggerMessage</c> methods, as the Client's own events do. Set it
	///     to <see langword="true" /> only to troubleshoot on a machine you control: the formatted message and the
	///     exception text then reach the host log sink, by default the Windows debug output of the Cheat Engine process,
	///     which any debugger or debug-output viewer of the session can read.
	/// </remarks>
	public bool IncludeFormattedMessages
	{
		get;
		set;
	}
}
