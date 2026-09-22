using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CheatEngine.Client.Repository.Tests.Release;

/// <summary>
/// Validates a JSON document against the subset of JSON Schema draft 2020-12 that the repository schemas use:
/// <c>type</c> (a name or a list), <c>const</c>, <c>enum</c>, <c>pattern</c>, <c>minLength</c>, <c>required</c>,
/// <c>properties</c>, <c>additionalProperties: false</c>, <c>items</c>, <c>minItems</c>, <c>maxItems</c>,
/// <c>uniqueItems</c> and local <c>$ref</c> to <c>#/$defs/…</c>. Any other keyword fails the validation, so a schema
/// cannot silently rely on a rule this validator ignores.
/// </summary>
internal static class JsonSchemaSubset
{
	private static readonly HashSet<string> _supportedKeywords = new(StringComparer.Ordinal)
	{
		"$schema", "$id", "$defs", "$ref", "title", "description", "type", "const", "enum", "pattern", "minLength",
		"required", "properties", "additionalProperties", "items", "minItems", "maxItems", "uniqueItems"
	};

	/// <summary>Returns every violation as <c>path: reason</c>; an empty list means the document is valid.</summary>
	internal static IReadOnlyList<string> Validate(JsonElement schema, JsonElement document)
	{
		List<string> errors = [];
		Validate(schema, schema, document, "$", errors);
		return errors;
	}

	/// <summary>The <c>required</c> list of the schema root, or of the named root property.</summary>
	internal static string[] RequiredNames(JsonElement schema, params string[] propertyPath)
	{
		ArgumentNullException.ThrowIfNull(propertyPath);

		JsonElement current = schema;
		foreach (string property in propertyPath)
		{
			current = Resolve(schema, current.GetProperty("properties").GetProperty(property));
			if (current.TryGetProperty("items", out JsonElement items))
			{
				current = Resolve(schema, items);
			}
		}

		return current.GetProperty("required").EnumerateArray().Select(static name => name.GetString()!).ToArray();
	}

	private static JsonElement Resolve(JsonElement root, JsonElement schema)
	{
		if (!schema.TryGetProperty("$ref", out JsonElement reference))
		{
			return schema;
		}

		string path = reference.GetString()!;
		if (!path.StartsWith("#/$defs/", StringComparison.Ordinal))
		{
			throw new NotSupportedException($"Only local #/$defs references are supported, not '{path}'.");
		}

		return root.GetProperty("$defs").GetProperty(path["#/$defs/".Length..]);
	}

	private static void Validate(JsonElement root, JsonElement schema, JsonElement value, string path, List<string> errors)
	{
		schema = Resolve(root, schema);
		foreach (JsonProperty keyword in schema.EnumerateObject())
		{
			if (!_supportedKeywords.Contains(keyword.Name))
			{
				errors.Add($"{path}: the schema uses the unsupported keyword '{keyword.Name}'");
			}
		}

		if (schema.TryGetProperty("type", out JsonElement type) && !MatchesType(type, value))
		{
			errors.Add($"{path}: expected type {type.GetRawText()}, found {value.ValueKind}");
			return;
		}

		if (schema.TryGetProperty("const", out JsonElement constant) && !JsonElement.DeepEquals(constant, value))
		{
			errors.Add($"{path}: expected the constant {constant.GetRawText()}, found {value.GetRawText()}");
		}

		if (schema.TryGetProperty("enum", out JsonElement allowed)
			&& !allowed.EnumerateArray().Any(candidate => JsonElement.DeepEquals(candidate, value)))
		{
			errors.Add($"{path}: {value.GetRawText()} is not one of {allowed.GetRawText()}");
		}

		if (value.ValueKind == JsonValueKind.String)
		{
			ValidateString(schema, value.GetString()!, path, errors);
		}
		else if (value.ValueKind == JsonValueKind.Object)
		{
			ValidateObject(root, schema, value, path, errors);
		}
		else if (value.ValueKind == JsonValueKind.Array)
		{
			ValidateArray(root, schema, value, path, errors);
		}
	}

	private static void ValidateString(JsonElement schema, string text, string path, List<string> errors)
	{
		if (schema.TryGetProperty("pattern", out JsonElement pattern)
			&& !Regex.IsMatch(text, pattern.GetString()!, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)))
		{
			errors.Add($"{path}: '{text}' does not match {pattern.GetString()}");
		}

		if (schema.TryGetProperty("minLength", out JsonElement minLength) && text.Length < minLength.GetInt32())
		{
			errors.Add($"{path}: '{text}' is shorter than {minLength.GetInt32()} characters");
		}
	}

	private static void ValidateObject(JsonElement root, JsonElement schema, JsonElement value, string path, List<string> errors)
	{
		if (schema.TryGetProperty("required", out JsonElement required))
		{
			foreach (JsonElement name in required.EnumerateArray())
			{
				if (!value.TryGetProperty(name.GetString()!, out _))
				{
					errors.Add($"{path}: the required property '{name.GetString()}' is missing");
				}
			}
		}

		bool hasProperties = schema.TryGetProperty("properties", out JsonElement properties);
		bool closed = schema.TryGetProperty("additionalProperties", out JsonElement additional)
					  && additional.ValueKind == JsonValueKind.False;
		foreach (JsonProperty property in value.EnumerateObject())
		{
			if (hasProperties && properties.TryGetProperty(property.Name, out JsonElement propertySchema))
			{
				Validate(root, propertySchema, property.Value, $"{path}.{property.Name}", errors);
			}
			else if (closed)
			{
				errors.Add($"{path}: the property '{property.Name}' is not allowed");
			}
		}
	}

	private static void ValidateArray(JsonElement root, JsonElement schema, JsonElement value, string path, List<string> errors)
	{
		int length = value.GetArrayLength();
		if (schema.TryGetProperty("minItems", out JsonElement minItems) && length < minItems.GetInt32())
		{
			errors.Add($"{path}: {length} items, expected at least {minItems.GetInt32()}");
		}

		if (schema.TryGetProperty("maxItems", out JsonElement maxItems) && length > maxItems.GetInt32())
		{
			errors.Add($"{path}: {length} items, expected at most {maxItems.GetInt32()}");
		}

		if (schema.TryGetProperty("uniqueItems", out JsonElement unique) && unique.ValueKind == JsonValueKind.True)
		{
			HashSet<string> seen = new(StringComparer.Ordinal);
			foreach (JsonElement item in value.EnumerateArray())
			{
				if (!seen.Add(item.GetRawText()))
				{
					errors.Add($"{path}: the item {item.GetRawText()} is repeated");
				}
			}
		}

		if (schema.TryGetProperty("items", out JsonElement items))
		{
			int index = 0;
			foreach (JsonElement item in value.EnumerateArray())
			{
				Validate(root, items, item, string.Create(CultureInfo.InvariantCulture, $"{path}[{index}]"), errors);
				index++;
			}
		}
	}

	private static bool MatchesType(JsonElement type, JsonElement value)
	{
		if (type.ValueKind == JsonValueKind.Array)
		{
			return type.EnumerateArray().Any(candidate => MatchesType(candidate, value));
		}

		return type.GetString() switch
		{
			"object" => value.ValueKind == JsonValueKind.Object,
			"array" => value.ValueKind == JsonValueKind.Array,
			"string" => value.ValueKind == JsonValueKind.String,
			"boolean" => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
			"null" => value.ValueKind == JsonValueKind.Null,
			"number" => value.ValueKind == JsonValueKind.Number,
			"integer" => value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out _),
			_ => throw new NotSupportedException($"Unsupported JSON Schema type '{type.GetString()}'.")
		};
	}
}
