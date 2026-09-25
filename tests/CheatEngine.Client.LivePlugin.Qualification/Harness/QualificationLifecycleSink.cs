using System.Text;

namespace LivePlugin.Qualification.Harness;

/// <summary>
///     The lifecycle receipt sink of Q43. The harness cannot answer a Lua call once Cheat Engine disables it (at
///     <c>closeCE</c> or through Settings &gt; Plugins), so it appends its lifecycle record and the templates of the log
///     events it captures to the file the runner named (<see cref="QualificationInputs.LifecycleFile" />), one line per
///     entry, flushed at once: <c>ledger&lt;TAB&gt;#&lt;enable&gt; &lt;stage&gt;[ &lt;exception type&gt;]</c> or
///     <c>log&lt;TAB&gt;&lt;level&gt;&lt;TAB&gt;&lt;category&gt;&lt;TAB&gt;&lt;event id&gt;&lt;TAB&gt;&lt;template&gt;</c>.
///     Like the ledger it never writes a formatted message, an exception message or a path, and a write that fails is
///     counted, never thrown: the sink must not change the lifecycle it records.
/// </summary>
internal static class QualificationLifecycleSink
{
	private static readonly Lock Gate = new();
	private static string? _path;
	private static int _failedWrites;

	/// <summary>Gets the number of lines that could not be written.</summary>
	internal static int FailedWrites
	{
		get
		{
			lock (Gate)
			{
				return _failedWrites;
			}
		}
	}

	/// <summary>Gets whether a sink file is configured for the current enable.</summary>
	internal static bool IsConfigured
	{
		get
		{
			lock (Gate)
			{
				return _path is not null;
			}
		}
	}

	/// <summary>Sets the sink file of this enable, or none; an unauthorized run never has one.</summary>
	internal static void Configure(string? path)
	{
		lock (Gate)
		{
			_path = path;
		}
	}

	/// <summary>Appends one lifecycle record entry.</summary>
	internal static void Ledger(string entry)
	{
		Append("ledger\t" + Clean(entry));
	}

	/// <summary>Appends the template of one captured log event.</summary>
	internal static void Log(CapturedLogEvent captured)
	{
		Append(string.Join('\t', "log", captured.Level.ToString(), Clean(captured.Category),
			captured.EventId.ToString(System.Globalization.CultureInfo.InvariantCulture), Clean(captured.Template)));
	}

	/// <summary>Replaces the control characters of a field, so every entry stays on one line with its fields apart.</summary>
	internal static string Clean(string value)
	{
		ArgumentNullException.ThrowIfNull(value);
		StringBuilder cleaned = new(value.Length);
		foreach (char character in value)
		{
			cleaned.Append(char.IsControl(character) ? ' ' : character);
		}

		return cleaned.ToString();
	}

	private static void Append(string line)
	{
		lock (Gate)
		{
			if (_path is null)
			{
				return;
			}

			try
			{
				File.AppendAllText(_path, line + "\n", new UTF8Encoding(false));
			}
			catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
												  or NotSupportedException or ArgumentException)
			{
				_failedWrites++;
			}
		}
	}
}
