using System;

namespace IdleRng.Runtime.Save
{
    /// <summary>Header/length/CRC mismatch or unparsable JSON (specs/06 rule R1). The loader falls back to the backup.</summary>
    public sealed class SaveCorruptException : Exception
    {
        public SaveCorruptException(string message, Exception inner = null) : base(message, inner) { }
    }

    /// <summary>Parsed, but the content is unusable (wrong kind, missing sections, a run that no longer replays).</summary>
    public sealed class SaveInvalidException : Exception
    {
        public SaveInvalidException(string message, Exception inner = null) : base(message, inner) { }
    }

    /// <summary>Any failure while writing. The caller keeps its in-memory state and retries later.</summary>
    public sealed class SaveWriteException : Exception
    {
        public SaveWriteException(string message, Exception inner = null) : base(message, inner) { }
    }
}
