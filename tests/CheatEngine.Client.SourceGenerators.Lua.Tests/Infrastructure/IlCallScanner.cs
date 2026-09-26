using System.Collections.Frozen;
using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace CheatEngine.Client.SourceGenerators.Lua.Tests.Infrastructure;

/// <summary>Lists the methods an emitted assembly's IL calls, read with System.Reflection.Metadata (no code runs).</summary>
internal static class IlCallScanner
{
	private static readonly FrozenDictionary<short, OpCode> OpCodesByValue = typeof(OpCodes)
		.GetFields(BindingFlags.Public | BindingFlags.Static)
		.Select(static field => (OpCode) field.GetValue(null)!)
		.ToFrozenDictionary(static opCode => opCode.Value);

	/// <summary>Returns the simple names of every method definition or member reference invoked by any method body.</summary>
	public static IReadOnlyList<string> CalledMethodNames(byte[] image)
	{
		using PEReader reader = new(new MemoryStream(image));
		MetadataReader metadata = reader.GetMetadataReader();
		List<string> names = [];
		foreach (MethodDefinitionHandle handle in metadata.MethodDefinitions)
		{
			MethodDefinition method = metadata.GetMethodDefinition(handle);
			if (method.RelativeVirtualAddress == 0)
			{
				continue;
			}

			BlobReader il = reader.GetMethodBody(method.RelativeVirtualAddress).GetILReader();
			while (il.RemainingBytes > 0)
			{
				byte first = il.ReadByte();
				short value = first == 0xFE ? (short) (0xFE00 | il.ReadByte()) : first;
				OpCode opCode = OpCodesByValue[value];
				if (opCode.OperandType == OperandType.InlineMethod)
				{
					EntityHandle token = MetadataTokens.EntityHandle(il.ReadInt32());
					string? name = token.Kind switch
					{
						HandleKind.MethodDefinition =>
							metadata.GetString(metadata.GetMethodDefinition((MethodDefinitionHandle) token).Name),
						HandleKind.MemberReference =>
							metadata.GetString(metadata.GetMemberReference((MemberReferenceHandle) token).Name),
						HandleKind.MethodSpecification => DescribeSpecification(metadata, (MethodSpecificationHandle) token),
						_ => null
					};
					if (name is not null)
					{
						names.Add(name);
					}

					continue;
				}

				il.Offset += OperandSize(opCode.OperandType, ref il);
			}
		}

		return names;
	}

	private static string? DescribeSpecification(MetadataReader metadata, MethodSpecificationHandle handle)
	{
		EntityHandle method = metadata.GetMethodSpecification(handle).Method;
		return method.Kind switch
		{
			HandleKind.MethodDefinition =>
				metadata.GetString(metadata.GetMethodDefinition((MethodDefinitionHandle) method).Name),
			HandleKind.MemberReference =>
				metadata.GetString(metadata.GetMemberReference((MemberReferenceHandle) method).Name),
			_ => null
		};
	}

	private static int OperandSize(OperandType operandType, ref BlobReader il)
	{
		switch (operandType)
		{
			case OperandType.InlineNone:
				return 0;
			case OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar:
				return 1;
			case OperandType.InlineVar:
				return 2;
			case OperandType.InlineI8 or OperandType.InlineR:
				return 8;
			case OperandType.InlineSwitch:
				int count = il.ReadInt32();
				return count * 4;
			default:
				return 4;
		}
	}
}
