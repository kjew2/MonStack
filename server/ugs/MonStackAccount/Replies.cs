using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using IdleRng.Server;
using Newtonsoft.Json;

namespace MonStackAccount
{
    // What the endpoints return. Cloud Code serializes a reply with Newtonsoft.Json; the names here are the JSON the browser reads
    // (web/ugs-transport.js): camelCase, rejection codes as their names, an empty field left out.

    /// <summary>Open's answer (specs/27 section 5.1).</summary>
    public sealed class OpenReply
    {
        /// <summary>"None" when accepted, else the rejection's name (only "NotHolder" for an open).</summary>
        [JsonProperty("code")] public string Code { get; set; }
        [JsonProperty("accepted")] public bool Accepted { get; set; }

        /// <summary>The AccountSave text the engine loads, gzip then base64 (null when rejected).</summary>
        [JsonProperty("account", NullValueHandling = NullValueHandling.Ignore)] public string Account { get; set; }
        [JsonProperty("notice", NullValueHandling = NullValueHandling.Ignore)] public string Notice { get; set; }
        [JsonProperty("seq")] public int Seq { get; set; }
        [JsonProperty("hash", NullValueHandling = NullValueHandling.Ignore)] public string Hash { get; set; }
        [JsonProperty("created")] public bool Created { get; set; }
        [JsonProperty("contentVersion")] public string ContentVersion { get; set; }
        [JsonProperty("serverSec")] public long ServerSec { get; set; }
        [JsonProperty("day")] public int Day { get; set; }

        public static OpenReply From(OpenResult r) => new OpenReply
        {
            Code = r.Code.ToString(),
            Accepted = r.Accepted,
            Account = r.Account == null ? null : Gzip.Pack(r.Account),
            Notice = r.Notice,
            Seq = r.Seq,
            Hash = r.Hash,
            Created = r.Created,
            ContentVersion = r.ContentVersion,
            ServerSec = r.ServerSec,
            Day = r.Day,
        };
    }

    /// <summary>Commit's answer (specs/27 section 4).</summary>
    public sealed class CommitReply
    {
        [JsonProperty("code")] public string Code { get; set; }
        [JsonProperty("accepted")] public bool Accepted { get; set; }

        /// <summary>The refused command's position, or -1.</summary>
        [JsonProperty("index")] public int Index { get; set; }
        [JsonProperty("reason", NullValueHandling = NullValueHandling.Ignore)] public string Reason { get; set; }

        /// <summary>Accepted: the new commit number and hash. Mismatch: the hash the server computed.</summary>
        [JsonProperty("seq")] public int Seq { get; set; }
        [JsonProperty("hash", NullValueHandling = NullValueHandling.Ignore)] public string Hash { get; set; }
        [JsonProperty("serverSec")] public long ServerSec { get; set; }
        [JsonProperty("day")] public int Day { get; set; }

        public static CommitReply From(CommitResult r) => new CommitReply
        {
            Code = r.Code.ToString(),
            Accepted = r.Accepted,
            Index = r.Index,
            Reason = r.Reason,
            Seq = r.Seq,
            Hash = r.Hash,
            ServerSec = r.ServerSec,
            Day = r.Day,
        };
    }

    /// <summary>One row of the Endless Tower season board (specs/28 section 6.1): no player ids.</summary>
    public sealed class BoardRowReply
    {
        [JsonProperty("rank")] public int Rank { get; set; }
        [JsonProperty("name")] public string Name { get; set; }
        [JsonProperty("floor")] public int Floor { get; set; }
        [JsonProperty("you")] public bool You { get; set; }

        public static BoardRowReply From(BoardRow r) =>
            r == null ? null : new BoardRowReply { Rank = r.Rank, Name = r.Name, Floor = r.Floor, You = r.You };
    }

    /// <summary>EndlessBoard's answer.</summary>
    public sealed class BoardReply
    {
        [JsonProperty("season")] public int Season { get; set; }
        [JsonProperty("total")] public int Total { get; set; }
        [JsonProperty("top")] public List<BoardRowReply> Top { get; set; } = new List<BoardRowReply>();
        [JsonProperty("you", NullValueHandling = NullValueHandling.Ignore)] public BoardRowReply You { get; set; }

        public static BoardReply From(BoardView v) => new BoardReply
        {
            Season = v.Season,
            Total = v.Total,
            Top = v.Top.Select(BoardRowReply.From).ToList(),
            You = BoardRowReply.From(v.You),
        };
    }

    /// <summary>Warm's answer: proof the worker is up, for the keep-warm schedule (issue #109).</summary>
    public sealed class WarmReply
    {
        [JsonProperty("contentVersion")] public string ContentVersion { get; set; }
        [JsonProperty("serverSec")] public long ServerSec { get; set; }
    }

    /// <summary>The account text on the wire (specs/27 section 5.1): UTF-8, gzip, base64.</summary>
    public static class Gzip
    {
        public static string Pack(string text)
        {
            using (var ms = new MemoryStream())
            {
                using (var gz = new GZipStream(ms, CompressionLevel.Optimal, leaveOpen: true))
                {
                    var b = Encoding.UTF8.GetBytes(text);
                    gz.Write(b, 0, b.Length);
                }
                return System.Convert.ToBase64String(ms.ToArray());
            }
        }

        public static string Unpack(string base64)
        {
            using (var ms = new MemoryStream(System.Convert.FromBase64String(base64)))
            using (var gz = new GZipStream(ms, CompressionMode.Decompress))
            using (var r = new StreamReader(gz, new UTF8Encoding(false)))
                return r.ReadToEnd();
        }
    }
}
