using System.Diagnostics.CodeAnalysis;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.RegularExpressions;
using System.Xml.Linq;

using CheatEngine.Client.Tests.Infrastructure;

using ReflectionAssembly = System.Reflection.Assembly;

namespace CheatEngine.Client.Tests.Architecture;

/// <summary>
///     C0 ratchet: the Client uses no <c>[Experimental]</c> member of the consumed CheatEngine.SDK and never suppresses
///     an SDK experimental diagnostic (<c>CESDK5xxx</c>).
/// </summary>
/// <remarks>
///     <para>
///         The metadata scan reads every <c>[Experimental]</c> type and member of the referenced SDK assemblies and
///         fails when a shipped Client assembly references one. <see cref="ExperimentalFloor" /> proves the scan is not
///         vacuous: each floor member must be found experimental in the consumed package, with its diagnostic id.
///     </para>
///     <para>
///         The text scan covers suppression mechanisms only: <c>#pragma warning disable</c>, <c>SuppressMessage</c>
///         attributes, MSBuild <c>NoWarn</c> and <c>WarningsNotAsErrors</c>, <c>.editorconfig</c> and
///         <c>.globalconfig</c> severities, and response-file <c>-nowarn</c>. Comments and documentation may cite the
///         identifiers.
///     </para>
/// </remarks>
public sealed partial class SdkExperimentalApiRatchetTests
{
	private const string ExperimentalAttributeType = "System.Diagnostics.CodeAnalysis.ExperimentalAttribute";

	private const int RegexTimeoutMilliseconds = 1000;

	private const string AobScanner = "CheatEngine.SDK.Engine.Scanning.Aob.AobScanner";

	private const string AobTypes = "CheatEngine.SDK.Engine.Scanning.Aob.";

	private const string MemoryScanSession = "CheatEngine.SDK.Engine.Scanning.Values.MemoryScanSession";

	/// <summary>The bounded AOB scan without a deadline is stable API; the scan must tell the two overloads apart.</summary>
	private const string StableBoundedScan =
		AobScanner + "::TryScanWithinBounds(string," + AobTypes + "AobScanBounds," + AobTypes + "AobScanOptions," +
		"System.Span`1<CheatEngine.SDK.Engine.Values.Address>,System.Threading.CancellationToken)->" + AobTypes +
		"AobBoundedScanResult";

	/// <summary>
	///     The experimental members of CheatEngine.SDK 2.0.0 the scan must find, with the diagnostic id each one raises.
	/// </summary>
	private static readonly (string Member, string DiagnosticId)[] ExperimentalFloor =
	[
		("CheatEngine.SDK.Lua.Runtime.LuaRuntime::AdmitWorkerThreads()->void", "CESDK5001"),
		(MemoryScanSession +
		 "::TryWaitForCompletion(System.TimeSpan)->CheatEngine.SDK.Engine.Scanning.Values.MemoryScanWaitStatus",
			"CESDK5010"),
		(MemoryScanSession +
		 "::TryTerminateScan(System.TimeSpan)->CheatEngine.SDK.Engine.Scanning.Values.MemoryScanTerminationStatus",
			"CESDK5010"),
		(AobScanner + "::TryScanWithinBounds(string," + AobTypes + "AobScanBounds," + AobTypes + "AobScanOptions," +
		 "System.TimeSpan,System.Span`1<CheatEngine.SDK.Engine.Values.Address>,System.Threading.CancellationToken)->" +
		 AobTypes + "AobBoundedScanResult", "CESDK5010"),
		(AobScanner + "::TryFindFirstFoundWithinBounds(string," + AobTypes + "AobScanBounds," + AobTypes +
		 "AobScanOptions,System.Threading.CancellationToken)->" + AobTypes + "AobFirstFoundResult", "CESDK5011")
	];

	private static readonly HashSet<string> SkippedDirectories = new(StringComparer.OrdinalIgnoreCase)
	{
		"artifacts", "bin", "obj", ".git", ".vs", ".idea", "TestResults", "node_modules"
	};

	private static readonly HashSet<string> MsBuildExtensions = new(StringComparer.OrdinalIgnoreCase)
	{
		".csproj", ".props", ".targets"
	};

	private static readonly string[] MsBuildSuppressionElements = ["NoWarn", "WarningsNotAsErrors"];

	private static readonly Lazy<ExperimentalSurface> Surface = new(ReadExperimentalSurface,
		LazyThreadSafetyMode.ExecutionAndPublication);

	[Fact]
	public void TheConsumedSdkMarksEveryFloorMemberExperimental()
	{
		ExperimentalSurface surface = Surface.Value;
		string inventory = string.Join(Environment.NewLine,
			surface.Members.OrderBy(static entry => entry.Key, StringComparer.Ordinal)
				.Select(static entry => $"{entry.Value} {entry.Key}"));

		foreach ((string member, string diagnosticId) in ExperimentalFloor)
		{
			Assert.True(surface.Members.TryGetValue(member, out string? found),
				$"{member} is not [Experimental] in the consumed CheatEngine.SDK. Found:{Environment.NewLine}{inventory}");
			Assert.Equal(diagnosticId, found);
		}

		Assert.DoesNotContain(StableBoundedScan, surface.Members.Keys);
	}

	[Fact]
	public void ClientAssembliesReferenceNoExperimentalSdkApi()
	{
		ExperimentalSurface surface = Surface.Value;
		List<string> violations = [];
		foreach (string assembly in ClientAssemblyCatalog.Names)
		{
			ClientAssemblyCatalog.ReadMetadata(assembly, (reader, _) =>
			{
				foreach (MemberReferenceHandle handle in reader.MemberReferences)
				{
					string member = MetadataSurface.DescribeMember(reader, handle,
						out MetadataSurface.TypeIdentity declaringType);
					if (declaringType.IsSdk && surface.TryGetDiagnosticId(member, declaringType, out string? id))
					{
						violations.Add($"{assembly} references {member} [Experimental(\"{id}\")].");
					}
				}

				foreach (TypeReferenceHandle handle in reader.TypeReferences)
				{
					MetadataSurface.TypeIdentity type = MetadataSurface.ResolveType(reader, handle);
					if (type.IsSdk && surface.TryGetTypeDiagnosticId(type, out string? id))
					{
						violations.Add($"{assembly} references {type.FullName} [Experimental(\"{id}\")].");
					}
				}
			});
		}

		Assert.True(violations.Count == 0,
			"The Client uses no experimental CheatEngine.SDK API (decision 3 of the 1.0 plan):" + Environment.NewLine +
			string.Join(Environment.NewLine, violations));
	}

	[Fact]
	public void NoSourceOrBuildFileSuppressesAnSdkExperimentalDiagnostic()
	{
		List<string> offenders = [];
		int scanned = 0;
		foreach (string file in EnumerateScannedFiles())
		{
			scanned++;
			string relative = Path.GetRelativePath(RepositoryLayout.Root, file).Replace('\\', '/');
			offenders.AddRange(FindSuppressions(relative, File.ReadAllText(file))
				.Select(suppression => $"{relative}: {suppression}"));
		}

		Assert.True(scanned > 100, $"Only {scanned} files were scanned; the repository walk is broken.");
		Assert.True(offenders.Count == 0,
			"An SDK experimental diagnostic is suppressed; the Client never opts into experimental SDK API:" +
			Environment.NewLine + string.Join(Environment.NewLine, offenders));
	}

	[Theory]
	[InlineData("Probe.cs", "#pragma warning disable CESDK5010")]
	[InlineData("Probe.cs", "\t#pragma warning disable CS0618, CESDK5001 // deliberate")]
	[InlineData("Probe.cs", "[SuppressMessage(\"Usage\", \"CESDK5011:Experimental API\", Justification = \"x\")]")]
	[InlineData("Probe.cs",
		"[assembly: System.Diagnostics.CodeAnalysis.SuppressMessage(\"Usage\",\n\t\"CESDK5010\", Justification = \"x\")]")]
	[InlineData("Probe.props", "<Project><PropertyGroup><NoWarn>$(NoWarn);CESDK5010</NoWarn></PropertyGroup></Project>")]
	[InlineData("Probe.csproj",
		"<Project><PropertyGroup><WarningsNotAsErrors>CESDK5001</WarningsNotAsErrors></PropertyGroup></Project>")]
	[InlineData("Probe.targets", "<Project><ItemGroup><PackageReference Include=\"X\" NoWarn=\"CESDK5011\" /></ItemGroup></Project>")]
	[InlineData(".editorconfig", "[*.cs]\ndotnet_diagnostic.CESDK5010.severity = none")]
	[InlineData("Probe.globalconfig", "is_global = true\ndotnet_diagnostic.CESDK5001.severity = warning")]
	[InlineData("Directory.Build.rsp", "-nowarn:CESDK5011")]
	public void TheSuppressionScanRecognizesEverySuppressionForm(string path, string text)
	{
		Assert.NotEmpty(FindSuppressions(path, text));
	}

	[Theory]
	[InlineData("Probe.cs", "// CESDK5010 marks the deadline overload of TryScanWithinBounds experimental.")]
	[InlineData("Probe.cs", "/// The SDK gates AdmitWorkerThreads behind <c>[Experimental(\"CESDK5001\")]</c>.")]
	[InlineData("Probe.cs", "#pragma warning disable CS0618 // CESDK5010 stays an error")]
	[InlineData("Probe.props", "<Project><!-- Never add CESDK5010 to NoWarn. --><PropertyGroup><NoWarn>CS1591</NoWarn></PropertyGroup></Project>")]
	[InlineData(".editorconfig", "# dotnet_diagnostic.CESDK5001.severity = none is forbidden")]
	public void CommentsMayCiteSdkExperimentalDiagnostics(string path, string text)
	{
		Assert.Empty(FindSuppressions(path, text));
	}

	/// <summary>Returns every suppression of an SDK experimental diagnostic in one file.</summary>
	private static List<string> FindSuppressions(string path, string text)
	{
		string name = Path.GetFileName(path);
		string extension = Path.GetExtension(path);
		List<string> found = [];
		if (extension.Equals(".cs", StringComparison.OrdinalIgnoreCase))
		{
			found.AddRange(PragmaDisable().Matches(text)
				.Where(static match => ExperimentalId().IsMatch(match.Groups["ids"].Value.Split("//", 2)[0]))
				.Select(static match => match.Value.Trim()));
			found.AddRange(SuppressMessageAttribute().Matches(text)
				.Where(static match => ExperimentalId().IsMatch(match.Groups["arguments"].Value))
				.Select(static match => match.Value.Trim()));
		}
		else if (MsBuildExtensions.Contains(extension))
		{
			XDocument document = XDocument.Parse(text);
			found.AddRange(document.Descendants()
				.Where(static element => MsBuildSuppressionElements.Contains(element.Name.LocalName) &&
										 ExperimentalId().IsMatch(element.Value))
				.Select(static element => $"<{element.Name.LocalName}>{element.Value}</{element.Name.LocalName}>"));
			found.AddRange(document.Descendants().Attributes()
				.Where(static attribute => MsBuildSuppressionElements.Contains(attribute.Name.LocalName) &&
										   ExperimentalId().IsMatch(attribute.Value))
				.Select(static attribute => attribute.ToString()));
		}
		else if (name.Equals(".editorconfig", StringComparison.OrdinalIgnoreCase) ||
				 extension.Equals(".globalconfig", StringComparison.OrdinalIgnoreCase))
		{
			found.AddRange(SeverityConfiguration().Matches(text).Select(static match => match.Value.Trim()));
		}
		else if (extension.Equals(".rsp", StringComparison.OrdinalIgnoreCase))
		{
			found.AddRange(ResponseFileNoWarn().Matches(text).Select(static match => match.Value.Trim()));
		}

		return found;
	}

	private static IEnumerable<string> EnumerateScannedFiles()
	{
		Stack<string> directories = new([RepositoryLayout.Root]);
		while (directories.TryPop(out string? directory))
		{
			foreach (string child in Directory.EnumerateDirectories(directory))
			{
				if (!SkippedDirectories.Contains(Path.GetFileName(child)))
				{
					directories.Push(child);
				}
			}

			foreach (string file in Directory.EnumerateFiles(directory))
			{
				string extension = Path.GetExtension(file);
				if (extension.Equals(".cs", StringComparison.OrdinalIgnoreCase) || MsBuildExtensions.Contains(extension) ||
					extension.Equals(".globalconfig", StringComparison.OrdinalIgnoreCase) ||
					extension.Equals(".rsp", StringComparison.OrdinalIgnoreCase) ||
					Path.GetFileName(file).Equals(".editorconfig", StringComparison.OrdinalIgnoreCase))
				{
					yield return file;
				}
			}
		}
	}

	private static ExperimentalSurface ReadExperimentalSurface()
	{
		ExperimentalSurface surface = new();
		foreach (ReflectionAssembly assembly in ConsumedSdkAssemblies.All)
		{
			using FileStream stream = File.OpenRead(assembly.Location);
			using PEReader peReader = new(stream);
			MetadataReader reader = peReader.GetMetadataReader();
			if (TryGetDiagnosticId(reader, reader.GetAssemblyDefinition().GetCustomAttributes(), out string? assemblyId))
			{
				surface.Assemblies[reader.GetString(reader.GetAssemblyDefinition().Name)] = assemblyId;
			}

			foreach (TypeDefinitionHandle handle in reader.TypeDefinitions)
			{
				if (TryGetDiagnosticId(reader, reader.GetTypeDefinition(handle).GetCustomAttributes(), out string? id))
				{
					surface.Types[MetadataSurface.ResolveTypeDefinition(reader, handle).FullName] = id;
				}
			}

			foreach (MethodDefinitionHandle handle in reader.MethodDefinitions)
			{
				if (TryGetDiagnosticId(reader, reader.GetMethodDefinition(handle).GetCustomAttributes(), out string? id))
				{
					surface.Members[MetadataSurface.DescribeMethodDefinition(reader, handle)] = id;
				}
			}

			foreach (PropertyDefinitionHandle handle in reader.PropertyDefinitions)
			{
				PropertyDefinition property = reader.GetPropertyDefinition(handle);
				if (TryGetDiagnosticId(reader, property.GetCustomAttributes(), out string? id))
				{
					PropertyAccessors accessors = property.GetAccessors();
					AddAccessors(reader, surface, id, accessors.Getter, accessors.Setter);
				}
			}

			foreach (EventDefinitionHandle handle in reader.EventDefinitions)
			{
				EventDefinition definition = reader.GetEventDefinition(handle);
				if (TryGetDiagnosticId(reader, definition.GetCustomAttributes(), out string? id))
				{
					EventAccessors accessors = definition.GetAccessors();
					AddAccessors(reader, surface, id, accessors.Adder, accessors.Remover, accessors.Raiser);
				}
			}

			foreach (FieldDefinitionHandle handle in reader.FieldDefinitions)
			{
				if (TryGetDiagnosticId(reader, reader.GetFieldDefinition(handle).GetCustomAttributes(), out string? id))
				{
					surface.Members[MetadataSurface.DescribeFieldDefinition(reader, handle)] = id;
				}
			}
		}

		return surface;
	}

	private static void AddAccessors(MetadataReader reader, ExperimentalSurface surface, string id,
		params MethodDefinitionHandle[] accessors)
	{
		foreach (MethodDefinitionHandle accessor in accessors.Where(static accessor => !accessor.IsNil))
		{
			surface.Members[MetadataSurface.DescribeMethodDefinition(reader, accessor)] = id;
		}
	}

	private static bool TryGetDiagnosticId(MetadataReader reader, CustomAttributeHandleCollection attributes,
		[NotNullWhen(true)] out string? diagnosticId)
	{
		foreach (CustomAttributeHandle handle in attributes)
		{
			CustomAttribute attribute = reader.GetCustomAttribute(handle);
			if (MetadataSurface.GetAttributeTypeName(reader, attribute) != ExperimentalAttributeType)
			{
				continue;
			}

			// ECMA-335 II.23.3: the prolog 0x0001, then the diagnostic id as the only fixed argument (a SerString).
			BlobReader value = reader.GetBlobReader(attribute.Value);
			_ = value.ReadUInt16();
			diagnosticId = value.ReadSerializedString() ?? string.Empty;
			return true;
		}

		diagnosticId = null;
		return false;
	}

	[GeneratedRegex(@"CESDK5\d{3}", RegexOptions.CultureInvariant, RegexTimeoutMilliseconds)]
	private static partial Regex ExperimentalId();

	[GeneratedRegex(@"^[ \t]*#[ \t]*pragma[ \t]+warning[ \t]+disable\b(?<ids>[^\r\n]*)",
		RegexOptions.CultureInvariant | RegexOptions.Multiline, RegexTimeoutMilliseconds)]
	private static partial Regex PragmaDisable();

	[GeneratedRegex(
		@"^[ \t]*\[[ \t]*(?:(?:assembly|module)[ \t]*:[ \t]*)?(?:global::)?(?:System\.Diagnostics\.CodeAnalysis\.)?(?:Unconditional)?SuppressMessage(?:Attribute)?[ \t]*\((?<arguments>[^)]*)\)",
		RegexOptions.CultureInvariant | RegexOptions.Multiline, RegexTimeoutMilliseconds)]
	private static partial Regex SuppressMessageAttribute();

	[GeneratedRegex(@"^[ \t]*dotnet_diagnostic\.CESDK5\d{3}\.severity[ \t]*=[^\r\n]*",
		RegexOptions.CultureInvariant | RegexOptions.Multiline | RegexOptions.IgnoreCase, RegexTimeoutMilliseconds)]
	private static partial Regex SeverityConfiguration();

	[GeneratedRegex(@"(?:^|\s)[-/]nowarn:[^\r\n]*CESDK5\d{3}",
		RegexOptions.CultureInvariant | RegexOptions.Multiline | RegexOptions.IgnoreCase, RegexTimeoutMilliseconds)]
	private static partial Regex ResponseFileNoWarn();

	/// <summary>The <c>[Experimental]</c> assemblies, types and members of the consumed SDK, with their diagnostic ids.</summary>
	private sealed class ExperimentalSurface
	{
		internal Dictionary<string, string> Assemblies
		{
			get;
		} = new(StringComparer.Ordinal);

		internal Dictionary<string, string> Types
		{
			get;
		} = new(StringComparer.Ordinal);

		internal Dictionary<string, string> Members
		{
			get;
		} = new(StringComparer.Ordinal);

		/// <summary>Whether a referenced member, its declaring type, an enclosing type or its assembly is experimental.</summary>
		internal bool TryGetDiagnosticId(string member, MetadataSurface.TypeIdentity declaringType,
			[NotNullWhen(true)] out string? diagnosticId)
		{
			return Members.TryGetValue(member, out diagnosticId) || TryGetTypeDiagnosticId(declaringType, out diagnosticId);
		}

		/// <summary>Whether a referenced type, an enclosing type or its assembly is experimental.</summary>
		internal bool TryGetTypeDiagnosticId(MetadataSurface.TypeIdentity type, [NotNullWhen(true)] out string? diagnosticId)
		{
			if (Assemblies.TryGetValue(type.Assembly, out diagnosticId))
			{
				return true;
			}

			string[] nesting = type.FullName.Split('+');
			for (int depth = 1; depth <= nesting.Length; depth++)
			{
				if (Types.TryGetValue(string.Join('+', nesting[..depth]), out diagnosticId))
				{
					return true;
				}
			}

			diagnosticId = null;
			return false;
		}
	}
}
