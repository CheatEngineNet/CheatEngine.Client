using System.Collections;
using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;

using CheatEngine.Client.Extensions.DependencyInjection;
using CheatEngine.Client.Hosting;
using CheatEngine.Client.Memory;
using CheatEngine.Client.SourceGenerators.Lua;
using CheatEngine.SDK.Engine.Objects;
using CheatEngine.SDK.Engine.Values;
using CheatEngine.SDK.Lua.References;

using Microsoft.Win32.SafeHandles;

using ReflectionAssembly = System.Reflection.Assembly;

namespace CheatEngine.Client.Tests;

/// <summary>Protects the aggregate package boundary from raw SDK lifetime and Lua implementation types.</summary>
public sealed class PublicClientSignatureBoundaryTests
{
	private const int ClientBoundaryMaximumDepth = 32;
	private const int ClientBoundaryMaximumNodes = 256;

	// Single source shared with the Lua generator: source-generators/CheatEngine.Client.SourceGenerators.Lua/ApprovedSdkClientTypes.cs.
	private static readonly HashSet<string> ApprovedSdkValueTypes =
		new(ApprovedSdkClientTypes.Names, StringComparer.Ordinal);

	private static readonly HashSet<string> ApprovedFrameworkValueTypes = new(StringComparer.Ordinal)
	{
		"System.Action",
		"System.Attribute",
		"System.DateOnly",
		"System.DateTime",
		"System.DateTimeOffset",
		"System.Decimal",
		"System.Exception",
		"System.Guid",
		"System.Half",
		"System.Index",
		"System.Int128",
		"System.Range",
		"System.Text.Rune",
		"System.Threading.CancellationToken",
		"System.TimeOnly",
		"System.TimeSpan",
		"System.UInt128",
		"System.Version"
	};

	private static readonly HashSet<string> ApprovedFrameworkGenericCollectionTypes = new(StringComparer.Ordinal)
	{
		"System.Collections.Frozen.FrozenDictionary`2",
		"System.Collections.Frozen.FrozenSet`1",
		"System.Collections.Generic.IAsyncEnumerable`1",
		"System.Collections.Generic.IEnumerable`1",
		"System.Collections.Generic.IReadOnlyCollection`1",
		"System.Collections.Generic.IReadOnlyDictionary`2",
		"System.Collections.Generic.IReadOnlyList`1",
		"System.Collections.Generic.IReadOnlySet`1",
		"System.Collections.Immutable.IImmutableDictionary`2",
		"System.Collections.Immutable.IImmutableList`1",
		"System.Collections.Immutable.IImmutableQueue`1",
		"System.Collections.Immutable.IImmutableSet`1",
		"System.Collections.Immutable.IImmutableStack`1",
		"System.Collections.Immutable.ImmutableArray`1",
		"System.Collections.Immutable.ImmutableDictionary`2",
		"System.Collections.Immutable.ImmutableHashSet`1",
		"System.Collections.Immutable.ImmutableList`1",
		"System.Collections.Immutable.ImmutableQueue`1",
		"System.Collections.Immutable.ImmutableSortedDictionary`2",
		"System.Collections.Immutable.ImmutableSortedSet`1",
		"System.Collections.Immutable.ImmutableStack`1",
		"System.Collections.ObjectModel.ReadOnlyCollection`1",
		"System.Collections.ObjectModel.ReadOnlyDictionary`2",
		"System.Action`1",
		"System.Func`1",
		"System.Nullable`1",
		"System.ReadOnlySpan`1",
		"System.Span`1"
	};

	private static readonly HashSet<string> ApprovedFrameworkIntegrationTypes = new(StringComparer.Ordinal)
	{
		"Microsoft.Extensions.Configuration.ConfigurationManager",
		"Microsoft.Extensions.Configuration.IConfiguration",
		"Microsoft.Extensions.Configuration.IConfigurationRoot",
		"Microsoft.Extensions.Configuration.IConfigurationSection",
		"Microsoft.Extensions.DependencyInjection.IServiceCollection",
		"Microsoft.Extensions.DependencyInjection.IServiceScope",
		"Microsoft.Extensions.Logging.ILogger",
		"Microsoft.Extensions.Logging.ILoggingBuilder",
		"Microsoft.Extensions.Options.ValidateOptionsResult"
	};

	[Fact]
	public void AllPublicClientSignaturesAreHandleFreeAndUseOnlyApprovedSdkValueTypes()
	{
		List<string> violations = [];

		foreach (ReflectionAssembly assembly in GetAggregateClientAssemblies())
		{
			foreach (Type publicType in assembly.GetExportedTypes())
			{
				VerifyType(publicType, $"{assembly.GetName().Name}:{publicType.FullName}", violations);
				VerifyDeclaredMembers(publicType, violations);
			}
		}

		Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
	}

	[Fact]
	public void RecursiveVerifierRejectsNestedSdkHandlesOwnershipDelegatesAndConstraints()
	{
		AssertViolation(typeof(LuaRef[]), "forbidden SDK handle");
		AssertViolation(typeof(IReadOnlyList<Owned<CEObject>>), "forbidden SDK handle");
		AssertViolation(typeof((int Code, CEObject Handle)), "forbidden SDK handle");
		AssertViolation(typeof(Owned<>), "forbidden SDK handle");
		AssertViolation(typeof(LeakingDto), "forbidden SDK handle");
		AssertViolation(typeof(UnsafeCallback), "forbidden SDK handle");
		AssertViolation(typeof(ConstrainedDto<>), "forbidden SDK handle");
	}

	[Fact]
	public void RecursiveVerifierRejectsOpaqueFrameworkAndReflectionTypes()
	{
		AssertViolation(typeof(ArrayList), "unsupported framework type");
		AssertViolation(typeof(IEnumerable), "unsupported framework type");
		AssertViolation(typeof(GCHandle), "interop namespace type");
		AssertViolation(typeof(SafeFileHandle), "unsupported framework type");
		AssertViolation(typeof(ReflectionAssembly), "unsupported framework type");
		AssertViolation(typeof(MemberInfo), "unsupported framework type");
	}

	[Fact]
	public void RecursiveVerifierRejectsPointersByReferenceAndFunctionPointers()
	{
		AssertViolation(typeof(int).MakePointerType(), "pointer type");
		AssertViolation(typeof(LuaRef).MakeByRefType(), "forbidden SDK handle");

		MethodInfo method = typeof(PublicClientSignatureBoundaryTests).GetMethod(
			nameof(GetFunctionPointer),
			BindingFlags.Static | BindingFlags.NonPublic)!;
		AssertViolation(method.ReturnType, "function pointer");
	}

	[Fact]
	public void PublicSignaturesRejectDisguisedNativePointers()
	{
		AssertViolation(typeof(nint), "native-sized integer");
		AssertViolation(typeof(nuint), "native-sized integer");
		AssertViolation(typeof(IntPtr[]), "native-sized integer");
		AssertViolation(typeof(Func<nint>), "native-sized integer");
		AssertViolation(typeof(object), "unsupported framework type");
		AssertViolation(typeof(void).MakePointerType(), "pointer type");

		List<string> violations = [];
		VerifyType(typeof(int), "int", violations);
		VerifyType(typeof(ulong), "ulong", violations);
		VerifyType(typeof(Address), "address", violations);
		Assert.Empty(violations);
	}

	[Fact]
	public void RecursiveVerifierAllowsApprovedSdkValuesInsideSafeContainers()
	{
		List<string> violations = [];
		VerifyType(typeof(Address[]), "approved array", violations);
		VerifyType(typeof(IEnumerable<Address>), "approved enumerable", violations);
		VerifyType(typeof(IReadOnlyList<Address>), "approved generic", violations);
		VerifyType(typeof(ImmutableArray<IReadOnlyList<Address>>), "approved immutable generic", violations);
		VerifyType(typeof((Address Address, int Version)), "approved tuple", violations);

		Assert.Empty(violations);
	}

	[Fact]
	public void RecursiveVerifierFailsClosedWhenTheTypeGraphExceedsTheDepthBudget()
	{
		Type nested = typeof(Address);
		for (int index = 0; index <= ClientBoundaryMaximumDepth; index++)
		{
			nested = typeof(IReadOnlyList<>).MakeGenericType(nested);
		}

		AssertViolation(nested, "graph budget");
	}

	[Fact]
	public void RecursiveVerifierFailsClosedWhenTheTypeGraphExceedsTheNodeBudget()
	{
		AssemblyBuilder assembly = AssemblyBuilder.DefineDynamicAssembly(
			new AssemblyName("ClientBoundaryWideGraph"), AssemblyBuilderAccess.Run);
		ModuleBuilder module = assembly.DefineDynamicModule("ClientBoundaryWideGraph");
		Type[] leaves = Enumerable.Range(0, ClientBoundaryMaximumNodes)
			.Select(index => module.DefineType("Leaf" + index, TypeAttributes.Public).CreateType()!)
			.ToArray();
		TypeBuilder root = module.DefineType("Root", TypeAttributes.Public);
		foreach ((Type leaf, int index) in leaves.Select((leaf, index) => (leaf, index)))
		{
			root.DefineField("_leaf" + index, leaf, FieldAttributes.Private);
		}

		AssertViolation(root.CreateType()!, "graph budget");
	}

	private static IEnumerable<ReflectionAssembly> GetAggregateClientAssemblies()
	{
		Queue<ReflectionAssembly> pending = new(
		[
			typeof(ICheatEngineClient).Assembly,
			typeof(MemoryAddressBuilder).Assembly,
			typeof(CheatEngineClientBuilder).Assembly,
			typeof(CheatEngineClientPlugin).Assembly
		]);
		HashSet<string> visited = new(StringComparer.Ordinal);

		while (pending.TryDequeue(out ReflectionAssembly? candidate))
		{
			if (candidate is null)
			{
				continue;
			}

			ReflectionAssembly assembly = candidate;
			string? assemblyName = assembly.GetName().Name;
			if (assemblyName is null || !assemblyName.StartsWith("CheatEngine.Client", StringComparison.Ordinal) ||
				!visited.Add(assemblyName))
			{
				continue;
			}

			yield return assembly;

			foreach (AssemblyName reference in assembly.GetReferencedAssemblies())
			{
				if (reference.Name?.StartsWith("CheatEngine.Client", StringComparison.Ordinal) == true)
				{
					pending.Enqueue(ReflectionAssembly.Load(reference));
				}
			}
		}
	}

	private static void VerifyDeclaredMembers(Type publicType, List<string> violations)
	{
		const BindingFlags PublicDeclared = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static |
											BindingFlags.DeclaredOnly;
		if (typeof(Delegate).IsAssignableFrom(publicType))
		{
			MethodInfo? invoke = publicType.GetMethod("Invoke", BindingFlags.Public | BindingFlags.Instance);
			if (invoke is not null)
			{
				VerifyType(invoke.ReturnType, invoke.ToString(), violations, invoke);
				VerifyParameters(invoke.GetParameters(), invoke, violations);
			}

			return;
		}

		foreach (ConstructorInfo constructor in publicType.GetConstructors(PublicDeclared))
		{
			VerifyParameters(constructor.GetParameters(), constructor, violations);
		}

		foreach (MethodInfo method in publicType.GetMethods(PublicDeclared))
		{
			if (IsObjectEqualityMethod(method))
			{
				continue;
			}

			VerifyType(method.ReturnType, method.ToString(), violations, method);
			VerifyParameters(method.GetParameters(), method, violations);
			VerifyGenericParameterConstraints(method.GetGenericArguments(), method.ToString(), violations);
		}

		foreach (PropertyInfo property in publicType.GetProperties(PublicDeclared))
		{
			VerifyType(property.PropertyType, property.ToString(), violations, property);
			VerifyParameters(property.GetIndexParameters(), property, violations);
		}

		foreach (FieldInfo field in publicType.GetFields(PublicDeclared))
		{
			VerifyType(field.FieldType, field.ToString(), violations, field);
		}

		foreach (EventInfo @event in publicType.GetEvents(PublicDeclared))
		{
			VerifyType(@event.EventHandlerType, @event.ToString(), violations, @event);
		}

		VerifyGenericParameterConstraints(publicType.GetGenericArguments(), publicType.FullName, violations);
	}

	private static void VerifyParameters(IEnumerable<ParameterInfo> parameters, MemberInfo member,
		List<string> violations)
	{
		HashSet<Type> visited = [];
		int visitedCount = 0;
		VerifyParameters(parameters, member, violations, visited, ref visitedCount, 0);
	}

	private static void VerifyParameters(IEnumerable<ParameterInfo> parameters, MemberInfo member,
		List<string> violations, HashSet<Type> visited, ref int visitedCount, int depth)
	{
		foreach (ParameterInfo parameter in parameters)
		{
			VerifyType(parameter.ParameterType, $"{member} parameter '{parameter.Name}'", violations, member, visited,
				ref visitedCount, depth + 1);
		}
	}

	private static void VerifyGenericParameterConstraints(IEnumerable<Type> genericParameters, string? member,
		List<string> violations)
	{
		HashSet<Type> visited = [];
		int visitedCount = 0;
		VerifyGenericParameterConstraints(genericParameters, member, violations, visited, ref visitedCount, 0);
	}

	private static void VerifyGenericParameterConstraints(IEnumerable<Type> genericParameters, string? member,
		List<string> violations, HashSet<Type> visited, ref int visitedCount, int depth)
	{
		foreach (Type genericParameter in genericParameters.Where(static parameter => parameter.IsGenericParameter))
		{
			foreach (Type constraint in genericParameter.GetGenericParameterConstraints()
						 .OrderBy(static type => type.FullName, StringComparer.Ordinal))
			{
				if (constraint == typeof(ValueType) || constraint == typeof(Enum))
				{
					continue;
				}

				VerifyType(constraint, $"{member} generic parameter '{genericParameter.Name}'", violations, null,
					visited, ref visitedCount, depth + 1);
			}
		}
	}

	private static void VerifyType(Type? type, string? source, List<string> violations,
		MemberInfo? declaringMember = null)
	{
		HashSet<Type> visited = [];
		int visitedCount = 0;
		VerifyType(type, source, violations, declaringMember, visited, ref visitedCount, 0);
	}

	private static void VerifyType(Type? type, string? source, List<string> violations, MemberInfo? declaringMember,
		HashSet<Type> visited, ref int visitedCount, int depth)
	{
		if (type is null)
		{
			return;
		}

		if (depth > ClientBoundaryMaximumDepth)
		{
			violations.Add($"{source} exceeds the supported Client result type graph budget.");
			return;
		}

		if (!visited.Add(type))
		{
			return;
		}

		if (++visitedCount > ClientBoundaryMaximumNodes)
		{
			violations.Add($"{source} exceeds the supported Client result type graph budget.");
			return;
		}

		if (type.IsFunctionPointer)
		{
			violations.Add($"{source} exposes a function pointer type '{type}'.");
			return;
		}

		if (type.IsPointer)
		{
			violations.Add($"{source} exposes a pointer type '{type}'.");
			return;
		}

		if (type.IsByRef)
		{
			VerifyType(type.GetElementType(), source, violations, declaringMember, visited, ref visitedCount,
				depth + 1);
			return;
		}

		if (type.IsArray)
		{
			VerifyType(type.GetElementType(), source, violations, declaringMember, visited, ref visitedCount,
				depth + 1);
			return;
		}

		if (type.IsGenericParameter)
		{
			VerifyGenericParameterConstraints(type.GetGenericParameterConstraints(), source, violations, visited,
				ref visitedCount, depth);
			return;
		}

		if (IsNativeSized(type) && !IsApprovedNativeSizedMember(declaringMember))
		{
			violations.Add($"{source} exposes a native-sized integer '{type}' that could disguise a native pointer.");
			return;
		}

		if (IsForbiddenSdkType(type, source, violations, declaringMember) ||
			IsUnsupportedFrameworkType(type, source, violations, declaringMember))
		{
			return;
		}

		if (type.IsGenericType)
		{
			Type genericDefinition = type.GetGenericTypeDefinition();
			VerifyGenericParameterConstraints(genericDefinition.GetGenericArguments(), source, violations, visited,
				ref visitedCount, depth);
			foreach (Type argument in type.GetGenericArguments())
			{
				VerifyType(argument, source, violations, declaringMember, visited, ref visitedCount, depth + 1);
			}
		}

		if (typeof(Delegate).IsAssignableFrom(type))
		{
			MethodInfo? invoke = type.GetMethod("Invoke", BindingFlags.Public | BindingFlags.Instance);
			if (invoke is not null)
			{
				VerifyType(invoke.ReturnType, $"{source} delegate return", violations, invoke, visited,
					ref visitedCount,
					depth + 1);
				VerifyParameters(invoke.GetParameters(), invoke, violations, visited, ref visitedCount, depth);
			}

			return;
		}

		if (!ShouldInspectTypeMembers(type))
		{
			return;
		}

		VerifyTypeHierarchy(type, source, violations, declaringMember, visited, ref visitedCount, depth);
		VerifyTypeMembers(type, source, violations, visited, ref visitedCount, depth);
	}

	private static bool IsForbiddenSdkType(Type type, string? source, List<string> violations,
		MemberInfo? declaringMember)
	{
		Type definition = type.IsGenericType ? type.GetGenericTypeDefinition() : type;
		string typeName = definition.FullName ?? definition.Name;
		if (definition.Name is "LuaState" or "LuaRef" or "CEObject" ||
			definition.Name.StartsWith("Owned`", StringComparison.Ordinal))
		{
			violations.Add($"{source} exposes forbidden SDK handle '{typeName}'.");
			return true;
		}

		if (definition.Namespace?.Contains(".Interop", StringComparison.Ordinal) == true)
		{
			violations.Add($"{source} exposes interop namespace type '{typeName}'.");
			return true;
		}

		if (definition.Assembly.GetName().Name?.StartsWith("CheatEngine.SDK", StringComparison.Ordinal) == true &&
			(!definition.IsValueType || !ApprovedSdkValueTypes.Contains(typeName)))
		{
			violations.Add($"{source} exposes non-approved SDK type '{typeName}'.");
			return true;
		}

		return false;
	}

	private static bool IsUnsupportedFrameworkType(Type type, string? source, List<string> violations,
		MemberInfo? declaringMember)
	{
		Type definition = type.IsGenericType ? type.GetGenericTypeDefinition() : type;
		if (!IsFrameworkType(definition) || IsApprovedFrameworkClientBoundaryType(definition) ||
			IsApprovedAttributeMetadataType(definition, declaringMember))
		{
			return false;
		}

		string typeName = definition.FullName ?? definition.Name;
		violations.Add($"{source} exposes unsupported framework type '{typeName}'.");
		return true;
	}

	private static bool IsApprovedFrameworkClientBoundaryType(Type type)
	{
		Type definition = type.IsGenericType ? type.GetGenericTypeDefinition() : type;
		string typeName = definition.FullName ?? definition.Name;
		return IsScalar(type) || IsTuple(type) || ApprovedFrameworkValueTypes.Contains(typeName) ||
			   ApprovedFrameworkGenericCollectionTypes.Contains(typeName) ||
			   ApprovedFrameworkIntegrationTypes.Contains(typeName);
	}

	private static bool IsApprovedAttributeMetadataType(Type type, MemberInfo? declaringMember)
	{
		return type == typeof(Type) && declaringMember?.DeclaringType is { } declaringType &&
			   typeof(Attribute).IsAssignableFrom(declaringType);
	}

	/// <summary>
	///     Scalars that can never disguise a native handle. <see cref="IntPtr" /> and <see cref="UIntPtr" /> (<c>nint</c>,
	///     <c>nuint</c>) are primitive to the runtime but are rejected unless explicitly allowlisted (A11-29).
	/// </summary>
	private static bool IsScalar(Type type)
	{
		return (type.IsPrimitive && !IsNativeSized(type)) || type == typeof(void) || type == typeof(string);
	}

	private static bool IsNativeSized(Type type)
	{
		return type == typeof(IntPtr) || type == typeof(UIntPtr);
	}

	private static bool IsTuple(Type type)
	{
		return type.FullName?.StartsWith("System.ValueTuple", StringComparison.Ordinal) == true;
	}

	private static bool IsFrameworkType(Type type)
	{
		string? assemblyName = type.Assembly.GetName().Name;
		return assemblyName is not null &&
			   (assemblyName.StartsWith("System", StringComparison.Ordinal) ||
				assemblyName.StartsWith("Microsoft", StringComparison.Ordinal));
	}

	private static bool ShouldInspectTypeMembers(Type type)
	{
		return type.Assembly.IsDynamic || type.Assembly == typeof(PublicClientSignatureBoundaryTests).Assembly ||
			   (type.IsValueType &&
				type.Assembly.GetName().Name?.StartsWith("CheatEngine.Client", StringComparison.Ordinal) == true);
	}

	private static void VerifyTypeHierarchy(Type type, string? source, List<string> violations,
		MemberInfo? declaringMember, HashSet<Type> visited, ref int visitedCount, int depth)
	{
		if (type.BaseType is { } baseType && baseType != typeof(object) && baseType != typeof(ValueType) &&
			baseType != typeof(Enum) && !IsRequiredPluginBase(baseType))
		{
			VerifyType(baseType, $"{source} base type", violations, declaringMember, visited, ref visitedCount,
				depth + 1);
		}

		foreach (Type implementedInterface in type.GetInterfaces().OrderBy(static candidate => candidate.FullName,
					 StringComparer.Ordinal))
		{
			if (IsSafeFrameworkDtoImplementationContract(implementedInterface))
			{
				continue;
			}

			VerifyType(implementedInterface, $"{source} interface", violations, declaringMember, visited,
				ref visitedCount, depth + 1);
		}
	}

	private static bool IsRequiredPluginBase(Type type)
	{
		// Hosting intentionally derives from the SDK plugin bootstrap contract; it is not an SDK owner or raw handle.
		return type.FullName == "CheatEngine.SDK.Hosting.Plugin.CheatEnginePlugin";
	}

	private static bool IsSafeFrameworkDtoImplementationContract(Type type)
	{
		Type definition = type.IsGenericType ? type.GetGenericTypeDefinition() : type;
		return definition.FullName is "System.IAsyncDisposable" or "System.IComparable" or "System.IComparable`1" or
			"System.IConvertible" or "System.IDisposable" or "System.IEquatable`1" or "System.IFormattable" or
			"System.ISpanFormattable" or "System.IUtf8SpanFormattable";
	}

	private static void VerifyTypeMembers(Type type, string? source, List<string> violations, HashSet<Type> visited,
		ref int visitedCount, int depth)
	{
		const BindingFlags DeclaredInstance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic |
											  BindingFlags.DeclaredOnly;

		foreach (FieldInfo field in type.GetFields(DeclaredInstance).OrderBy(static candidate => candidate.Name,
					 StringComparer.Ordinal))
		{
			VerifyType(field.FieldType, $"{source} field '{field.Name}'", violations, field, visited,
				ref visitedCount, depth + 1);
		}

		foreach (PropertyInfo property in type.GetProperties(DeclaredInstance).OrderBy(
					 static candidate => candidate.Name,
					 StringComparer.Ordinal))
		{
			VerifyType(property.PropertyType, $"{source} property '{property.Name}'", violations, property, visited,
				ref visitedCount, depth + 1);
			VerifyParameters(property.GetIndexParameters(), property, violations, visited, ref visitedCount, depth);
		}

		foreach (ConstructorInfo constructor in type.GetConstructors(DeclaredInstance).OrderBy(static candidate =>
					 candidate.ToString(), StringComparer.Ordinal))
		{
			VerifyParameters(constructor.GetParameters(), constructor, violations, visited, ref visitedCount, depth);
		}

		foreach (MethodInfo method in type.GetMethods(DeclaredInstance)
					 .Where(static candidate => !candidate.IsPrivate)
					 .OrderBy(static candidate => candidate.ToString(), StringComparer.Ordinal))
		{
			if (IsObjectEqualityMethod(method))
			{
				continue;
			}

			VerifyType(method.ReturnType, $"{source} method '{method.Name}'", violations, method, visited,
				ref visitedCount, depth + 1);
			VerifyParameters(method.GetParameters(), method, violations, visited, ref visitedCount, depth);
			VerifyGenericParameterConstraints(method.GetGenericArguments(), method.ToString(), violations, visited,
				ref visitedCount, depth);
		}
	}

	private static bool IsObjectEqualityMethod(MethodInfo method)
	{
		return method.Name == nameof(object.Equals) && !method.IsStatic && method.ReturnType == typeof(bool) &&
			   method.GetParameters() is [ParameterInfo { ParameterType: var type }] && type == typeof(object);
	}

	/// <summary>No public Client member may expose <c>nint</c>/<c>nuint</c>; add a reviewed, named exception here if one ever must.</summary>
	private static bool IsApprovedNativeSizedMember(MemberInfo? declaringMember)
	{
		_ = declaringMember;
		return false;
	}

	private static void AssertViolation(Type type, string expectedFragment)
	{
		List<string> violations = [];
		VerifyType(type, type.FullName, violations);

		Assert.Contains(violations, violation => violation.Contains(expectedFragment, StringComparison.Ordinal));
	}

	private static unsafe delegate* unmanaged[Cdecl]<int, int> GetFunctionPointer()
	{
		return null;
	}

	private delegate void UnsafeCallback(LuaRef reference);

	private interface IUnsafeConstraint
	{
		public LuaRef Reference
		{
			get;
		}
	}

	private sealed class ConstrainedDto<T>
		where T : IUnsafeConstraint;

	private sealed class LeakingDto : IDisposable
	{
		private readonly LuaRef _reference = new();

		public void Dispose()
		{
			_reference.Dispose();
		}

		private LuaRef PreserveReference()
		{
			return _reference;
		}
	}
}
