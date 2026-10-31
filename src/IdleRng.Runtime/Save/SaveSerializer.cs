using System;
using System.Globalization;
using System.IO;
using System.Numerics;
using System.Reflection;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

namespace IdleRng.Runtime.Save
{
    /// <summary>
    /// The save file format (specs/06 section 4): one ASCII header line plus a UTF-8 JSON body:
    /// <code>
    /// IDLERNG-SAVE fmt=json crc32=1a2b3c4d len=3917\n
    /// { "Kind": "monster_account", "SchemaVersion": 2, ... }
    /// </code>
    /// AccountSave builds the body. Enums are member names; ulong seeds are 16-char lowercase hex strings; doubles round-trip
    /// exactly.
    /// </summary>
    public static class SaveSerializer
    {
        const string Magic = "IDLERNG-SAVE";
        const int MaxHeaderBytes = 256;
        static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);

        public static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            ContractResolver = new SaveContractResolver(),
            Converters = { new StringEnumConverter() },
            NullValueHandling = NullValueHandling.Include,
            MissingMemberHandling = MissingMemberHandling.Ignore,
            DateParseHandling = DateParseHandling.None,
            FloatParseHandling = FloatParseHandling.Double,
            Culture = CultureInfo.InvariantCulture,
        };

        public static readonly JsonSerializer Serializer = JsonSerializer.Create(Settings);

        /// <summary>Indented bodies are easier to diff and hand-edit; a few KB either way.</summary>
        public static bool Indented = true;

        // ------------------------------------------------------------------ encode

        /// <summary>Wrap an arbitrary JSON body in a valid header (fixtures, debug import, tests). No schema checks.</summary>
        public static byte[] EncodeBody(string bodyJson)
        {
            var body = Utf8NoBom.GetBytes(bodyJson);
            var header = Encoding.ASCII.GetBytes(
                $"{Magic} fmt=json crc32={Crc32.Compute(body):x8} len={body.Length.ToString(CultureInfo.InvariantCulture)}\n");
            var file = new byte[header.Length + body.Length];
            Buffer.BlockCopy(header, 0, file, 0, header.Length);
            Buffer.BlockCopy(body, 0, file, header.Length, body.Length);
            return file;
        }

        // ------------------------------------------------------------------ decode

        /// <summary>
        /// Validate header, length and CRC, parse the body. Returns the body's SchemaVersion (authoritative; the
        /// header is only a container) and the raw JObject for migration. Throws <see cref="SaveCorruptException"/>.
        /// </summary>
        public static (int schemaVersion, JObject body) Decode(byte[] file)
        {
            if (file == null || file.Length == 0) throw new SaveCorruptException("empty file");
            int nl = Array.IndexOf(file, (byte)'\n', 0, Math.Min(file.Length, MaxHeaderBytes));
            if (nl < 0) throw new SaveCorruptException("no header line");
            string header = Encoding.ASCII.GetString(file, 0, nl);
            var parts = header.Split(' ');
            if (parts.Length != 4 || parts[0] != Magic) throw new SaveCorruptException($"bad header '{header}'");
            string fmt = Field(parts[1], "fmt"), crcHex = Field(parts[2], "crc32"), lenStr = Field(parts[3], "len");
            if (fmt != "json") throw new SaveCorruptException($"unsupported fmt '{fmt}'");
            if (crcHex.Length != 8 || !uint.TryParse(crcHex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint crc))
                throw new SaveCorruptException("bad crc32 field");
            if (!int.TryParse(lenStr, NumberStyles.None, CultureInfo.InvariantCulture, out int len))
                throw new SaveCorruptException("bad len field");

            int bodyStart = nl + 1;
            int remaining = file.Length - bodyStart;
            if (remaining < len) throw new SaveCorruptException($"truncated body ({remaining} < {len})");
            if (remaining > len) throw new SaveCorruptException($"trailing bytes ({remaining} > {len})");
            if (Crc32.Compute(file, bodyStart, len) != crc) throw new SaveCorruptException("crc mismatch");

            JObject body;
            try { body = ParseBody(Utf8NoBom.GetString(file, bodyStart, len)); }
            catch (JsonException e) { throw new SaveCorruptException("body is not valid JSON", e); }

            var v = body["SchemaVersion"];
            if (v == null || v.Type != JTokenType.Integer) throw new SaveCorruptException("SchemaVersion missing");
            return ((int)v, body);
        }

        /// <summary>Parse a body without a header (debug import, migration fixtures).</summary>
        public static JObject ParseBody(string json)
        {
            using (var reader = new JsonTextReader(new StringReader(json))
                   { DateParseHandling = DateParseHandling.None, FloatParseHandling = FloatParseHandling.Double })
            {
                var o = JObject.Load(reader);
                while (reader.Read())
                    if (reader.TokenType != JsonToken.Comment) throw new JsonReaderException("trailing content after body");
                return o;
            }
        }

        static string Field(string part, string key)
        {
            string prefix = key + "=";
            if (!part.StartsWith(prefix, StringComparison.Ordinal)) throw new SaveCorruptException($"header field '{key}' missing");
            return part.Substring(prefix.Length);
        }

        // ------------------------------------------------------------------ materialise

        // ------------------------------------------------------------------ json plumbing

        /// <summary>ulong properties whose name ends in "Seed" are written as 16 lowercase hex chars (specs/06 3.2).</summary>
        sealed class SaveContractResolver : DefaultContractResolver
        {
            static readonly HexUInt64Converter Hex = new HexUInt64Converter();

            protected override JsonProperty CreateProperty(MemberInfo member, MemberSerialization memberSerialization)
            {
                var p = base.CreateProperty(member, memberSerialization);
                if (p.PropertyType == typeof(ulong) && p.PropertyName.EndsWith("Seed", StringComparison.Ordinal))
                    p.Converter = Hex;
                return p;
            }
        }

        /// <summary>Writes "x16" hex; reads hex strings or (legacy / hand-edited) JSON integers.</summary>
        public sealed class HexUInt64Converter : JsonConverter
        {
            public override bool CanConvert(Type objectType) => objectType == typeof(ulong);

            public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer) =>
                writer.WriteValue(((ulong)value).ToString("x16", CultureInfo.InvariantCulture));

            public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
            {
                switch (reader.TokenType)
                {
                    case JsonToken.String:
                        var s = (string)reader.Value;
                        if (s.Length == 0 || s.Length > 16) throw new FormatException($"seed '{s}' is not 1-16 hex chars");
                        return ulong.Parse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                    case JsonToken.Integer:
                        return reader.Value is BigInteger b ? (ulong)b : Convert.ToUInt64(reader.Value, CultureInfo.InvariantCulture);
                    default:
                        throw new JsonSerializationException($"unexpected token {reader.TokenType} for a seed");
                }
            }
        }
    }
}
