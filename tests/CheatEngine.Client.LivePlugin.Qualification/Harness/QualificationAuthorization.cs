using System.Globalization;
using System.Text.Json;

namespace LivePlugin.Qualification.Harness;

// Re-implemented from CheatEngineNet/CheatEngine.SDK:tests/CheatEngine.SDK.LiveProbe/LiveProbeAuthorization.cs (the
// ce77-live-probe-v1 contract of the SDK qualification runner), so one runner-written manifest authorizes any harness.
// The evaluation is a pure function of IQualificationEnvironment, so every refusal is proven by tests without a host.

/// <summary>Why the qualification gate refused, or <see cref="None" /> when it allowed the run.</summary>
internal enum AuthorizationDenial
{
	/// <summary>The gate allowed the run.</summary>
	None = 0,

	/// <summary>The harness does not run in a 64-bit process.</summary>
	NotX64Process,

	/// <summary>The acknowledgement variable is absent or not the exact phrase.</summary>
	AcknowledgementMissing,

	/// <summary>The manifest variable is absent.</summary>
	ManifestMissing,

	/// <summary>The manifest cannot be read or lacks a required field.</summary>
	ManifestInvalid,

	/// <summary>The manifest names another acknowledgement.</summary>
	ManifestAcknowledgementMismatch,

	/// <summary>The manifest does not mark the target disposable.</summary>
	TargetNotDisposable,

	/// <summary>The manifest has expired.</summary>
	ManifestExpired,

	/// <summary>The manifest is valid for longer than the maximum lifetime.</summary>
	ManifestLifetimeTooLong,

	/// <summary>The host image cannot be inspected.</summary>
	HostUnavailable,

	/// <summary>The host is not the pinned Cheat Engine 7.7.0.10621 x64 executable, or the manifest pins another one.</summary>
	HostMismatch,

	/// <summary>The declared target is the Cheat Engine host itself.</summary>
	TargetIsHost,

	/// <summary>The declared target cannot be inspected.</summary>
	TargetUnavailable,

	/// <summary>The declared target's image differs from the manifest hash.</summary>
	TargetMismatch
}

/// <summary>The PE facts the gate needs about a process image. Paths never leave the harness.</summary>
/// <param name="Sha256">SHA-256 of the image file, upper-case hex.</param>
/// <param name="Machine">The COFF machine, for example <c>Amd64</c>.</param>
/// <param name="FileVersion">The file version resource, or <see langword="null" />.</param>
internal readonly record struct ProcessImage(string Sha256, string Machine, string? FileVersion);

/// <summary>What the gate reads from the process it runs in; the production implementation is <see cref="QualificationEnvironment" />.</summary>
internal interface IQualificationEnvironment
{
	/// <summary>Gets the current UTC time.</summary>
	public DateTimeOffset UtcNow
	{
		get;
	}

	/// <summary>Gets the id of the process the harness runs in (the Cheat Engine host).</summary>
	public int CurrentProcessId
	{
		get;
	}

	/// <summary>Gets a value indicating whether the process is 64-bit.</summary>
	public bool Is64BitProcess
	{
		get;
	}

	/// <summary>Reads an environment variable of the process.</summary>
	public string? GetVariable(string name);

	/// <summary>Reads a UTF-8 text file; <see langword="false" /> when it is absent or unreadable.</summary>
	public bool TryReadFile(string path, out string text);

	/// <summary>Describes the main image of a process; <see langword="false" /> when it cannot be inspected.</summary>
	public bool TryDescribeProcessImage(int processId, out ProcessImage image);
}

/// <summary>The gate's decision. It carries hashes and ids only, never a path, so it can be reported as is.</summary>
internal sealed record AuthorizationDecision(
	AuthorizationDenial Denial,
	int TargetProcessId,
	string TargetSha256,
	DateTimeOffset ExpiresUtc)
{
	/// <summary>Gets a value indicating whether the run is authorized.</summary>
	internal bool IsAllowed => Denial == AuthorizationDenial.None;

	/// <summary>Gets a denied decision.</summary>
	internal static AuthorizationDecision Denied(AuthorizationDenial denial)
	{
		return new AuthorizationDecision(denial, 0, string.Empty, default);
	}

	/// <summary>Whether the decision authorizes work on the given process: only the declared disposable target.</summary>
	internal bool Allows(int processId)
	{
		return IsAllowed && processId > 0 && processId == TargetProcessId;
	}
}

/// <summary>
///     The fail-closed authorization of mutating qualification functions. A command-line switch, a launch profile or a
///     target path alone is never authorization: the operator affirms the exact phrase, the SDK runner supplies a
///     short-lived <c>ce77-live-probe-v1</c> manifest, the host is the pinned Cheat Engine binary and the declared
///     disposable target is alive with the declared image.
/// </summary>
internal static class QualificationAuthorization
{
	internal const string Acknowledgement = "I_AUTHORIZE_CE77_LIVE_PROBES_ON_A_DISPOSABLE_TARGET";
	internal const string AcknowledgementVariable = "CE_SDK_LIVE_PROBE_ACKNOWLEDGEMENT";
	internal const string ManifestVariable = "CE_SDK_LIVE_PROBE_AUTHORIZATION_FILE";
	internal const string ManifestSchema = "ce77-live-probe-v1";

	/// <summary>SHA-256 of <c>cheatengine-x86_64.exe</c> 7.7.0.10621, the qualifiable host profile.</summary>
	internal const string ExactCheatEngineSha256 = "9727076DA50924E4A097B49A02155E4B34759269C3017FF31375364B8826EB4D";

	internal const string ExactCheatEngineFileVersion = "7.7.0.10621";

	/// <summary>The longest remaining validity a manifest may declare; the SDK runner writes 30 minutes.</summary>
	internal static readonly TimeSpan MaximumLifetime = TimeSpan.FromMinutes(30);

	/// <summary>Evaluates the gate against the given environment.</summary>
	internal static AuthorizationDecision Evaluate(IQualificationEnvironment environment)
	{
		ArgumentNullException.ThrowIfNull(environment);
		if (!environment.Is64BitProcess)
		{
			return AuthorizationDecision.Denied(AuthorizationDenial.NotX64Process);
		}

		if (!string.Equals(environment.GetVariable(AcknowledgementVariable), Acknowledgement, StringComparison.Ordinal))
		{
			return AuthorizationDecision.Denied(AuthorizationDenial.AcknowledgementMissing);
		}

		string? manifestPath = environment.GetVariable(ManifestVariable);
		if (string.IsNullOrWhiteSpace(manifestPath))
		{
			return AuthorizationDecision.Denied(AuthorizationDenial.ManifestMissing);
		}

		if (!environment.TryReadFile(manifestPath, out string text) || !TryParseManifest(text, out Manifest manifest))
		{
			return AuthorizationDecision.Denied(AuthorizationDenial.ManifestInvalid);
		}

		AuthorizationDenial denial = CheckManifest(manifest, environment.UtcNow);
		if (denial == AuthorizationDenial.None)
		{
			denial = CheckHost(manifest, environment);
		}

		if (denial == AuthorizationDenial.None)
		{
			denial = CheckTarget(manifest, environment);
		}

		return denial == AuthorizationDenial.None
			? new AuthorizationDecision(AuthorizationDenial.None, manifest.TargetProcessId, manifest.TargetSha256,
				manifest.ExpiresUtc)
			: AuthorizationDecision.Denied(denial);
	}

	private static AuthorizationDenial CheckManifest(Manifest manifest, DateTimeOffset now)
	{
		if (!string.Equals(manifest.Acknowledgement, Acknowledgement, StringComparison.Ordinal))
		{
			return AuthorizationDenial.ManifestAcknowledgementMismatch;
		}

		if (!manifest.Disposable)
		{
			return AuthorizationDenial.TargetNotDisposable;
		}

		if (manifest.ExpiresUtc <= now)
		{
			return AuthorizationDenial.ManifestExpired;
		}

		return manifest.ExpiresUtc - now > MaximumLifetime
			? AuthorizationDenial.ManifestLifetimeTooLong
			: AuthorizationDenial.None;
	}

	private static AuthorizationDenial CheckHost(Manifest manifest, IQualificationEnvironment environment)
	{
		if (!environment.TryDescribeProcessImage(environment.CurrentProcessId, out ProcessImage host))
		{
			return AuthorizationDenial.HostUnavailable;
		}

		bool exactHost = string.Equals(host.Sha256, ExactCheatEngineSha256, StringComparison.Ordinal) &&
						 string.Equals(host.Machine, "Amd64", StringComparison.Ordinal) &&
						 string.Equals(host.FileVersion, ExactCheatEngineFileVersion, StringComparison.Ordinal);
		return exactHost && string.Equals(manifest.HostSha256, ExactCheatEngineSha256, StringComparison.Ordinal)
			? AuthorizationDenial.None
			: AuthorizationDenial.HostMismatch;
	}

	private static AuthorizationDenial CheckTarget(Manifest manifest, IQualificationEnvironment environment)
	{
		if (manifest.TargetProcessId == environment.CurrentProcessId)
		{
			return AuthorizationDenial.TargetIsHost;
		}

		if (!environment.TryDescribeProcessImage(manifest.TargetProcessId, out ProcessImage target))
		{
			return AuthorizationDenial.TargetUnavailable;
		}

		return string.Equals(target.Sha256, manifest.TargetSha256, StringComparison.Ordinal)
			? AuthorizationDenial.None
			: AuthorizationDenial.TargetMismatch;
	}

	private static bool TryParseManifest(string text, out Manifest manifest)
	{
		manifest = default;
		try
		{
			using JsonDocument document = JsonDocument.Parse(text);
			JsonElement root = document.RootElement;
			if (root.ValueKind != JsonValueKind.Object ||
				!TryString(root, "schema", out string schema) ||
				!string.Equals(schema, ManifestSchema, StringComparison.Ordinal) ||
				!TryString(root, "acknowledgement", out string acknowledgement) ||
				!TryString(root, "hostSha256", out string hostSha256) ||
				!TryString(root, "targetSha256", out string targetSha256) ||
				!TryString(root, "expiresUtc", out string expires) ||
				!root.TryGetProperty("targetProcessId", out JsonElement processId) ||
				!processId.TryGetInt32(out int targetProcessId) || targetProcessId <= 0 ||
				!root.TryGetProperty("disposable", out JsonElement disposable) ||
				disposable.ValueKind is not (JsonValueKind.True or JsonValueKind.False) ||
				!DateTimeOffset.TryParse(expires, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind,
					out DateTimeOffset expiresUtc))
			{
				return false;
			}

			manifest = new Manifest(acknowledgement, NormalizeHash(hostSha256), targetProcessId,
				NormalizeHash(targetSha256), disposable.GetBoolean(), expiresUtc);
			return true;
		}
		catch (JsonException)
		{
			return false;
		}
	}

	private static bool TryString(JsonElement parent, string name, out string value)
	{
		value = string.Empty;
		if (!parent.TryGetProperty(name, out JsonElement property) || property.ValueKind != JsonValueKind.String)
		{
			return false;
		}

		value = property.GetString() ?? string.Empty;
		return value.Trim().Length > 0;
	}

	private static string NormalizeHash(string hash)
	{
		return hash.Replace("-", string.Empty, StringComparison.Ordinal).Trim().ToUpperInvariant();
	}

	private readonly record struct Manifest(
		string Acknowledgement,
		string HostSha256,
		int TargetProcessId,
		string TargetSha256,
		bool Disposable,
		DateTimeOffset ExpiresUtc);
}
