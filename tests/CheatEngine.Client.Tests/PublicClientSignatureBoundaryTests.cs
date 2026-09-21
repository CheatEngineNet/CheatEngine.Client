using System.Reflection;

using CheatEngine.Client.Extensions.DependencyInjection;
using CheatEngine.Client.Hosting;
using CheatEngine.Client.Memory;

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
		foreach (ParameterInfo parameter in parameters)
		{
			VerifyType(parameter.ParameterType, $"{member} parameter '{parameter.Name}'", violations, member);
		}
	}

	private static void VerifyGenericParameterConstraints(IEnumerable<Type> genericParameters, string? member,
		List<string> violations)
	{
		foreach (Type genericParameter in genericParameters.Where(static parameter => parameter.IsGenericParameter))
		{
			foreach (Type constraint in genericParameter.GetGenericParameterConstraints())
			{
				VerifyType(constraint, $"{member} generic parameter '{genericParameter.Name}'", violations);
			}
		}
	}

	private static void VerifyType(Type? type, string? source, List<string> violations,
		MemberInfo? declaringMember = null)
	{
		if (type is null || type.IsGenericParameter)
		{
			return;
		}

		if (type.IsPointer)
		{
			violations.Add($"{source} exposes a pointer type '{type}'.");
			return;
		}

		if (type.HasElementType)
		{
			VerifyType(type.GetElementType(), source, violations, declaringMember);
			return;
		}

		if (type.IsGenericType)
		{
			Type genericDefinition = type.GetGenericTypeDefinition();
			if (genericDefinition != type)
			{
				VerifyType(genericDefinition, source, violations, declaringMember);
			}

			foreach (Type argument in type.GetGenericArguments())
			{
				VerifyType(argument, source, violations, declaringMember);
			}

			return;
		}

		string typeName = type.FullName ?? type.Name;
		if (type.Name is "LuaState" or "LuaRef" or "CEObject" ||
		    type.Name.StartsWith("Owned`", StringComparison.Ordinal))
		{
			violations.Add($"{source} exposes forbidden SDK handle '{typeName}'.");
			return;
		}

		if (type.Namespace?.Contains(".Interop", StringComparison.Ordinal) == true)
		{
			violations.Add($"{source} exposes interop namespace type '{typeName}'.");
			return;
		}

		if (type.Assembly.GetName().Name?.StartsWith("CheatEngine.SDK", StringComparison.Ordinal) == true &&
		    (!type.IsValueType || !ApprovedSdkValueTypes.Contains(typeName)) &&
		    !IsShippedRuntimeCapabilitiesDebt(type, declaringMember))
		{
			violations.Add($"{source} exposes non-approved SDK type '{typeName}'.");
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
			       ConstructorInfo => true,
			       MethodInfo { Name: "get_SdkCapabilities" } => true,
			       PropertyInfo { Name: "SdkCapabilities" } => true,
			       _ => false
		       };
	}
}
