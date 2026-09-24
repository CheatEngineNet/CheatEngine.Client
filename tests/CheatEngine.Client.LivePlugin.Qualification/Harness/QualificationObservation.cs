using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;

using CheatEngine.Client.Results;

namespace LivePlugin.Qualification.Harness;

/// <summary>
///     Builds the JSON observation a harness Lua function returns (schema
///     <c>cheatengine-client-qualification-observation/v0</c>). Written with <see cref="Utf8JsonWriter" /> only, so the
///     trimming and AOT analysis of the plugin stay clean. It is bounded and redacted by construction: a string that looks
///     like a local path is replaced, a failure is written as its kind, operation and host effect only (never its message
///     or exception text, which may carry user data: Q46), and an address list is written as its count and first and last
///     eight entries.
/// </summary>
internal sealed partial class QualificationObservation : IDisposable
{
	internal const string Schema = "cheatengine-client-qualification-observation/v0";
	internal const string RedactedPath = "<redacted-path>";
	internal const int AddressListEdge = 8;

	private static readonly JsonWriterOptions Options = new()
	{
		Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
	};

	private readonly MemoryStream _stream = new();
	private readonly Utf8JsonWriter _writer;
	private bool _completed;

	/// <summary>Starts the observation of one harness function.</summary>
	internal QualificationObservation(string function)
	{
		_writer = new Utf8JsonWriter(_stream, Options);
		_writer.WriteStartObject();
		_writer.WriteString("schema", Schema);
		_writer.WriteString("function", function);
	}

	/// <summary>Writes a string value, redacting anything that looks like a local path.</summary>
	internal QualificationObservation String(string name, string? value)
	{
		if (value is null)
		{
			_writer.WriteNull(name);
		}
		else
		{
			_writer.WriteString(name, LooksLikeLocalPath(value) ? RedactedPath : value);
		}

		return this;
	}

	/// <summary>Writes an integer value.</summary>
	internal QualificationObservation Number(string name, long value)
	{
		_writer.WriteNumber(name, value);
		return this;
	}

	/// <summary>Writes a boolean value.</summary>
	internal QualificationObservation Boolean(string name, bool value)
	{
		_writer.WriteBoolean(name, value);
		return this;
	}

	/// <summary>Writes a target address as <c>0x</c> upper-case hex.</summary>
	internal QualificationObservation Address(string name, ulong value)
	{
		_writer.WriteString(name, Hex(value));
		return this;
	}

	/// <summary>Writes a failure as its kind, operation and host effect; its message and exception are never written.</summary>
	internal QualificationObservation Failure(string name, CheatEngineFailure failure)
	{
		_writer.WriteStartObject(name);
		_writer.WriteString("kind", failure.Kind.ToString());
		_writer.WriteString("operation", failure.Operation);
		_writer.WriteString("hostEffect", failure.HostEffect.ToString());
		_writer.WriteEndObject();
		return this;
	}

	/// <summary>Writes an address list as its count and its first and last <see cref="AddressListEdge" /> entries.</summary>
	internal QualificationObservation Addresses(string name, IReadOnlyList<ulong> addresses)
	{
		ArgumentNullException.ThrowIfNull(addresses);
		_writer.WriteStartObject(name);
		_writer.WriteNumber("count", addresses.Count);
		_writer.WriteStartArray("first");
		for (int index = 0; index < Math.Min(AddressListEdge, addresses.Count); index++)
		{
			_writer.WriteStringValue(Hex(addresses[index]));
		}

		_writer.WriteEndArray();
		_writer.WriteStartArray("last");
		for (int index = Math.Max(AddressListEdge, addresses.Count - AddressListEdge); index < addresses.Count; index++)
		{
			_writer.WriteStringValue(Hex(addresses[index]));
		}

		_writer.WriteEndArray();
		_writer.WriteEndObject();
		return this;
	}

	/// <summary>Writes a list of short strings (names, stage records), each redacted like <see cref="String" />.</summary>
	internal QualificationObservation Strings(string name, IEnumerable<string> values)
	{
		ArgumentNullException.ThrowIfNull(values);
		_writer.WriteStartArray(name);
		foreach (string value in values)
		{
			_writer.WriteStringValue(LooksLikeLocalPath(value) ? RedactedPath : value);
		}

		_writer.WriteEndArray();
		return this;
	}

	/// <summary>Opens a nested object.</summary>
	internal QualificationObservation BeginObject(string name)
	{
		_writer.WriteStartObject(name);
		return this;
	}

	/// <summary>Closes the nested object.</summary>
	internal QualificationObservation EndObject()
	{
		_writer.WriteEndObject();
		return this;
	}

	/// <summary>Opens a nested array of objects.</summary>
	internal QualificationObservation BeginArray(string name)
	{
		_writer.WriteStartArray(name);
		return this;
	}

	/// <summary>Opens an object inside the current array.</summary>
	internal QualificationObservation BeginItem()
	{
		_writer.WriteStartObject();
		return this;
	}

	/// <summary>Closes the current array.</summary>
	internal QualificationObservation EndArray()
	{
		_writer.WriteEndArray();
		return this;
	}

	/// <summary>Closes the observation and returns its JSON text.</summary>
	internal string Complete()
	{
		if (!_completed)
		{
			_writer.WriteEndObject();
			_writer.Flush();
			_completed = true;
		}

		return Encoding.UTF8.GetString(_stream.ToArray());
	}

	/// <inheritdoc />
	public void Dispose()
	{
		_writer.Dispose();
		_stream.Dispose();
	}

	/// <summary>Whether a text holds a drive-rooted path, a user profile segment, a home directory or a file URI.</summary>
	internal static bool LooksLikeLocalPath(string value)
	{
		return LocalPath().IsMatch(value);
	}

	/// <summary>Formats a target address.</summary>
	internal static string Hex(ulong value)
	{
		return "0x" + value.ToString("X", CultureInfo.InvariantCulture);
	}

	[GeneratedRegex(@"(?<![A-Za-z0-9])[A-Za-z]:[\\/]|\\[Uu]sers\\|/Users/|(?<![A-Za-z0-9])/home/|[Ff][Ii][Ll][Ee]://|^\\\\",
		RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
	private static partial Regex LocalPath();
}
