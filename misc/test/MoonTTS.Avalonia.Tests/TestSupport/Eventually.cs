namespace MoonTTS.Avalonia.Tests.TestSupport;

/* File Structure:
        - DefaultTimeout : TimeSpan
        - UntilAsync(Func<bool> condition, string because, TimeSpan? timeout) : Task
*/

/* Waits for the observable outcome of fire-and-forget work (e.g. `_ = SwitchModelAsync(...)` or a
   Task.Yield based revert). Polls the condition instead of sleeping a fixed time, so tests are fast when the
   work is fast and only fail after a generous timeout. Inside [AvaloniaFact] tests the awaited delay resumes
   on the dispatcher, which keeps processing posted jobs while waiting */
public static class Eventually
{
    /* Generous upper bound; a passing test never waits this long */
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(10);

    /* Returns as soon as the condition holds, or fails the test with `because` once the timeout elapses */
    public static async Task UntilAsync(Func<bool> condition, string because, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? DefaultTimeout);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
                Assert.Fail($"Timed out waiting until {because}");
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
    }
}
