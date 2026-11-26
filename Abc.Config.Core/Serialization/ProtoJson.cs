using System.Globalization;
using System.Numerics;
using System.Reflection;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using ProtoBuf;

namespace Abc.Config.Core.Serialization;

public static class ProtoJson
{
	public static JsonSerializerOptions Options { get; } = new()
	{
		Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
		DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
		NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
		TypeInfoResolver = new DefaultJsonTypeInfoResolver { Modifiers = { UseProtoMembers } },
		Converters = { new JsonStringEnumConverter(), new Int64String<long>(), new Int64String<ulong>() },
		WriteIndented = true,
		MaxDepth = 256,
	};

	private static void UseProtoMembers(JsonTypeInfo type)
	{
		if (type.Kind != JsonTypeInfoKind.Object || !type.Type.IsDefined(typeof(ProtoContractAttribute), false))
			return;
		for (int i = type.Properties.Count - 1; i >= 0; i--)
		{
			JsonPropertyInfo property = type.Properties[i];
			if (property.AttributeProvider is not PropertyInfo member || member.GetCustomAttribute<ProtoMemberAttribute>() is not { } proto)
			{
				type.Properties.RemoveAt(i);
				continue;
			}
			property.Order = proto.Tag;
			if (type.Type.GetMethod("ShouldSerialize" + member.Name, Type.EmptyTypes) is { ReturnType: var result } method &&
				result == typeof(bool))
				property.ShouldSerialize = (owner, _) => (bool)method.Invoke(owner, null)!;
		}
	}

	private sealed class Int64String<T> : JsonConverter<T> where T : IBinaryInteger<T>
	{
		public override T Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options) =>
			T.Parse(reader.GetString()!, CultureInfo.InvariantCulture);

		public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options) =>
			writer.WriteStringValue(value.ToString(null, CultureInfo.InvariantCulture));

		public override T ReadAsPropertyName(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options) =>
			T.Parse(reader.GetString()!, CultureInfo.InvariantCulture);

		public override void WriteAsPropertyName(Utf8JsonWriter writer, T value, JsonSerializerOptions options) =>
			writer.WritePropertyName(value.ToString(null, CultureInfo.InvariantCulture));
	}
}
