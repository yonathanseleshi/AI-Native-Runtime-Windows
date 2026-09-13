using AI_Native_Runtime_Windows.Services;
using Xunit;

namespace AI_Native_Runtime_Windows.Tests
{
    /// <summary>Real round-trips against the actual Windows Credential Manager (via
    /// `CredWriteW`/`CredReadW`/`CredDeleteW`) - not a mock, mirroring MAC's own
    /// `KeychainStoreTests` precedent (Phase 1 report: "Keychain round-trips" as real
    /// tests, not abstracted away). A dedicated test key is used and always cleaned up.</summary>
    public class CredentialStoreTests
    {
        private const string TestKey = "windows-tests-roundtrip-key";

        [Fact]
        public void Put_then_TryGet_round_trips_exactly()
        {
            var store = new CredentialStore();
            store.Delete(TestKey);
            try
            {
                store.Put(TestKey, "sentinel-value-42");
                var found = store.TryGet(TestKey, out var value);
                Assert.True(found);
                Assert.Equal("sentinel-value-42", value);
            }
            finally
            {
                store.Delete(TestKey);
            }
        }

        [Fact]
        public void TryGet_returns_false_for_a_key_that_was_never_stored()
        {
            var store = new CredentialStore();
            store.Delete(TestKey);
            var found = store.TryGet(TestKey, out _);
            Assert.False(found);
        }

        [Fact]
        public void Delete_on_an_absent_key_is_not_an_error()
        {
            var store = new CredentialStore();
            store.Delete(TestKey); // no throw, even though nothing was ever stored
        }
    }
}
