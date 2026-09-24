using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace CheatEngine.Client.Tests.Architecture;

/// <summary>
///     Reads type and member references of a compiled assembly as stable text, and attributes the references found in
///     method bodies to their outermost declaring type.
/// </summary>
/// <remarks>
///     Everything is read with <see cref="System.Reflection.Metadata" />: no Client or SDK code executes. Generic
///     instantiations are reduced to their definitions, so the text of a reference does not depend on the type arguments
///     a call site used. See https://learn.microsoft.com/dotnet/api/system.reflection.metadata.methodbodyblock.getilreader.
/// </remarks>
internal static class MetadataSurface
{
	internal const string SdkAssemblyPrefix = "CheatEngine.SDK";

	private static readonly Dictionary<ushort, OperandType> OperandTypes = BuildOperandTypes();

	/// <summary>Gets whether an assembly simple name belongs to the consumed CheatEngine.SDK package.</summary>
	internal static bool IsSdkAssembly(string assemblyName)
	{
		return assemblyName.StartsWith(SdkAssemblyPrefix, StringComparison.Ordinal);
	}

	/// <summary>Resolves a type handle to its assembly and full name, reducing a generic instantiation to its definition.</summary>
	internal static TypeIdentity ResolveType(MetadataReader reader, EntityHandle handle)
	{
		return handle.Kind switch
		{
			HandleKind.TypeReference => ResolveTypeReference(reader, (TypeReferenceHandle) handle),
			HandleKind.TypeDefinition => ResolveTypeDefinition(reader, (TypeDefinitionHandle) handle),
			HandleKind.TypeSpecification => reader.GetTypeSpecification((TypeSpecificationHandle) handle)
				.DecodeSignature(new SignatureNameProvider(), null).Identity ?? TypeIdentity.Unresolved,
			_ => TypeIdentity.Unresolved
		};
	}

	/// <summary>Describes a member reference as <c>DeclaringType::Name(parameters)-&gt;return</c>.</summary>
	internal static string DescribeMember(MetadataReader reader, MemberReferenceHandle handle,
		out TypeIdentity declaringType)
	{
		MemberReference member = reader.GetMemberReference(handle);
		declaringType = member.Parent.Kind switch
		{
			HandleKind.MethodDefinition => ResolveTypeDefinition(reader,
				reader.GetMethodDefinition((MethodDefinitionHandle) member.Parent).GetDeclaringType()),
			HandleKind.ModuleReference => TypeIdentity.Unresolved,
			_ => ResolveType(reader, member.Parent)
		};
		string name = reader.GetString(member.Name);
		SignatureNameProvider provider = new();
		if (member.GetKind() == MemberReferenceKind.Field)
		{
			SignatureName fieldType = member.DecodeFieldSignature(provider, null);
			return $"{declaringType.FullName}::{name}:{fieldType.Display}";
		}

		MethodSignature<SignatureName> signature = member.DecodeMethodSignature(provider, null);
		string generic = signature.GenericParameterCount == 0 ? string.Empty : $"``{signature.GenericParameterCount}";
		string parameters = string.Join(",", signature.ParameterTypes.Select(static parameter => parameter.Display));
		return $"{declaringType.FullName}::{name}{generic}({parameters})->{signature.ReturnType.Display}";
	}

	/// <summary>
	///     Describes a method definition with the text <see cref="DescribeMember" /> gives a reference to it, so that a
	///     definition read from an SDK assembly and a reference read from a Client assembly compare as equal strings.
	/// </summary>
	internal static string DescribeMethodDefinition(MetadataReader reader, MethodDefinitionHandle handle)
	{
		MethodDefinition method = reader.GetMethodDefinition(handle);
		TypeIdentity declaringType = ResolveTypeDefinition(reader, method.GetDeclaringType());
		MethodSignature<SignatureName> signature = method.DecodeSignature(new SignatureNameProvider(), null);
		string generic = signature.GenericParameterCount == 0 ? string.Empty : $"``{signature.GenericParameterCount}";
		string parameters = string.Join(",", signature.ParameterTypes.Select(static parameter => parameter.Display));
		string name = reader.GetString(method.Name);
		return $"{declaringType.FullName}::{name}{generic}({parameters})->{signature.ReturnType.Display}";
	}

	/// <summary>Describes a field definition with the text <see cref="DescribeMember" /> gives a reference to it.</summary>
	internal static string DescribeFieldDefinition(MetadataReader reader, FieldDefinitionHandle handle)
	{
		FieldDefinition field = reader.GetFieldDefinition(handle);
		TypeIdentity declaringType = ResolveTypeDefinition(reader, field.GetDeclaringType());
		SignatureName fieldType = field.DecodeSignature(new SignatureNameProvider(), null);
		return $"{declaringType.FullName}::{reader.GetString(field.Name)}:{fieldType.Display}";
	}

	/// <summary>Gets the full name of the type of a custom attribute.</summary>
	internal static string GetAttributeTypeName(MetadataReader reader, CustomAttribute attribute)
	{
		return attribute.Constructor.Kind switch
		{
			HandleKind.MemberReference => ResolveType(reader,
				reader.GetMemberReference((MemberReferenceHandle) attribute.Constructor).Parent).FullName,
			HandleKind.MethodDefinition => ResolveTypeDefinition(reader,
				reader.GetMethodDefinition((MethodDefinitionHandle) attribute.Constructor).GetDeclaringType()).FullName,
			_ => string.Empty
		};
	}

	/// <summary>Enumerates the metadata tokens referenced by every method body, with the outermost declaring type.</summary>
	/// <remarks>Closures, lambdas, local functions, and state machines are nested types; they fold into their container.</remarks>
	internal static IEnumerable<IlReference> ReadIlReferences(MetadataReader reader, PEReader peReader)
	{
		foreach (MethodDefinitionHandle methodHandle in reader.MethodDefinitions)
		{
			MethodDefinition method = reader.GetMethodDefinition(methodHandle);
			if (method.RelativeVirtualAddress == 0)
			{
				continue;
			}

			string outerType = GetOutermostTypeName(reader, method.GetDeclaringType());
			string methodName = reader.GetString(method.Name);
			MethodBodyBlock body = peReader.GetMethodBody(method.RelativeVirtualAddress);
			foreach (EntityHandle token in ReadTokens(body.GetILReader()))
			{
				yield return new IlReference(outerType, methodName, token);
			}
		}
	}

	/// <summary>Gets the full name of the outermost type that contains <paramref name="handle" />.</summary>
	internal static string GetOutermostTypeName(MetadataReader reader, TypeDefinitionHandle handle)
	{
		TypeDefinitionHandle current = handle;
		while (true)
		{
			TypeDefinitionHandle declaring = reader.GetTypeDefinition(current).GetDeclaringType();
			if (declaring.IsNil)
			{
				return ResolveTypeDefinition(reader, current).FullName;
			}

			current = declaring;
		}
	}

	/// <summary>Resolves a method-body token to the member reference it names, if any.</summary>
	internal static MemberReferenceHandle? AsMemberReference(MetadataReader reader, EntityHandle token)
	{
		return token.Kind switch
		{
			HandleKind.MemberReference => (MemberReferenceHandle) token,
			HandleKind.MethodSpecification when reader.GetMethodSpecification((MethodSpecificationHandle) token).Method
				is { Kind: HandleKind.MemberReference } method => (MemberReferenceHandle) method,
			_ => null
		};
	}

	internal static TypeIdentity ResolveTypeDefinition(MetadataReader reader, TypeDefinitionHandle handle)
	{
		TypeDefinition definition = reader.GetTypeDefinition(handle);
		string name = reader.GetString(definition.Name);
		TypeDefinitionHandle declaring = definition.GetDeclaringType();
		string assembly = reader.GetString(reader.GetAssemblyDefinition().Name);
		if (!declaring.IsNil)
		{
			return new TypeIdentity(assembly, $"{ResolveTypeDefinition(reader, declaring).FullName}+{name}");
		}

		string ns = reader.GetString(definition.Namespace);
		return new TypeIdentity(assembly, ns.Length == 0 ? name : $"{ns}.{name}");
	}

	private static TypeIdentity ResolveTypeReference(MetadataReader reader, TypeReferenceHandle handle)
	{
		TypeReference reference = reader.GetTypeReference(handle);
		string name = reader.GetString(reference.Name);
		EntityHandle scope = reference.ResolutionScope;
		switch (scope.Kind)
		{
			case HandleKind.TypeReference:
				{
					TypeIdentity outer = ResolveTypeReference(reader, (TypeReferenceHandle) scope);
					return new TypeIdentity(outer.Assembly, $"{outer.FullName}+{name}");
				}
			case HandleKind.AssemblyReference:
				{
					string assembly = reader.GetString(reader.GetAssemblyReference((AssemblyReferenceHandle) scope).Name);
					string ns = reader.GetString(reference.Namespace);
					return new TypeIdentity(assembly, ns.Length == 0 ? name : $"{ns}.{name}");
				}
			default:
				{
					string ns = reader.GetString(reference.Namespace);
					string assembly = reader.GetString(reader.GetAssemblyDefinition().Name);
					return new TypeIdentity(assembly, ns.Length == 0 ? name : $"{ns}.{name}");
				}
		}
	}

	private static List<EntityHandle> ReadTokens(BlobReader il)
	{
		List<EntityHandle> tokens = [];
		while (il.RemainingBytes > 0)
		{
			ushort code = il.ReadByte();
			if (code == 0xFE)
			{
				code = (ushort) (0xFE00 | il.ReadByte());
			}

			switch (OperandTypes[code])
			{
				case OperandType.InlineNone:
					break;
				case OperandType.ShortInlineBrTarget:
				case OperandType.ShortInlineI:
				case OperandType.ShortInlineVar:
					il.ReadByte();
					break;
				case OperandType.InlineVar:
					il.ReadInt16();
					break;
				case OperandType.InlineI:
				case OperandType.InlineBrTarget:
				case OperandType.ShortInlineR:
				case OperandType.InlineString:
				case OperandType.InlineSig:
					il.ReadInt32();
					break;
				case OperandType.InlineI8:
				case OperandType.InlineR:
					il.ReadInt64();
					break;
				case OperandType.InlineSwitch:
					int targets = il.ReadInt32();
					il.Offset += targets * sizeof(int);
					break;
				case OperandType.InlineField:
				case OperandType.InlineMethod:
				case OperandType.InlineTok:
				case OperandType.InlineType:
					tokens.Add(MetadataTokens.EntityHandle(il.ReadInt32()));
					break;
				default:
					throw new InvalidOperationException($"Unsupported IL operand type for opcode 0x{code:X4}.");
			}
		}

		return tokens;
	}

	private static Dictionary<ushort, OperandType> BuildOperandTypes()
	{
		Dictionary<ushort, OperandType> result = [];
		foreach (FieldInfo field in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
		{
			if (field.GetValue(null) is OpCode opcode)
			{
				result[unchecked((ushort) opcode.Value)] = opcode.OperandType;
			}
		}

		return result;
	}

	/// <summary>A type identified by the simple name of its defining assembly and its full metadata name.</summary>
	internal readonly record struct TypeIdentity(string Assembly, string FullName)
	{
		internal static TypeIdentity Unresolved => new(string.Empty, "<unresolved>");

		internal bool IsSdk => IsSdkAssembly(Assembly);
	}

	/// <summary>One metadata token referenced by a method body.</summary>
	internal readonly record struct IlReference(string OuterType, string Method, EntityHandle Token);

	/// <summary>The display text of a decoded signature type and, for named types, their identity.</summary>
	internal sealed record SignatureName(string Display, TypeIdentity? Identity);

	internal sealed class SignatureNameProvider : ISignatureTypeProvider<SignatureName, object?>
	{
		public SignatureName GetArrayType(SignatureName elementType, ArrayShape shape)
		{
			return new SignatureName($"{elementType.Display}[{new string(',', shape.Rank - 1)}]", null);
		}

		public SignatureName GetByReferenceType(SignatureName elementType)
		{
			return new SignatureName(elementType.Display + "&", null);
		}

		public SignatureName GetFunctionPointerType(MethodSignature<SignatureName> signature)
		{
			string parameters = string.Join(",", signature.ParameterTypes.Select(static parameter => parameter.Display));
			return new SignatureName($"fnptr({parameters})->{signature.ReturnType.Display}", null);
		}

		public SignatureName GetGenericInstantiation(SignatureName genericType, ImmutableArray<SignatureName> typeArguments)
		{
			string arguments = string.Join(",", typeArguments.Select(static argument => argument.Display));
			return new SignatureName($"{genericType.Display}<{arguments}>", genericType.Identity);
		}

		public SignatureName GetGenericMethodParameter(object? genericContext, int index)
		{
			return new SignatureName($"!!{index}", null);
		}

		public SignatureName GetGenericTypeParameter(object? genericContext, int index)
		{
			return new SignatureName($"!{index}", null);
		}

		public SignatureName GetModifiedType(SignatureName modifier, SignatureName unmodifiedType, bool isRequired)
		{
			return unmodifiedType;
		}

		public SignatureName GetPinnedType(SignatureName elementType)
		{
			return elementType;
		}

		public SignatureName GetPointerType(SignatureName elementType)
		{
			return new SignatureName(elementType.Display + "*", null);
		}

		public SignatureName GetPrimitiveType(PrimitiveTypeCode typeCode)
		{
			return new SignatureName(typeCode.ToString().ToLowerInvariant(), null);
		}

		public SignatureName GetSZArrayType(SignatureName elementType)
		{
			return new SignatureName(elementType.Display + "[]", null);
		}

		public SignatureName GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind)
		{
			TypeIdentity identity = ResolveTypeDefinition(reader, handle);
			return new SignatureName(identity.FullName, identity);
		}

		public SignatureName GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind)
		{
			TypeIdentity identity = ResolveTypeReference(reader, handle);
			return new SignatureName(identity.FullName, identity);
		}

		public SignatureName GetTypeFromSpecification(MetadataReader reader, object? genericContext,
			TypeSpecificationHandle handle, byte rawTypeKind)
		{
			return reader.GetTypeSpecification(handle).DecodeSignature(this, genericContext);
		}
	}
}
