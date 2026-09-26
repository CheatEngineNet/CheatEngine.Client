using CheatEngine.SDK.Hosting.Diagnostics;

using Microsoft.Extensions.Logging;

namespace CheatEngine.Client.Hosting;

/// <summary>Writes <c>Microsoft.Extensions.Logging</c> entries to CheatEngine.SDK's <see cref="HostLog" />.</summary>
/// <remarks>
///     <para>
///         An entry is written as <c>category[event id]: text</c>. By default the text is the message template of the
///         entry, read from its <c>{OriginalFormat}</c> value, followed by the exception type name: argument values and
///         exception messages, which can hold user data, are never read (audit Q46). The template is the caller's text
///         as passed, so one built by string interpolation already carries its values; only constant structured
///         templates keep them out. An entry whose state carries no template is written as its category and event id
///         only. With formatted messages enabled, the text is the formatted message and the exception is passed to the
///         host log.
///     </para>
///     <para>
///         <see cref="HostLog.Write" /> never throws and drops an entry that a sink writes back into it on the same
///         thread, so this provider cannot recurse through a sink that forwards host log entries to
///         <c>ILogger</c>.
///     </para>
/// </remarks>
internal sealed class CheatEngineHostLogProvider(bool includeFormattedMessages) : ILoggerProvider
{
	private const string OriginalFormatKey = "{OriginalFormat}";

	/// <summary>Gets whether entries carry the formatted message and the exception instead of the template.</summary>
	internal bool IncludeFormattedMessages => includeFormattedMessages;

	public ILogger CreateLogger(string categoryName)
	{
		return new HostLogLogger(categoryName ?? string.Empty, includeFormattedMessages);
	}

	public void Dispose()
	{
		// The host log belongs to CheatEngine.SDK: this provider holds nothing to release.
	}

	/// <summary>Maps a logging level to its host log level; <see cref="LogLevel.Critical" /> becomes the highest, Error.</summary>
	/// <returns><see langword="false" /> for <see cref="LogLevel.None" /> and undefined levels, which are never written.</returns>
	internal static bool TryMapLevel(LogLevel logLevel, out HostLogLevel hostLevel)
	{
		(bool mapped, hostLevel) = logLevel switch
		{
			LogLevel.Trace or LogLevel.Debug => (true, HostLogLevel.Trace),
			LogLevel.Information => (true, HostLogLevel.Information),
			LogLevel.Warning => (true, HostLogLevel.Warning),
			LogLevel.Error or LogLevel.Critical => (true, HostLogLevel.Error),
			_ => (false, HostLogLevel.Trace)
		};
		return mapped;
	}

	/// <summary>Reads the message template of a structured state without formatting any of its values.</summary>
	internal static string? FindTemplate<TState>(TState state)
	{
		if (state is not IReadOnlyList<KeyValuePair<string, object?>> values)
		{
			return null;
		}

		// Microsoft.Extensions.Logging places the template last; the loop tolerates a state that does not.
		for (int index = values.Count - 1; index >= 0; index--)
		{
			KeyValuePair<string, object?> value = values[index];
			if (string.Equals(value.Key, OriginalFormatKey, StringComparison.Ordinal))
			{
				return value.Value as string;
			}
		}

		return null;
	}

	private sealed class HostLogLogger(string category, bool includeFormattedMessages) : ILogger
	{
		public IDisposable? BeginScope<TState>(TState state)
			where TState : notnull
		{
			return null;
		}

		public bool IsEnabled(LogLevel logLevel)
		{
			return TryMapLevel(logLevel, out HostLogLevel hostLevel) && HostLog.IsEnabled(hostLevel);
		}

		public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
			Func<TState, Exception?, string> formatter)
		{
			ArgumentNullException.ThrowIfNull(formatter);
			if (!TryMapLevel(logLevel, out HostLogLevel hostLevel) || !HostLog.IsEnabled(hostLevel))
			{
				return;
			}

			string prefix = $"{category}[{eventId.Id}]";
			if (includeFormattedMessages)
			{
				HostLog.Write(hostLevel, $"{prefix}: {formatter(state, exception)}", exception);
				return;
			}

			string? template = FindTemplate(state);
			string text = template is null ? prefix : $"{prefix}: {template}";
			HostLog.Write(hostLevel, exception is null
				? text
				: $"{text} ({exception.GetType().FullName ?? exception.GetType().Name})");
		}
	}
}
