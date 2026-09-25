using System.Text.RegularExpressions;

using Microsoft.Extensions.Logging;

namespace LivePlugin.Qualification.Harness;

/// <summary>One captured log event: where it came from and its message template, never the formatted message.</summary>
internal readonly record struct CapturedLogEvent(string Category, int EventId, string? EventName, LogLevel Level,
	string Template);

/// <summary>
///     The Q46 log sink of the harness. It is registered in the plugin's logging pipeline and records, for every event
///     the Client and the harness log, the category, event id, level and <c>{OriginalFormat}</c> template. The formatted
///     message and exception text are inspected once, to count <see cref="SensitiveHits" /> (a declared scenario value, a
///     target address of six or more hex digits, or the harness script marker), and are never stored: a formatted message
///     may carry the very data the scenario checks, or a user path the runner would have to redact.
/// </summary>
internal sealed partial class CapturingLoggerProvider : ILoggerProvider
{
	/// <summary>The marker every harness Auto Assembler or Lua script body contains.</summary>
	internal const string ScriptMarker = "cheatengine_client_qualification_script";

	/// <summary>The most events kept; older events are dropped and counted.</summary>
	internal const int Capacity = 512;

	private readonly Lock _gate = new();
	private readonly List<CapturedLogEvent> _events = [];
	private readonly HashSet<string> _sensitive = new(StringComparer.Ordinal);
	private int _dropped;
	private int _sensitiveHits;

	/// <summary>Gets the number of events whose formatted message or exception text contained sensitive data.</summary>
	internal int SensitiveHits
	{
		get
		{
			lock (_gate)
			{
				return _sensitiveHits;
			}
		}
	}

	/// <summary>Gets the number of events dropped because the capacity was reached.</summary>
	internal int Dropped
	{
		get
		{
			lock (_gate)
			{
				return _dropped;
			}
		}
	}

	/// <inheritdoc />
	public ILogger CreateLogger(string categoryName)
	{
		return new CapturingLogger(this, categoryName);
	}

	/// <inheritdoc />
	public void Dispose()
	{
	}

	/// <summary>Declares a value a scenario writes or searches, so that its appearance in a log counts as a hit.</summary>
	internal void DeclareSensitive(string value)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			return;
		}

		lock (_gate)
		{
			_sensitive.Add(value);
		}
	}

	/// <summary>Copies the captured events.</summary>
	internal IReadOnlyList<CapturedLogEvent> Events()
	{
		lock (_gate)
		{
			return [.. _events];
		}
	}

	/// <summary>
	///     Records one event: its template always, a hit when the formatted text carries sensitive data. The template is
	///     also appended to the lifecycle receipt sink when the runner configured one (Q43).
	/// </summary>
	internal void Record(CapturedLogEvent captured, string formatted, string? exceptionText)
	{
		QualificationLifecycleSink.Log(captured);
		lock (_gate)
		{
			if (IsSensitive(formatted) || (exceptionText is not null && IsSensitive(exceptionText)))
			{
				_sensitiveHits++;
			}

			if (_events.Count == Capacity)
			{
				_events.RemoveAt(0);
				_dropped++;
			}

			_events.Add(captured);
		}
	}

	private bool IsSensitive(string text)
	{
		if (Address().IsMatch(text) || text.Contains(ScriptMarker, StringComparison.Ordinal))
		{
			return true;
		}

		foreach (string value in _sensitive)
		{
			if (text.Contains(value, StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}
		}

		return false;
	}

	[GeneratedRegex("0x[0-9A-Fa-f]{6,}", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
	private static partial Regex Address();

	private sealed class CapturingLogger(CapturingLoggerProvider provider, string category) : ILogger
	{
		public IDisposable? BeginScope<TState>(TState state)
			where TState : notnull
		{
			return null;
		}

		public bool IsEnabled(LogLevel logLevel)
		{
			return logLevel != LogLevel.None;
		}

		public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
			Func<TState, Exception?, string> formatter)
		{
			ArgumentNullException.ThrowIfNull(formatter);
			string template = "<no template>";
			if (state is IReadOnlyList<KeyValuePair<string, object?>> values)
			{
				foreach (KeyValuePair<string, object?> value in values)
				{
					if (string.Equals(value.Key, "{OriginalFormat}", StringComparison.Ordinal) &&
						value.Value is string original)
					{
						template = original;
					}
				}
			}

			provider.Record(new CapturedLogEvent(category, eventId.Id, eventId.Name, logLevel, template),
				formatter(state, exception), exception?.ToString());
		}
	}
}
