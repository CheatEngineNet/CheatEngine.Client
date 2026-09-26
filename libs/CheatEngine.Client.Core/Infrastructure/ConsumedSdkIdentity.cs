using System.Globalization;
using System.Reflection;

using CheatEngine.Client.Runtime;
using CheatEngine.SDK.Engine.Runtime;

namespace CheatEngine.Client.Core.Infrastructure;

/// <summary>
///     The consumed CheatEngine.SDK identity embedded in this Client build, compared with the CheatEngine.SDK.Engine
///     assembly actually loaded (audit ADR-09, ADR-10, A10-02, A21-35, A22-21).
/// </summary>
/// <remarks>
///     <para>
///         The build embeds, as <see cref="AssemblyMetadataAttribute" /> values of this assembly, the identity of the
///         CheatEngine.SDK package it restored: the version and NuGet content hash of the resolved entry of Core's
///         <c>packages.lock.json</c> (the version equals the pin of <c>eng/CheatEngineSdk.props</c>, or the build fails
///         with <c>CHEATENGINECLIENT9050</c>), the repository commit declared by the restored package whose content hash
///         equals the lock value, and the one CheatEngine.SDK major this Client supports
///         (<c>_CheatEngineClientSupportedSdkMajor</c> of <c>eng/CheatEngineSdk.props</c>). No version is written in
///         this source.
///     </para>
///     <para>
///         The package gate follows the dependency range the Client packages declare: it is
///         <see cref="ClientCapabilityEvidenceState.Satisfied" /> when the SemVer version of the loaded
///         CheatEngine.SDK.Engine informational version (build metadata ignored) has the supported major and is at
///         least the pin, by SemVer precedence, so a prerelease of the pin is below it. The reason and
///         <see cref="IdentityLabel" /> say whether the loaded assembly is exactly the reviewed package
///         (<c>{version}+{sourceCommit}</c>, <see cref="ExactReviewedIdentity" />) or another release of the supported
///         major. Another major or a version below the pin is <see cref="ClientCapabilityEvidenceState.Missing" />. An
///         identity without embedded evidence (a defensive case: a build that cannot embed it fails with
///         <c>CHEATENGINECLIENT9050</c>), an SDK assembly without an informational version and an informational version
///         that is not a semantic version are <see cref="ClientCapabilityEvidenceState.Unknown" />.
///     </para>
///     <para>
///         Only assembly-level attributes are read (AOT-safe); no file, network, or Lua access happens, so the identity is
///         available during enable without touching Cheat Engine.
///     </para>
/// </remarks>
internal sealed class ConsumedSdkIdentity
{
	/// <summary>The assembly-metadata key of the consumed CheatEngine.SDK version.</summary>
	internal const string VersionKey = "CheatEngine.Client.ConsumedSdk.Version";

	/// <summary>The assembly-metadata key of the consumed CheatEngine.SDK source commit.</summary>
	internal const string SourceCommitKey = "CheatEngine.Client.ConsumedSdk.SourceCommit";

	/// <summary>The assembly-metadata key of the consumed CheatEngine.SDK NuGet content hash (SHA-512, base64).</summary>
	internal const string ContentHashKey = "CheatEngine.Client.ConsumedSdk.ContentHashSha512";

	/// <summary>The assembly-metadata key of the one CheatEngine.SDK major this Client supports.</summary>
	internal const string SupportedMajorKey = "CheatEngine.Client.ConsumedSdk.SupportedMajor";

	/// <summary>
	///     The id of the Cheat Engine host profile that the consumed CheatEngine.SDK 2.0.0 names as qualifiable. It names
	///     the supported profile; it is not a Client qualification (the Client tuple stays <c>NotExecuted</c> until a
	///     Client receipt exists).
	/// </summary>
	internal const string SupportedHostProfileId = "ce-7.7.0.10621-x64-managed-hostfxr";

	private const string NotComparedLabel = "not compared";

	private static readonly Lazy<ConsumedSdkIdentity> LazyCurrent =
		new(ReadCurrent, LazyThreadSafetyMode.ExecutionAndPublication);

	/// <summary>Creates an identity from explicit values; tests use it to supply expected and loaded identities.</summary>
	/// <param name="version">The embedded consumed version, the pin.</param>
	/// <param name="sourceCommit">The embedded source commit of the consumed package.</param>
	/// <param name="contentHashSha512">The embedded NuGet content hash of the consumed package.</param>
	/// <param name="supportedMajor">The embedded supported major, as the decimal text of the assembly metadata.</param>
	/// <param name="loadedInformationalVersion">The informational version of the loaded CheatEngine.SDK.Engine.</param>
	internal ConsumedSdkIdentity(string? version, string? sourceCommit, string? contentHashSha512,
		string? supportedMajor, string? loadedInformationalVersion)
	{
		Version = Normalize(version);
		SourceCommit = Normalize(sourceCommit);
		ContentHashSha512 = Normalize(contentHashSha512);
		SupportedMajor = SemanticVersion.TryParseNumericIdentifier(Normalize(supportedMajor), out int major)
			? major
			: null;
		LoadedInformationalVersion = Normalize(loadedInformationalVersion);
		(PackageGate, IdentityLabel, ExactReviewedIdentity) = Evaluate();
	}

	/// <summary>Gets the identity of this build compared with the CheatEngine.SDK.Engine assembly loaded in the process.</summary>
	internal static ConsumedSdkIdentity Current => LazyCurrent.Value;

	/// <summary>Gets an identity without embedded evidence and without a loaded version.</summary>
	internal static ConsumedSdkIdentity NotEmbedded
	{
		get;
	} = new(null, null, null, null, null);

	/// <summary>Gets the embedded consumed CheatEngine.SDK version (the pin), or <see langword="null" />.</summary>
	internal string? Version
	{
		get;
	}

	/// <summary>Gets the embedded consumed CheatEngine.SDK source commit, or <see langword="null" />.</summary>
	internal string? SourceCommit
	{
		get;
	}

	/// <summary>Gets the embedded NuGet content hash of the consumed package, or <see langword="null" />.</summary>
	internal string? ContentHashSha512
	{
		get;
	}

	/// <summary>Gets the embedded CheatEngine.SDK major this Client supports, or <see langword="null" />.</summary>
	internal int? SupportedMajor
	{
		get;
	}

	/// <summary>Gets the informational version of the loaded CheatEngine.SDK.Engine assembly, or <see langword="null" />.</summary>
	internal string? LoadedInformationalVersion
	{
		get;
	}

	/// <summary>Gets whether the version, commit, content hash and supported major were all embedded at build.</summary>
	internal bool IsEmbedded => Version is not null && SourceCommit is not null && ContentHashSha512 is not null &&
								SupportedMajor is not null;

	/// <summary>
	///     Gets the informational version of the reviewed package, or <see langword="null" /> when not embedded.
	/// </summary>
	internal string? ExpectedInformationalVersion => IsEmbedded ? $"{Version}+{SourceCommit}" : null;

	/// <summary>
	///     Gets whether the loaded CheatEngine.SDK.Engine declares exactly the informational version of the reviewed
	///     package this build consumed. The package gate does not require it; it is kept for the qualification gate,
	///     because a qualification receipt covers only the exact tuple it was produced with.
	/// </summary>
	internal bool ExactReviewedIdentity
	{
		get;
	}

	/// <summary>Gets the package evidence gate shared by every operational Client capability.</summary>
	internal ClientCapabilityEvidenceGate PackageGate
	{
		get;
	}

	/// <summary>
	///     Gets a short description of how the loaded CheatEngine.SDK.Engine relates to the consumed package, for the
	///     activation log: built from versions only, never from a path.
	/// </summary>
	internal string IdentityLabel
	{
		get;
	}

	private (ClientCapabilityEvidenceGate Gate, string Label, bool Exact) Evaluate()
	{
		if (!IsEmbedded)
		{
			return (new ClientCapabilityEvidenceGate(ClientCapabilityEvidenceState.Unknown,
				"This Client build embeds no consumed CheatEngine.SDK identity, so the runtime snapshot does not " +
				"establish the identity of the consumed SDK package artifact."), NotComparedLabel, false);
		}

		if (!SemanticVersion.TryParse(Version!, out SemanticVersion pin) || pin.Major != SupportedMajor)
		{
			return (new ClientCapabilityEvidenceGate(ClientCapabilityEvidenceState.Unknown,
				$"This Client build embeds the consumed CheatEngine.SDK version '{Version}' with the supported major " +
				$"{SupportedMajor}, which is not a pin of that major, so no loaded package can be compared with it."),
				NotComparedLabel, false);
		}

		if (LoadedInformationalVersion is null)
		{
			return (new ClientCapabilityEvidenceGate(ClientCapabilityEvidenceState.Unknown,
				"The loaded CheatEngine.SDK.Engine assembly declares no informational version, so it cannot be " +
				$"compared with the package this Client build consumed ({ExpectedInformationalVersion})."),
				NotComparedLabel, false);
		}

		if (!SemanticVersion.TryParse(LoadedInformationalVersion, out SemanticVersion loaded))
		{
			return (new ClientCapabilityEvidenceGate(ClientCapabilityEvidenceState.Unknown,
				$"The loaded CheatEngine.SDK.Engine informational version '{LoadedInformationalVersion}' is not a " +
				"semantic version, so it cannot be compared with the package this Client build consumed " +
				$"({ExpectedInformationalVersion})."), NotComparedLabel, false);
		}

		string supported = $"{SupportedMajor}.x at or above {Version}";
		if (string.Equals(LoadedInformationalVersion, ExpectedInformationalVersion, StringComparison.Ordinal))
		{
			return (new ClientCapabilityEvidenceGate(ClientCapabilityEvidenceState.Satisfied,
				$"The loaded CheatEngine.SDK.Engine {LoadedInformationalVersion} is exactly the reviewed package this " +
				$"Client build consumed (NuGet content hash {ContentHashSha512}, read from the lock file at build time)."),
				"the reviewed package", true);
		}

		if (loaded.Major == SupportedMajor && SemanticVersion.Compare(loaded, pin) >= 0)
		{
			return (new ClientCapabilityEvidenceGate(ClientCapabilityEvidenceState.Satisfied,
				$"The loaded CheatEngine.SDK.Engine {LoadedInformationalVersion} is a release of the supported " +
				$"CheatEngine.SDK {supported}; it is another release than the reviewed package this Client build " +
				$"consumed ({ExpectedInformationalVersion})."), $"another release of {supported}", false);
		}

		return (new ClientCapabilityEvidenceGate(ClientCapabilityEvidenceState.Missing,
			$"The loaded CheatEngine.SDK.Engine {LoadedInformationalVersion} is not a release of the supported " +
			$"CheatEngine.SDK {supported}, the range of the package this Client build consumed " +
			$"({ExpectedInformationalVersion})."), $"outside {supported}", false);
	}

	private static ConsumedSdkIdentity ReadCurrent()
	{
		string? version = null;
		string? sourceCommit = null;
		string? contentHash = null;
		string? supportedMajor = null;
		foreach (AssemblyMetadataAttribute metadata in typeof(ConsumedSdkIdentity).Assembly
					 .GetCustomAttributes<AssemblyMetadataAttribute>())
		{
			switch (metadata.Key)
			{
				case VersionKey:
					version = metadata.Value;
					break;
				case SourceCommitKey:
					sourceCommit = metadata.Value;
					break;
				case ContentHashKey:
					contentHash = metadata.Value;
					break;
				case SupportedMajorKey:
					supportedMajor = metadata.Value;
					break;
			}
		}

		string? loaded = typeof(RuntimeInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
			?.InformationalVersion;
		return new ConsumedSdkIdentity(version, sourceCommit, contentHash, supportedMajor, loaded);
	}

	private static string? Normalize(string? value)
	{
		return string.IsNullOrWhiteSpace(value) ? null : value;
	}

	/// <summary>
	///     The precedence-relevant part of a SemVer 2.0.0 version (https://semver.org/#spec-item-11): major, minor, patch
	///     and prerelease identifiers. Build metadata is validated and ignored.
	/// </summary>
	private readonly struct SemanticVersion
	{
		private readonly string[] _prerelease;

		private SemanticVersion(int major, int minor, int patch, string[] prerelease)
		{
			Major = major;
			Minor = minor;
			Patch = patch;
			_prerelease = prerelease;
		}

		internal int Major
		{
			get;
		}

		private int Minor
		{
			get;
		}

		private int Patch
		{
			get;
		}

		internal static bool TryParse(string value, out SemanticVersion version)
		{
			version = default;
			int plus = value.IndexOf('+', StringComparison.Ordinal);
			if (plus >= 0 && !AreIdentifiers(value[(plus + 1)..], false))
			{
				return false;
			}

			string precedence = plus < 0 ? value : value[..plus];
			int dash = precedence.IndexOf('-', StringComparison.Ordinal);
			string[] core = (dash < 0 ? precedence : precedence[..dash]).Split('.');
			if (core.Length != 3 || !TryParseNumericIdentifier(core[0], out int major) ||
				!TryParseNumericIdentifier(core[1], out int minor) || !TryParseNumericIdentifier(core[2], out int patch))
			{
				return false;
			}

			string[] prerelease = [];
			if (dash >= 0)
			{
				string identifiers = precedence[(dash + 1)..];
				if (!AreIdentifiers(identifiers, true))
				{
					return false;
				}

				prerelease = identifiers.Split('.');
			}

			version = new SemanticVersion(major, minor, patch, prerelease);
			return true;
		}

		/// <summary>Parses a SemVer numeric identifier: ASCII digits, no leading zero, within <see cref="int" />.</summary>
		internal static bool TryParseNumericIdentifier(string? value, out int number)
		{
			number = 0;
			return value is not null && IsNumeric(value) && (value.Length == 1 || value[0] != '0') &&
				   int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out number);
		}

		/// <summary>Compares two versions by SemVer precedence: a release is above each of its prereleases.</summary>
		internal static int Compare(SemanticVersion left, SemanticVersion right)
		{
			if (left.Major != right.Major)
			{
				return left.Major.CompareTo(right.Major);
			}

			if (left.Minor != right.Minor)
			{
				return left.Minor.CompareTo(right.Minor);
			}

			if (left.Patch != right.Patch)
			{
				return left.Patch.CompareTo(right.Patch);
			}

			string[] leftPrerelease = left._prerelease ?? [];
			string[] rightPrerelease = right._prerelease ?? [];
			if (leftPrerelease.Length == 0 || rightPrerelease.Length == 0)
			{
				return rightPrerelease.Length.CompareTo(leftPrerelease.Length);
			}

			for (int index = 0; index < Math.Min(leftPrerelease.Length, rightPrerelease.Length); index++)
			{
				int identifier = CompareIdentifiers(leftPrerelease[index], rightPrerelease[index]);
				if (identifier != 0)
				{
					return identifier;
				}
			}

			return leftPrerelease.Length.CompareTo(rightPrerelease.Length);
		}

		private static int CompareIdentifiers(string left, string right)
		{
			bool leftNumeric = IsNumeric(left);
			bool rightNumeric = IsNumeric(right);
			if (leftNumeric && rightNumeric)
			{
				// Numeric identifiers have no leading zero, so a longer one is larger.
				return left.Length != right.Length
					? left.Length.CompareTo(right.Length)
					: string.CompareOrdinal(left, right);
			}

			if (leftNumeric != rightNumeric)
			{
				return leftNumeric ? -1 : 1;
			}

			return string.CompareOrdinal(left, right);
		}

		/// <summary>
		///     Checks dot-separated SemVer identifiers: non-empty, ASCII alphanumerics and hyphens, and, for prerelease
		///     identifiers, no leading zero on a numeric one.
		/// </summary>
		private static bool AreIdentifiers(string value, bool prerelease)
		{
			foreach (string identifier in value.Split('.'))
			{
				if (identifier.Length == 0 || !identifier.All(IsIdentifierCharacter))
				{
					return false;
				}

				if (prerelease && IsNumeric(identifier) && identifier.Length > 1 && identifier[0] == '0')
				{
					return false;
				}
			}

			return true;
		}

		private static bool IsIdentifierCharacter(char character)
		{
			return char.IsAsciiLetterOrDigit(character) || character == '-';
		}

		private static bool IsNumeric(string value)
		{
			return value.Length > 0 && value.All(char.IsAsciiDigit);
		}
	}
}
