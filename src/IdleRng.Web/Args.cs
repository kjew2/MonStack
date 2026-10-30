using System;
using System.Linq;
using System.Text.Json;

namespace IdleRng.Web
{
    /// <summary>Positional JSON argument list ("[]", "[3]", "[{\"tier\":0},null]"). Missing and null both read as absent.</summary>
    internal sealed class Args
    {
        private readonly JsonElement[] _a;

        public Args(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) { _a = Array.Empty<JsonElement>(); return; }
            using var doc = JsonDocument.Parse(json);
            _a = doc.RootElement.ValueKind == JsonValueKind.Array
                ? doc.RootElement.EnumerateArray().Select(e => e.Clone()).ToArray()
                : Array.Empty<JsonElement>();
        }

        public bool Has(int i) => i < _a.Length && _a[i].ValueKind != JsonValueKind.Null && _a[i].ValueKind != JsonValueKind.Undefined;
        public double Num(int i) => Has(i) && _a[i].ValueKind == JsonValueKind.Number ? _a[i].GetDouble() : throw new ArgumentException("argument " + i + " must be a number");
        public int Int(int i) => (int)Math.Max(int.MinValue, Math.Min(int.MaxValue, Math.Floor(Num(i))));
        public string Str(int i) => Has(i) && _a[i].ValueKind == JsonValueKind.String ? _a[i].GetString() : throw new ArgumentException("argument " + i + " must be a string");
        public JsonElement El(int i) => Has(i) ? _a[i] : throw new ArgumentException("argument " + i + " missing");
    }
}
