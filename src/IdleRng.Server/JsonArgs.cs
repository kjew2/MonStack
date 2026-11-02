using System;
using System.Collections.Generic;
using IdleRng.Sim.Account;
using Newtonsoft.Json.Linq;

namespace IdleRng.Server
{
    /// <summary>
    /// A command's positional arguments from Newtonsoft JSON, read by the same rules as the browser bridge's System.Text.Json
    /// reader (IdleRng.Web.Args): missing and null both read as absent, and a value of the wrong JSON type is refused.
    /// </summary>
    public sealed class JsonArgs : ICommandArgs
    {
        readonly JArray _a;

        public JsonArgs(JArray a) { _a = a ?? new JArray(); }

        JToken At(int i) => i >= 0 && i < _a.Count ? _a[i] : null;

        public bool Has(int i)
        {
            var t = At(i);
            return t != null && t.Type != JTokenType.Null && t.Type != JTokenType.Undefined;
        }

        JToken El(int i) => Has(i) ? _a[i] : throw new ArgumentException("argument " + i + " missing");

        public double Num(int i)
        {
            var t = At(i);
            return t != null && (t.Type == JTokenType.Integer || t.Type == JTokenType.Float) ? (double)t : throw new ArgumentException("argument " + i + " must be a number");
        }

        public string Str(int i)
        {
            var t = At(i);
            return t != null && t.Type == JTokenType.String ? (string)t : throw new ArgumentException("argument " + i + " must be a string");
        }

        public bool Bool(int i)
        {
            var t = At(i);
            return t != null && t.Type == JTokenType.Boolean ? (bool)t : throw new ArgumentException("argument " + i + " must be true or false");
        }

        public List<string> StrList(int i)
        {
            if (!(El(i) is JArray arr)) throw new ArgumentException("argument " + i + " must be a list");
            var list = new List<string>();
            foreach (var x in arr)
                list.Add(x.Type == JTokenType.String ? (string)x : throw new ArgumentException("argument " + i + " must list strings"));
            return list;
        }

        public List<int> IntList(int i)
        {
            if (!(El(i) is JArray arr)) throw new ArgumentException("argument " + i + " must be a list");
            var list = new List<int>();
            foreach (var x in arr)
            {
                if (x.Type != JTokenType.Integer) throw new ArgumentException("argument " + i + " must list whole numbers");
                var v = ((JValue)x).Value;
                long n = v is System.Numerics.BigInteger ? long.MaxValue : Convert.ToInt64(v, System.Globalization.CultureInfo.InvariantCulture);
                list.Add(n >= int.MinValue && n <= int.MaxValue ? (int)n : throw new ArgumentException("argument " + i + " must list whole numbers"));
            }
            return list;
        }
    }
}
