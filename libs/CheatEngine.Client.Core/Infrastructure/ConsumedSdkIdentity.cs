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
///         The build embeds the version, the source commit and the NuGet content hash of <c>eng/sdk/consumed-sdk.json</c>
///         as <see cref="AssemblyMetadataAttribute" /> values of this assembly. At runtime the package gate is
///         <see cref="ClientCapabilityEvidenceState.Satisfied" /> only when the loaded CheatEngine.SDK.Engine informational
///         version equals <c>{version}+{sourceCommit}</c>; a different package is
///         <see cref="ClientCapabilityEvidenceState.Missing" />; a build without embedded identity (the SDK-side canary)
///         or an SDK assembly without an informational version is <see cref="ClientCapabilityEvidenceState.Unknown" />.
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

	private static readonly Lazy<ConsumedSdkIdentity> _current =
		new(ReadCurrent, LazyThreadSafetyMode.ExecutionAndPublication);

	/// <summary>Creates an identity from explicit values; tests use it to supply expected and loaded identities.</summary>
	internal ConsumedSdkIdentity(string? version, string? sourceCommit, string? contentHashSha512,
		string? loadedInformationalVersion)
	{
		Version = Normalize(version);
		SourceCommit = Normalize(sourceCommit);
		ContentHashSha512 = Normalize(contentHashSha512);
		LoadedInformationalVersion = Normalize(loadedInformationalVersion);
		PackageGate = CreatePackageGate();
	}

	/// <summary>Gets the identity of this build compared with the CheatEngine.SDK.Engine assembly loaded in the process.</summary>
	internal static ConsumedSdkIdentity Current => _current.Value;

	/// <summary>Gets an identity without embedded evidence and without a loaded version.</summary>
	internal static ConsumedSdkIdentity NotEmbedded
	{
		get;
	} = new(null, null, null, null);

	/// <summary>Gets the embedded consumed CheatEngine.SDK version, or <see langword="null" />.</summary>
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

	/// <summary>Gets the informational version of the loaded CheatEngine.SDK.Engine assembly, or <see langword="null" />.</summary>
	internal string? LoadedInformationalVersion
	{
		get;
	}

	/// <summary>Gets whether the version, commit and content hash were all embedded at build time.</summary>
	internal bool IsEmbedded => Version is not null && SourceCommit is not null && ContentHashSha512 is not null;

	/// <summary>Gets the informational version the loaded SDK must declare, or <see langword="null" /> when not embedded.</summary>
	internal string? ExpectedInformationalVersion => IsEmbedded ? $"{Version}+{SourceCommit}" : null;

	/// <summary>Gets the package evidence gate shared by every operational Client capability.</summary>
	internal ClientCapabilityEvidenceGate PackageGate
	{
		get;
	}

	private ClientCapabilityEvidenceGate CreatePackageGate()
	{
		if (!IsEmbedded)
		{
			return new ClientCapabilityEvidenceGate(ClientCapabilityEvidenceState.Unknown,
				"This Client build embeds no consumed CheatEngine.SDK identity (for example an SDK canary build), so " +
				"the runtime snapshot does not establish the identity of the consumed SDK package artifact.");
		}

		if (LoadedInformationalVersion is null)
		{
			return new ClientCapabilityEvidenceGate(ClientCapabilityEvidenceState.Unknown,
				"The loaded CheatEngine.SDK.Engine assembly declares no informational version, so it cannot be " +
				$"compared with the package this Client build consumed ({ExpectedInformationalVersion}).");
		}

		return string.Equals(LoadedInformationalVersion, ExpectedInformationalVersion, StringComparison.Ordinal)
			? new ClientCapabilityEvidenceGate(ClientCapabilityEvidenceState.Satisfied,
				$"The loaded CheatEngine.SDK.Engine {LoadedInformationalVersion} is the package this Client build " +
				$"consumed (NuGet content hash {ContentHashSha512}, embedded from the consumed-SDK identity at build " +
				"time).")
			: new ClientCapabilityEvidenceGate(ClientCapabilityEvidenceState.Missing,
				$"The loaded CheatEngine.SDK.Engine {LoadedInformationalVersion} is not the package this Client build " +
				$"consumed ({ExpectedInformationalVersion}); the Client refuses to treat it as its SDK.");
	}

	private static ConsumedSdkIdentity ReadCurrent()
	{
		string? version = null;
		string? sourceCommit = null;
		string? contentHash = null;
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
			}
		}

		string? loaded = typeof(RuntimeInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
			?.InformationalVersion;
		return new ConsumedSdkIdentity(version, sourceCommit, contentHash, loaded);
	}

	private static string? Normalize(string? value)
	{
		return string.IsNullOrWhiteSpace(value) ? null : value;
	}
}
