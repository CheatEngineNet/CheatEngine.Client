using System.Reflection;

using CheatEngine.Client.Extensions.DependencyInjection;
using CheatEngine.Client.Hosting;
using CheatEngine.Client.Memory;
using CheatEngine.SDK.Engine.Objects;
using CheatEngine.SDK.Lua.References;
using CheatEngine.SDK.Lua.State;

using ReflectionAssembly = System.Reflection.Assembly;

namespace CheatEngine.Client.Tests;

/// <summary>Protects the aggregate package boundary from raw SDK lifetime and Lua implementation types.</summary>
public sealed class PublicClientSignatureBoundaryTests
{
	private static readonly HashSet<string> ApprovedSdkValueTypes = new(StringComparer.Ordinal)
	{
		"CheatEngine.SDK.Engine.AddressList.MemoryRecordId",
		"CheatEngine.SDK.Engine.Enums.FastScanMethod",
		"CheatEngine.SDK.Engine.Enums.VariableType",
		"CheatEngine.SDK.Engine.Inspection.AddressResolutionOptions",
		"CheatEngine.SDK.Engine.Inspection.MemoryRegionInfo",
		"CheatEngine.SDK.Engine.Inspection.ModuleInfo",
		"CheatEngine.SDK.Engine.Inspection.ModuleName",
		"CheatEngine.SDK.Engine.Inspection.ModuleSectionInfo",
		"CheatEngine.SDK.Engine.Inspection.SymbolInfo",
		"CheatEngine.SDK.Engine.Inspection.SymbolExpression",
		"CheatEngine.SDK.Engine.Inspection.TargetProcessId",
		"CheatEngine.SDK.Engine.Runtime.CheatEngineArchitecture",
		"CheatEngine.SDK.Engine.Runtime.CheatEngineVersion",
		"CheatEngine.SDK.Engine.Runtime.PointerSize",
		"CheatEngine.SDK.Engine.Runtime.RuntimeCapabilityAvailability",
		"CheatEngine.SDK.Engine.Runtime.RuntimeCapabilityId",
		"CheatEngine.SDK.Engine.Runtime.TargetAbi",
		"CheatEngine.SDK.Engine.Scanning.Aob.AobScanOptions",
		"CheatEngine.SDK.Engine.Scanning.Aob.AobPattern",
		"CheatEngine.SDK.Engine.Scanning.Values.FirstScanRequest",
		"CheatEngine.SDK.Engine.Scanning.Values.NextScanRequest",
		"CheatEngine.SDK.Engine.Values.Address"
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
	public void RecursiveVerifierAllowsApprovedSdkValuesInsideSafeContainers()
	{
		List<string> violations = [];
		VerifyType(typeof(CheatEngine.SDK.Engine.Values.Address[]), "approved array", violations);
		VerifyType(typeof(IReadOnlyList<CheatEngine.SDK.Engine.Values.Address>), "approved generic", violations);
		VerifyType(typeof((CheatEngine.SDK.Engine.Values.Address Address, int Version)), "approved tuple", violations);

		Assert.Empty(violations);
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

		foreach (ConstructorInfo constructor in publicType.GetConstructors(PublicDeclared))
		{
			VerifyParameters(constructor.GetParameters(), constructor, violations);
		}

		foreach (MethodInfo method in publicType.GetMethods(PublicDeclared))
		{
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
		VerifyParameters(parameters, member, violations, [], 0);
	}

	private static void VerifyParameters(IEnumerable<ParameterInfo> parameters, MemberInfo member,
		List<string> violations, HashSet<Type> visited, int depth)
	{
		foreach (ParameterInfo parameter in parameters)
		{
			VerifyType(parameter.ParameterType, $"{member} parameter '{parameter.Name}'", violations, member, visited,
				depth + 1);
		}
	}

	private static void VerifyGenericParameterConstraints(IEnumerable<Type> genericParameters, string? member,
		List<string> violations)
	{
		VerifyGenericParameterConstraints(genericParameters, member, violations, [], 0);
	}

	private static void VerifyGenericParameterConstraints(IEnumerable<Type> genericParameters, string? member,
		List<string> violations, HashSet<Type> visited, int depth)
	{
		foreach (Type genericParameter in genericParameters.Where(static parameter => parameter.IsGenericParameter))
		{
			foreach (Type constraint in genericParameter.GetGenericParameterConstraints()
				         .OrderBy(static type => type.FullName, StringComparer.Ordinal))
			{
				VerifyType(constraint, $"{member} generic parameter '{genericParameter.Name}'", violations, null, visited,
					depth + 1);
			}
		}
	}

	private static void VerifyType(Type? type, string? source, List<string> violations,
		MemberInfo? declaringMember = null)
	{
		HashSet<Type> visited = [];
		VerifyType(type, source, violations, declaringMember, visited, 0);
	}

	private static void VerifyType(Type? type, string? source, List<string> violations, MemberInfo? declaringMember,
		HashSet<Type> visited, int depth)
	{
		if (type is null || depth > 32 || !visited.Add(type))
		{
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
			VerifyType(type.GetElementType(), source, violations, declaringMember, visited, depth + 1);
			return;
		}

		if (type.IsArray)
		{
			VerifyType(type.GetElementType(), source, violations, declaringMember, visited, depth + 1);
			return;
		}

		if (type.IsGenericParameter)
		{
			VerifyGenericParameterConstraints(type.GetGenericParameterConstraints(), source, violations, visited, depth);
			return;
		}

		if (IsForbiddenSdkType(type, source, violations, declaringMember))
		{
			return;
		}

		if (type.IsGenericType)
		{
			Type genericDefinition = type.GetGenericTypeDefinition();
			VerifyGenericParameterConstraints(genericDefinition.GetGenericArguments(), source, violations, visited, depth);
			foreach (Type argument in type.GetGenericArguments())
			{
				VerifyType(argument, source, violations, declaringMember, visited, depth + 1);
			}
		}

		if (typeof(Delegate).IsAssignableFrom(type))
		{
			MethodInfo? invoke = type.GetMethod("Invoke", BindingFlags.Public | BindingFlags.Instance);
			if (invoke is not null)
			{
				VerifyType(invoke.ReturnType, $"{source} delegate return", violations, invoke, visited, depth + 1);
				VerifyParameters(invoke.GetParameters(), invoke, violations, visited, depth);
			}

			return;
		}

		if (!ShouldInspectTypeMembers(type))
		{
			return;
		}

		VerifyTypeHierarchy(type, source, violations, declaringMember, visited, depth);
		VerifyTypeMembers(type, source, violations, visited, depth);
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
		    (!definition.IsValueType || !ApprovedSdkValueTypes.Contains(typeName)) &&
		    !IsShippedRuntimeCapabilitiesDebt(definition, declaringMember))
		{
			violations.Add($"{source} exposes non-approved SDK type '{typeName}'.");
			return true;
		}

		return false;
	}

	private static bool ShouldInspectTypeMembers(Type type)
	{
		return type.Assembly == typeof(PublicClientSignatureBoundaryTests).Assembly ||
		       type.Assembly.GetName().Name?.StartsWith("CheatEngine.Client", StringComparison.Ordinal) == true;
	}

	private static void VerifyTypeHierarchy(Type type, string? source, List<string> violations,
		MemberInfo? declaringMember, HashSet<Type> visited, int depth)
	{
		if (type.BaseType is { } baseType && baseType != typeof(object) && !IsRequiredPluginBase(baseType))
		{
			VerifyType(baseType, $"{source} base type", violations, declaringMember, visited, depth + 1);
		}

		foreach (Type implementedInterface in type.GetInterfaces().OrderBy(static candidate => candidate.FullName,
			             StringComparer.Ordinal))
		{
			VerifyType(implementedInterface, $"{source} interface", violations, declaringMember, visited, depth + 1);
		}
	}

	private static bool IsRequiredPluginBase(Type type)
	{
		// Hosting intentionally derives from the SDK plugin bootstrap contract; it is not an SDK owner or raw handle.
		return type.FullName == "CheatEngine.SDK.Hosting.Plugin.CheatEnginePlugin";
	}

	private static void VerifyTypeMembers(Type type, string? source, List<string> violations, HashSet<Type> visited,
		int depth)
	{
		const BindingFlags DeclaredInstance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic |
		                                          BindingFlags.DeclaredOnly;

		foreach (FieldInfo field in type.GetFields(DeclaredInstance).OrderBy(static candidate => candidate.Name,
			             StringComparer.Ordinal))
		{
			VerifyType(field.FieldType, $"{source} field '{field.Name}'", violations, field, visited, depth + 1);
		}

		foreach (PropertyInfo property in type.GetProperties(DeclaredInstance).OrderBy(static candidate => candidate.Name,
			             StringComparer.Ordinal))
		{
			VerifyType(property.PropertyType, $"{source} property '{property.Name}'", violations, property, visited,
				depth + 1);
			VerifyParameters(property.GetIndexParameters(), property, violations, visited, depth);
		}

		foreach (ConstructorInfo constructor in type.GetConstructors(DeclaredInstance).OrderBy(static candidate =>
			             candidate.ToString(), StringComparer.Ordinal))
		{
			VerifyParameters(constructor.GetParameters(), constructor, violations, visited, depth);
		}

		foreach (MethodInfo method in type.GetMethods(DeclaredInstance)
			             .Where(static candidate => !candidate.IsPrivate)
			             .OrderBy(static candidate => candidate.ToString(), StringComparer.Ordinal))
		{
			VerifyType(method.ReturnType, $"{source} method '{method.Name}'", violations, method, visited, depth + 1);
			VerifyParameters(method.GetParameters(), method, violations, visited, depth);
			VerifyGenericParameterConstraints(method.GetGenericArguments(), method.ToString(), violations, visited, depth);
		}
	}

	private static bool IsShippedRuntimeCapabilitiesDebt(Type type, MemberInfo? declaringMember)
	{
		// PublicAPI.Shipped preserves this one legacy class reference. It is deliberately a member-level exception:
		// every other SDK reference type remains prohibited by this recursive boundary test.
		return type.FullName == "CheatEngine.SDK.Engine.Runtime.RuntimeCapabilities" &&
		       declaringMember?.DeclaringType?.FullName == "CheatEngine.Client.Runtime.CheatEngineRuntimeSnapshot" &&
		       declaringMember switch
		       {
		       FieldInfo { Name: "<SdkCapabilities>k__BackingField" } => true,
		       ConstructorInfo => true,
			       MethodInfo { Name: "get_SdkCapabilities" } => true,
			       PropertyInfo { Name: "SdkCapabilities" } => true,
			       _ => false
		       };
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
		LuaRef Reference
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
