using System.Collections.Immutable;
using System.Reflection;

using CheatEngine.Client.Runtime;
using CheatEngine.Client.Scanning;
using CheatEngine.Client.Tables;

using ReflectionAssembly = System.Reflection.Assembly;

namespace CheatEngine.Client.Abstractions.Tests;

/// <summary>
///     Every Client value type that a public member returns or a <c>Try</c> method publishes is safe to read at
///     <see langword="default" />: a failed <c>Try</c> leaves its output <see langword="default" />, so every property of
///     that value must still be readable, a non-nullable reference property is never <see langword="null" />, and an
///     <see cref="ImmutableArray{T}" /> property is never the default array.
/// </summary>
/// <remarks>
///     The value types are collected by reflection: the return types and <c>out</c> parameters of every public member of
///     every public interface and class of the Abstractions assembly, then the Client value types their properties
///     expose, recursively. A generic value type is closed over <see cref="int" />.
/// </remarks>
public sealed class DefaultOutputValueTests
{
	private const string ClientNamespace = "CheatEngine.Client";

	private static readonly ReflectionAssembly Abstractions = typeof(ICheatEngineClient).Assembly;

	[Fact]
	public void EveryPublishedClientValueIsSafeToReadAtItsDefault()
	{
		HashSet<Type> published = CollectPublishedValueTypes();
		NullabilityInfoContext nullability = new();
		List<string> offenders = [];
		foreach (Type type in published)
		{
			object value = Activator.CreateInstance(type)!;
			foreach (PropertyInfo property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
			{
				if (property.GetIndexParameters().Length != 0)
				{
					continue;
				}

				object? read;
				try
				{
					read = property.GetValue(value);
				}
				catch (TargetInvocationException exception)
				{
					offenders.Add($"{Describe(type)}.{property.Name} throws {exception.InnerException?.GetType().Name}");
					continue;
				}

				if (IsImmutableArray(property.PropertyType))
				{
					if ((bool) property.PropertyType.GetProperty(nameof(ImmutableArray<>.IsDefault))!.GetValue(read)!)
					{
						offenders.Add($"{Describe(type)}.{property.Name} is a default ImmutableArray");
					}
				}
				else if (!property.PropertyType.IsValueType && read is null &&
						 nullability.Create(property).ReadState == NullabilityState.NotNull)
				{
					offenders.Add($"{Describe(type)}.{property.Name} is null although it is declared non-nullable");
				}
			}
		}

		// The collection cannot pass vacuously: the outputs of the main Try forms are among the checked types.
		Assert.Contains(typeof(AobScanResult), published);
		Assert.Contains(typeof(CheatEngineRuntimeSnapshot), published);
		Assert.Contains(typeof(MemoryRecordContentSnapshot), published);
		Assert.True(offenders.Count == 0,
			"A Client output value must be safe to read at its default (empty text, empty arrays):" +
			Environment.NewLine + string.Join(Environment.NewLine, offenders));
	}

	/// <summary>Collects the Client value types that public members return or publish, with those they expose.</summary>
	private static HashSet<Type> CollectPublishedValueTypes()
	{
		HashSet<Type> found = [];
		Queue<Type> pending = new();
		foreach (Type type in Abstractions.GetExportedTypes().Where(static type => type.IsInterface || type.IsClass))
		{
			foreach (MemberInfo member in type.GetMembers(BindingFlags.Public | BindingFlags.Instance |
														  BindingFlags.Static | BindingFlags.DeclaredOnly))
			{
				switch (member)
				{
					case PropertyInfo property:
						Enqueue(property.PropertyType);
						break;
					case MethodInfo method when !method.IsSpecialName:
						Enqueue(method.ReturnType);
						foreach (ParameterInfo parameter in method.GetParameters().Where(static parameter => parameter.IsOut))
						{
							Enqueue(parameter.ParameterType.GetElementType()!);
						}

						break;
				}
			}
		}

		while (pending.TryDequeue(out Type? type))
		{
			foreach (PropertyInfo property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
			{
				Enqueue(property.PropertyType);
			}
		}

		return found;

		void Enqueue(Type candidate)
		{
			Type type = Nullable.GetUnderlyingType(candidate) ?? candidate;
			if (IsImmutableArray(type))
			{
				Enqueue(type.GetGenericArguments()[0]);
				return;
			}

			if (!type.IsValueType || type.IsEnum || type.IsGenericParameter)
			{
				return;
			}

			if (type.ContainsGenericParameters)
			{
				if (!type.IsGenericType)
				{
					return;
				}

				type = type.GetGenericTypeDefinition().MakeGenericType(
					[.. type.GetGenericArguments().Select(static _ => typeof(int))]);
			}

			if (type.Namespace?.StartsWith(ClientNamespace, StringComparison.Ordinal) == true &&
				type.Assembly == Abstractions && found.Add(type))
			{
				pending.Enqueue(type);
			}
		}
	}

	private static bool IsImmutableArray(Type type)
	{
		return type.IsGenericType && type.GetGenericTypeDefinition() == typeof(ImmutableArray<>);
	}

	private static string Describe(Type type)
	{
		return type.IsGenericType ? type.GetGenericTypeDefinition().Name + "<int>" : type.Name;
	}
}
