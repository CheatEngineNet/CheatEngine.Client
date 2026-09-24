using System.Globalization;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;

using LivePlugin.Qualification.Harness;

namespace CheatEngine.Client.Tests.LiveQualification;

/// <summary>
///     Writes the files the qualification harness reads to authorize a session: the <c>ce77-live-probe-v1</c> manifest
///     (<see cref="QualificationAuthorization" />, compiled in from the harness) that names the pinned host, the disposable
///     target and a short expiry, and, for fault scenarios, the <c>liveprobe.fault.json</c> switch
///     (<see cref="QualificationFaultSwitch" />) next to the plugin.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class AuthorizationManifestWriter
{
	/// <summary>The longest validity the runner writes; the harness accepts at most 30 minutes.</summary>
	internal static readonly TimeSpan MaximumLifetime = TimeSpan.FromMinutes(25);

	private static readonly JsonWriterOptions WriterOptions = new()
	{
		Indented = true
	};

	/// <summary>Writes the manifest to <paramref name="path" /> and returns the path.</summary>
	internal static string Write(string path, string hostSha256, int targetProcessId, string targetSha256,
		DateTimeOffset now, TimeSpan lifetime)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(path);
		ArgumentException.ThrowIfNullOrWhiteSpace(hostSha256);
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(targetProcessId);
		ArgumentException.ThrowIfNullOrWhiteSpace(targetSha256);
		ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(lifetime, TimeSpan.Zero);
		ArgumentOutOfRangeException.ThrowIfGreaterThan(lifetime, MaximumLifetime);

		using MemoryStream buffer = new();
		using (Utf8JsonWriter json = new(buffer, WriterOptions))
		{
			json.WriteStartObject();
			json.WriteString("schema", QualificationAuthorization.ManifestSchema);
			json.WriteString("acknowledgement", QualificationAuthorization.Acknowledgement);
			json.WriteString("hostSha256", hostSha256);
			json.WriteNumber("targetProcessId", targetProcessId);
			json.WriteString("targetSha256", targetSha256);
			json.WriteBoolean("disposable", true);
			json.WriteString("expiresUtc", (now + lifetime).ToUniversalTime().ToString("o", CultureInfo.InvariantCulture));
			json.WriteEndObject();
		}

		File.WriteAllBytes(path, buffer.ToArray());
		return path;
	}

	/// <summary>The process variables that hand the manifest to the harness inside Cheat Engine.</summary>
	internal static IReadOnlyDictionary<string, string> SessionInputs(string manifestPath)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(manifestPath);
		return new Dictionary<string, string>(StringComparer.Ordinal)
		{
			[QualificationAuthorization.AcknowledgementVariable] = QualificationAuthorization.Acknowledgement,
			[QualificationAuthorization.ManifestVariable] = manifestPath
		};
	}

	/// <summary>Writes the fault switch next to the plugin, selecting <paramref name="stage" />, and returns its path.</summary>
	internal static string WriteFaultSwitch(string pluginDirectory, FaultStage stage)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(pluginDirectory);
		string path = Path.Combine(pluginDirectory, QualificationFaultSwitch.FileName);
		string text = string.Create(CultureInfo.InvariantCulture,
			$$"""{ "schema": "{{QualificationFaultSwitch.Schema}}", "throwIn": "{{stage}}" }""");
		File.WriteAllText(path, text, new UTF8Encoding(false));
		return path;
	}

	/// <summary>Removes the fault switch next to the plugin, if any.</summary>
	internal static void RemoveFaultSwitch(string pluginDirectory)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(pluginDirectory);
		File.Delete(Path.Combine(pluginDirectory, QualificationFaultSwitch.FileName));
	}
}
