using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Runtime.CompilerServices;

using CheatEngine.Client.Results;
using CheatEngine.Client.SourceGenerators.Lua;


namespace CheatEngine.Client.Tests.Architecture;

/// <summary>
///     The mechanical rules of the public API charter (Abstractions README, "Public API charter") over every shipped
///     Client assembly: no CheatEngine.SDK outcome, status or kind type in a public signature, the Try and throwing
///     form pairs and their parameter order, no enum named <c>*Outcome</c>, no settable public struct, no record struct
///     that compares an <see cref="ImmutableArray{T}" /> by reference, no public Client exception constructor, and the
///     final list of the CheatEngine.SDK value types allowed in public signatures.
/// </summary>
/// <remarks>
///     The SDK-type rule reads the compiled metadata with <see cref="System.Reflection.Metadata" />: every type in the
///     signature of every public or protected member, generic arguments included, is inspected and no Client code runs.
///     The other rules read the loaded public types.
/// </remarks>
public sealed class PublicApiCharterTests
{
	private const string TryPrefix = "Try";

	private static readonly string[] SdkOutcomeSuffixes = ["Outcome", "Status", "Kind"];

	/// <summary>
	///     The CheatEngine.SDK value types allowed in public signatures; moving to CheatEngine.SDK 3.0 is a Client 2.0.
	/// </summary>
	private static readonly string[] FinalApprovedSdkValueTypes =
	[
		"CheatEngine.SDK.Engine.AddressList.MemoryRecordId",
		"CheatEngine.SDK.Engine.Enums.VariableType",
		"CheatEngine.SDK.Engine.Inspection.MemoryRegionInfo",
		"CheatEngine.SDK.Engine.Inspection.ModuleInfo",
		"CheatEngine.SDK.Engine.Inspection.ModuleName",
		"CheatEngine.SDK.Engine.Inspection.ModuleSectionInfo",
		"CheatEngine.SDK.Engine.Inspection.SymbolExpression",
		"CheatEngine.SDK.Engine.Inspection.SymbolInfo",
		"CheatEngine.SDK.Engine.Inspection.TargetProcessId",
		"CheatEngine.SDK.Engine.Runtime.CheatEngineArchitecture",
		"CheatEngine.SDK.Engine.Runtime.CheatEngineOperatingSystem",
		"CheatEngine.SDK.Engine.Runtime.CheatEngineVersion",
		"CheatEngine.SDK.Engine.Runtime.PointerSize",
		"CheatEngine.SDK.Engine.Runtime.TargetAbi",
		"CheatEngine.SDK.Engine.Runtime.TargetBackend",
		"CheatEngine.SDK.Engine.Values.Address"
	];

	/// <summary>
	///     The <c>Try</c> members exempt from the twin and token rules, by declaring type and name: BCL-shaped pure lookups
	///     and parses, which have no failure output, implementable callbacks and the codec contexts' accessors.
	/// </summary>
	private static readonly HashSet<string> TryFormExemptions = new(StringComparer.Ordinal)
	{
		"CheatEngine.Client.Runtime.ClientCapabilities.TryGet",
		"CheatEngine.Client.Scanning.AobPattern.TryParse",
		"CheatEngine.Client.Lua.ILuaOperation`1.TryExecute",
		"CheatEngine.Client.Memory.IMemoryCodec`1.TryRead",
		"CheatEngine.Client.Memory.IMemoryCodec`1.TryWrite",
		"CheatEngine.Client.Memory.IMemoryReadContext.TryReadBytes",
		"CheatEngine.Client.Memory.IMemoryWriteContext.TryWriteBytes"
	};

	[Fact]
	public void NoCheatEngineSdkOutcomeStatusOrKindTypeAppearsInAPublicSignature()
	{
		List<string> offenders = [];
		int inspected = 0;
		foreach (string assembly in ClientAssemblyCatalog.Names)
		{
			ClientAssemblyCatalog.ReadMetadata(assembly, (reader, _) =>
			{
				foreach ((string member, ImmutableArray<MetadataSurface.TypeIdentity> types) in
						 ReadPublicSignatures(reader))
				{
					inspected++;
					offenders.AddRange(types
						.Where(static type => type.IsSdk && SdkOutcomeSuffixes.Any(suffix =>
							type.FullName.EndsWith(suffix, StringComparison.Ordinal)))
						.Select(type => $"{assembly}: {member} uses {type.FullName}"));
				}
			});
		}

		Assert.True(inspected > 500, $"Only {inspected} public signatures were inspected.");
		Assert.True(offenders.Count == 0,
			"A public signature exposes a CheatEngine.SDK outcome, status or kind type; map it to a Client value:" +
			Environment.NewLine + string.Join(Environment.NewLine, offenders.Distinct(StringComparer.Ordinal)));
	}

	[Fact]
	public void EveryTryFormHasItsThrowingTwinAndTheCharterParameterOrder()
	{
		List<string> offenders = [];
		int pairs = 0;
		foreach (Type type in PublicTypes())
		{
			foreach (MethodInfo method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance |
														  BindingFlags.Static | BindingFlags.DeclaredOnly))
			{
				if (!IsTryForm(method))
				{
					continue;
				}

				string name = $"{type.FullName}.{method.Name}";
				ParameterInfo[] parameters = method.GetParameters();
				offenders.AddRange(CheckParameterOrder(name, parameters));
				if (TryFormExemptions.Contains(name))
				{
					continue;
				}

				if (!HasThrowingTwin(type, method))
				{
					offenders.Add($"{name}: no public {method.Name[TryPrefix.Length..]} with the same inputs");
				}
				else
				{
					pairs++;
				}
			}
		}

		Assert.True(pairs > 50, $"Only {pairs} Try and throwing pairs were found.");
		Assert.True(offenders.Count == 0,
			"A TryX form returns bool with its value and 'out CheatEngineFailure failure' last among the outputs, then " +
			"an optional CancellationToken; a throwing X form with the same inputs exists in the same type:" +
			Environment.NewLine + string.Join(Environment.NewLine, offenders));
	}

	[Fact]
	public void NoPublicEnumIsNamedOutcome()
	{
		string[] offenders =
		[
			.. PublicTypes().Where(static type => type.IsEnum && type.Name.EndsWith("Outcome", StringComparison.Ordinal))
				.Select(static type => type.FullName!)
		];

		Assert.True(offenders.Length == 0,
			"'*Outcome' names a result object, never an enum: " + string.Join(", ", offenders));
	}

	[Fact]
	public void NoPublicStructHasAnInitOrSetAccessor()
	{
		string[] offenders =
		[
			.. PublicTypes().Where(static type => type is { IsValueType: true, IsEnum: false })
				.SelectMany(static type => type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
					.Where(static property => property.SetMethod is { IsPublic: true } or { IsFamily: true })
					.Select(property => $"{type.FullName}.{property.Name}"))
		];

		Assert.True(offenders.Length == 0,
			"A public value type exposes get-only properties set by its constructor: " + string.Join(", ", offenders));
	}

	[Fact]
	public void NoPublicRecordStructHoldsAnImmutableArray()
	{
		string[] records = [.. PublicTypes().Where(IsRecordStruct).Select(static type => type.FullName!)];
		string[] offenders =
		[
			.. PublicTypes().Where(IsRecordStruct)
				.SelectMany(static type => type
					.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
					.Where(static field => field.FieldType.IsGenericType &&
										   field.FieldType.GetGenericTypeDefinition() == typeof(ImmutableArray<>))
					.Select(field => $"{type.FullName}.{field.Name}"))
		];

		// Record structs remain for the value types whose member-wise equality is meaningful; CheatEngineFailure compares
		// its Exception by reference, as the charter says.
		Assert.Contains("CheatEngine.Client.Results.CheatEngineFailure", records);
		Assert.True(offenders.Length == 0,
			"A record struct compares an ImmutableArray by reference; make the type a plain readonly struct: " +
			string.Join(", ", offenders));
	}

	[Fact]
	public void NoClientExceptionHasAPublicOrProtectedConstructor()
	{
		Type[] exceptions = [.. PublicTypes().Where(static type => typeof(Exception).IsAssignableFrom(type))];
		string[] offenders =
		[
			.. exceptions.SelectMany(static type => type
				.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
				.Where(static constructor => constructor.IsPublic || constructor.IsFamily ||
											 constructor.IsFamilyOrAssembly)
				.Select(constructor => $"{type.FullName}({string.Join(", ",
					constructor.GetParameters().Select(static parameter => parameter.ParameterType.Name))})"))
		];

		Assert.Contains(typeof(CheatEngineInvalidStateException), exceptions);
		Assert.True(offenders.Length == 0,
			"A Client exception is created only by CheatEngineFailure.Throw or ToException: " +
			string.Join(", ", offenders));
	}

	[Fact]
	public void TheApprovedSdkValueTypesAreTheFinalSixteen()
	{
		Assert.Equal(FinalApprovedSdkValueTypes, ApprovedSdkClientTypes.Names);
	}

	private static bool IsTryForm(MethodInfo method)
	{
		return method.Name.Length > TryPrefix.Length && method.Name.StartsWith(TryPrefix, StringComparison.Ordinal) &&
			   char.IsUpper(method.Name[TryPrefix.Length]) && method.ReturnType == typeof(bool) &&
			   !method.IsSpecialName;
	}

	/// <summary>
	///     Checks the order the charter fixes: inputs, then the <c>out</c> outputs with <c>out CheatEngineFailure
	///     failure</c> last among them, then an optional <see cref="CancellationToken" />, always last.
	/// </summary>
	private static IEnumerable<string> CheckParameterOrder(string name, ParameterInfo[] parameters)
	{
		int firstOut = Array.FindIndex(parameters, static parameter => parameter.IsOut);
		int token = Array.FindIndex(parameters, static parameter => parameter.ParameterType == typeof(CancellationToken));
		int failure = Array.FindIndex(parameters,
			static parameter => parameter.IsOut && parameter.ParameterType.GetElementType() == typeof(CheatEngineFailure));
		if (token >= 0 && token != parameters.Length - 1)
		{
			yield return $"{name}: the CancellationToken is not the last parameter";
		}

		if (firstOut >= 0 && parameters.Take(token >= 0 ? token : parameters.Length).Skip(firstOut)
				.Any(static parameter => !parameter.IsOut))
		{
			yield return $"{name}: an input follows an out parameter";
		}

		if (failure >= 0)
		{
			int lastOut = Array.FindLastIndex(parameters, static parameter => parameter.IsOut);
			if (failure != lastOut)
			{
				yield return $"{name}: 'out CheatEngineFailure' is not the last output";
			}

			if (parameters[failure].Name != "failure")
			{
				yield return $"{name}: the failure output is named '{parameters[failure].Name}', not 'failure'";
			}
		}
	}

	/// <summary>Whether the declaring type has a public throwing form with the same inputs as the Try form.</summary>
	private static bool HasThrowingTwin(Type type, MethodInfo tryForm)
	{
		string twinName = tryForm.Name[TryPrefix.Length..];
		Type[] inputs = Inputs(tryForm);
		return type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
			.Where(candidate => candidate.Name == twinName &&
								candidate.GetGenericArguments().Length == tryForm.GetGenericArguments().Length)
			.Any(candidate => Inputs(candidate).Select(Normalize).SequenceEqual(inputs.Select(Normalize)));

		static Type[] Inputs(MethodInfo method)
		{
			return
			[
				.. method.GetParameters()
					.Where(static parameter => !parameter.IsOut && parameter.ParameterType != typeof(CancellationToken))
					.Select(static parameter => parameter.ParameterType)
			];
		}

		static string Normalize(Type type)
		{
			// A generic method parameter compares by position, whatever the declaring method.
			return type.IsGenericMethodParameter
				? $"!!{type.GenericParameterPosition}"
				: type.ContainsGenericParameters
					? type.Name
					: type.FullName ?? type.Name;
		}
	}

	private static bool IsRecordStruct(Type type)
	{
		return type is { IsValueType: true, IsEnum: false } &&
			   type.GetMethod("PrintMembers", BindingFlags.Instance | BindingFlags.NonPublic) is { } printMembers &&
			   printMembers.IsDefined(typeof(CompilerGeneratedAttribute));
	}

	private static IEnumerable<Type> PublicTypes()
	{
		return ClientAssemblyCatalog.LoadAll().SelectMany(static assembly => assembly.GetExportedTypes());
	}

	/// <summary>Reads the signature types of every public or protected member of every visible type.</summary>
	private static IEnumerable<(string Member, ImmutableArray<MetadataSurface.TypeIdentity> Types)> ReadPublicSignatures(
		MetadataReader reader)
	{
		TypeCollector collector = new();
		foreach (TypeDefinitionHandle typeHandle in reader.TypeDefinitions)
		{
			if (!IsVisible(reader, typeHandle))
			{
				continue;
			}

			TypeDefinition type = reader.GetTypeDefinition(typeHandle);
			string typeName = MetadataSurface.ResolveTypeDefinition(reader, typeHandle).FullName;
			if (!type.BaseType.IsNil)
			{
				yield return ($"{typeName} base type", Collect(reader, collector, type.BaseType));
			}

			foreach (InterfaceImplementationHandle implementation in type.GetInterfaceImplementations())
			{
				yield return ($"{typeName} interface",
					Collect(reader, collector, reader.GetInterfaceImplementation(implementation).Interface));
			}

			foreach (MethodDefinitionHandle methodHandle in type.GetMethods())
			{
				MethodDefinition method = reader.GetMethodDefinition(methodHandle);
				MethodAttributes access = method.Attributes & MethodAttributes.MemberAccessMask;
				if (access is MethodAttributes.Public or MethodAttributes.Family or MethodAttributes.FamORAssem)
				{
					MethodSignature<ImmutableArray<MetadataSurface.TypeIdentity>> signature =
						method.DecodeSignature(collector, null);
					yield return ($"{typeName}.{reader.GetString(method.Name)}",
						[.. signature.ReturnType, .. signature.ParameterTypes.SelectMany(static types => types)]);
				}
			}

			foreach (FieldDefinitionHandle fieldHandle in type.GetFields())
			{
				FieldDefinition field = reader.GetFieldDefinition(fieldHandle);
				FieldAttributes access = field.Attributes & FieldAttributes.FieldAccessMask;
				if (access is FieldAttributes.Public or FieldAttributes.Family or FieldAttributes.FamORAssem)
				{
					yield return ($"{typeName}.{reader.GetString(field.Name)}", field.DecodeSignature(collector, null));
				}
			}
		}
	}

	private static ImmutableArray<MetadataSurface.TypeIdentity> Collect(MetadataReader reader, TypeCollector collector,
		EntityHandle handle)
	{
		return handle.Kind switch
		{
			HandleKind.TypeDefinition => collector.GetTypeFromDefinition(reader, (TypeDefinitionHandle) handle, 0),
			HandleKind.TypeReference => collector.GetTypeFromReference(reader, (TypeReferenceHandle) handle, 0),
			HandleKind.TypeSpecification => collector.GetTypeFromSpecification(reader, null,
				(TypeSpecificationHandle) handle, 0),
			_ => []
		};
	}

	/// <summary>Whether a type definition is visible outside its assembly: public, or nested public in a visible type.</summary>
	private static bool IsVisible(MetadataReader reader, TypeDefinitionHandle handle)
	{
		TypeDefinition type = reader.GetTypeDefinition(handle);
		TypeAttributes visibility = type.Attributes & TypeAttributes.VisibilityMask;
		return visibility switch
		{
			TypeAttributes.Public => true,
			TypeAttributes.NestedPublic or TypeAttributes.NestedFamily or TypeAttributes.NestedFamORAssem =>
				IsVisible(reader, type.GetDeclaringType()),
			_ => false
		};
	}

	/// <summary>Collects every named type of a signature, generic arguments and element types included.</summary>
	private sealed class TypeCollector : ISignatureTypeProvider<ImmutableArray<MetadataSurface.TypeIdentity>, object?>
	{
		public ImmutableArray<MetadataSurface.TypeIdentity> GetArrayType(
			ImmutableArray<MetadataSurface.TypeIdentity> elementType, ArrayShape shape)
		{
			return elementType;
		}

		public ImmutableArray<MetadataSurface.TypeIdentity> GetByReferenceType(
			ImmutableArray<MetadataSurface.TypeIdentity> elementType)
		{
			return elementType;
		}

		public ImmutableArray<MetadataSurface.TypeIdentity> GetFunctionPointerType(
			MethodSignature<ImmutableArray<MetadataSurface.TypeIdentity>> signature)
		{
			return [.. signature.ReturnType, .. signature.ParameterTypes.SelectMany(static types => types)];
		}

		public ImmutableArray<MetadataSurface.TypeIdentity> GetGenericInstantiation(
			ImmutableArray<MetadataSurface.TypeIdentity> genericType,
			ImmutableArray<ImmutableArray<MetadataSurface.TypeIdentity>> typeArguments)
		{
			return [.. genericType, .. typeArguments.SelectMany(static types => types)];
		}

		public ImmutableArray<MetadataSurface.TypeIdentity> GetGenericMethodParameter(object? genericContext, int index)
		{
			return [];
		}

		public ImmutableArray<MetadataSurface.TypeIdentity> GetGenericTypeParameter(object? genericContext, int index)
		{
			return [];
		}

		public ImmutableArray<MetadataSurface.TypeIdentity> GetModifiedType(
			ImmutableArray<MetadataSurface.TypeIdentity> modifier,
			ImmutableArray<MetadataSurface.TypeIdentity> unmodifiedType, bool isRequired)
		{
			return unmodifiedType;
		}

		public ImmutableArray<MetadataSurface.TypeIdentity> GetPinnedType(
			ImmutableArray<MetadataSurface.TypeIdentity> elementType)
		{
			return elementType;
		}

		public ImmutableArray<MetadataSurface.TypeIdentity> GetPointerType(
			ImmutableArray<MetadataSurface.TypeIdentity> elementType)
		{
			return elementType;
		}

		public ImmutableArray<MetadataSurface.TypeIdentity> GetPrimitiveType(PrimitiveTypeCode typeCode)
		{
			return [];
		}

		public ImmutableArray<MetadataSurface.TypeIdentity> GetSZArrayType(
			ImmutableArray<MetadataSurface.TypeIdentity> elementType)
		{
			return elementType;
		}

		public ImmutableArray<MetadataSurface.TypeIdentity> GetTypeFromDefinition(MetadataReader reader,
			TypeDefinitionHandle handle, byte rawTypeKind)
		{
			return [MetadataSurface.ResolveTypeDefinition(reader, handle)];
		}

		public ImmutableArray<MetadataSurface.TypeIdentity> GetTypeFromReference(MetadataReader reader,
			TypeReferenceHandle handle, byte rawTypeKind)
		{
			return [MetadataSurface.ResolveType(reader, handle)];
		}

		public ImmutableArray<MetadataSurface.TypeIdentity> GetTypeFromSpecification(MetadataReader reader,
			object? genericContext, TypeSpecificationHandle handle, byte rawTypeKind)
		{
			return reader.GetTypeSpecification(handle).DecodeSignature(this, genericContext);
		}
	}
}
