using AI_Native_Runtime_Windows.Services.Transport;
using Xunit;

namespace AI_Native_Runtime_Windows.Tests
{
    /// <summary>Real round-trips against `%LOCALAPPDATA%\AINativeRuntime\event-cursor.json` -
    /// not a mock file system, mirroring this project's own precedent of testing
    /// persistence against the real mechanism rather than an abstraction over it.</summary>
    public class CursorStoreTests
    {
        [Fact]
        public void Load_returns_null_when_nothing_was_ever_saved()
        {
            var store = new CursorStore();
            store.Clear();
            Assert.Null(store.Load());
        }

        [Fact]
        public void Save_then_load_round_trips_exactly()
        {
            var store = new CursorStore();
            store.Save(new EventCursor("default", 42));
            var loaded = store.Load();
            Assert.NotNull(loaded);
            Assert.Equal("default", loaded!.StreamId);
            Assert.Equal(42, loaded.Sequence);
            store.Clear();
        }

        [Fact]
        public void Clear_removes_a_previously_saved_cursor()
        {
            var store = new CursorStore();
            store.Save(new EventCursor("default", 7));
            store.Clear();
            Assert.Null(store.Load());
        }
    }
}
