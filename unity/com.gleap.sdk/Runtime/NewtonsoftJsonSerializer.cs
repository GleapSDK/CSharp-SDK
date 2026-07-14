using System;
using System.Reflection;
using System.Text.Json; // JsonElement — raw config/actions forwarded verbatim in config-update
using GleapSDK.Serialization;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;

namespace GleapSDK.Unity
{
    /// <summary>
    /// IL2CPP-safe <see cref="IJsonSerializer"/> backed by Newtonsoft.Json (Unity's
    /// <c>com.unity.nuget.newtonsoft-json</c>), a drop-in for <c>SystemTextJsonSerializer</c> whose
    /// reflection-based serializer can be stripped/broken under IL2CPP/AOT. Produces the same wire shape:
    /// camelCase property names, null values omitted, enums as camelCase strings, and — crucially — the
    /// raw pass-through of <see cref="JsonElement"/> values (the flowConfig/projectActions embedded in
    /// <c>config-update</c>), which Newtonsoft does not handle natively.
    /// Pass it via <c>GleapUnity.AttachAsync(channel, key, new NewtonsoftJsonSerializer())</c>.
    /// </summary>
    public sealed class NewtonsoftJsonSerializer : IJsonSerializer
    {
        private static readonly JsonSerializerSettings Settings = CreateSettings();

        private static JsonSerializerSettings CreateSettings()
        {
            var settings = new JsonSerializerSettings
            {
                ContractResolver = new GleapContractResolver(),
                NullValueHandling = NullValueHandling.Ignore
            };
            settings.Converters.Add(new StringEnumConverter(new CamelCaseNamingStrategy()));
            settings.Converters.Add(new JsonElementConverter());
            return settings;
        }

        /// <summary>CamelCase resolver that also honours <c>System.Text.Json</c>'s
        /// <see cref="System.Text.Json.Serialization.JsonPropertyNameAttribute"/>, which the DTOs use and
        /// Newtonsoft ignores by default. Without this, <c>AIToolParameter.Enums</c>
        /// (<c>[JsonPropertyName("enum")]</c>) would serialize as <c>"enums"</c> and the web widget would
        /// not recognize the AI-tool parameter's allowed-values list.</summary>
        private sealed class GleapContractResolver : CamelCasePropertyNamesContractResolver
        {
            // Fully-qualified return type: System.Text.Json also defines a JsonProperty, so the bare name
            // is ambiguous once both namespaces are in scope (as they are on Unity too).
            protected override Newtonsoft.Json.Serialization.JsonProperty CreateProperty(
                MemberInfo member, MemberSerialization memberSerialization)
            {
                var property = base.CreateProperty(member, memberSerialization);
                var stj = member.GetCustomAttribute<System.Text.Json.Serialization.JsonPropertyNameAttribute>();
                if (stj != null && !string.IsNullOrEmpty(stj.Name))
                {
                    property.PropertyName = stj.Name;
                }
                return property;
            }
        }

        public string Serialize(object value) => JsonConvert.SerializeObject(value, Settings);

        public T Deserialize<T>(string json) => JsonConvert.DeserializeObject<T>(json, Settings)!;

        /// <summary>Writes a System.Text.Json <see cref="JsonElement"/> as its raw JSON so already-serialized
        /// config/actions pass through unchanged (Newtonsoft would otherwise reflect over its internals).</summary>
        private sealed class JsonElementConverter : JsonConverter<JsonElement>
        {
            public override void WriteJson(JsonWriter writer, JsonElement value, Newtonsoft.Json.JsonSerializer serializer)
                => writer.WriteRawValue(value.GetRawText());

            public override JsonElement ReadJson(JsonReader reader, Type objectType, JsonElement existingValue,
                bool hasExistingValue, Newtonsoft.Json.JsonSerializer serializer)
            {
                var raw = Newtonsoft.Json.Linq.JToken.Load(reader).ToString(Formatting.None);
                using var doc = JsonDocument.Parse(raw);
                return doc.RootElement.Clone();
            }
        }
    }
}
