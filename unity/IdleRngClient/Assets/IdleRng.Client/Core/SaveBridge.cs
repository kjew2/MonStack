using System;
using IdleRng.Sim;
using IdleRng.Sim.Content;
using IdleRng.Sim.Events;
using IdleRng.Sim.State;
using IdleRng.Sim.Time;

// ============================================================================================================
// SaveBridge: the ONLY file in the client that knows how a profile is created, loaded, saved or deleted.
// Everything else gets a ready Simulation from ISaveBridge.Open and calls Save on pause / quit / autosave /
// after pulls. To wire IdleRng.Runtime (specs/06 FileSaveStore / GameSession) in:
//   1. add "IdleRng.Runtime" to Core/IdleRng.Client.Core.asmdef "references" (and the package to Packages/manifest.json);
//   2. implement ISaveBridge with the Runtime store and return it from SaveBridgeFactory.Create.
// Nothing outside this file needs to change.
// ============================================================================================================

namespace IdleRng.Client.Core
{
    public interface ISaveBridge
    {
        /// <summary>Shown in the debug panel, e.g. "ephemeral (new profile every launch)".</summary>
        string Description { get; }

        /// <summary>
        /// Load the saved profile, or create a new one (with the profile-created tutorial grants applied), and return
        /// a Simulation that publishes into <paramref name="events"/>. Must NOT call SyncWallClock: the caller does
        /// that right after so the offline-earnings popup sees the OfflineGoldCredited event.
        /// </summary>
        Simulation Open(ContentDb content, IEventSink events, IClock clock);

        /// <summary>Persist the current state (synchronous; called on the main thread between ticks).</summary>
        void Save(Simulation sim);

        /// <summary>Delete the profile; the next Open creates a new one.</summary>
        void DeleteProfile();
    }

    public static class SaveBridgeFactory
    {
        /// <param name="dataDir">Application.persistentDataPath (unused until the Runtime store is wired).</param>
        public static ISaveBridge Create(string dataDir) => new EphemeralSaveBridge();
    }

    /// <summary>POC placeholder: no persistence. Every launch (and every "Reset profile") starts a new profile.</summary>
    public sealed class EphemeralSaveBridge : ISaveBridge
    {
        public string Description => "ephemeral (new profile every launch, saves are no-ops)";

        public Simulation Open(ContentDb content, IEventSink events, IClock clock)
        {
            var state = GameState.NewProfile(content, Guid.NewGuid(), clock.NowServerSeconds);
            var sim = new Simulation(content, state, events);
            sim.ApplyTutorialGrants();
            return sim;
        }

        public void Save(Simulation sim) { }

        public void DeleteProfile() { }
    }
}
