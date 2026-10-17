namespace MoonTTS.CLI.Tests.Support;

/* File Structure:
        - Post(SendOrPostCallback d, object? state) : void
        - CreateCopy() : SynchronizationContext
        - Install() : IDisposable
*/

/* Runs posted callbacks immediately on the posting thread. Installed while a command starts, it makes Progress<T>
   (which posts every report to the context captured at construction) print synchronously, so progress output is
   complete when the command returns instead of trickling into later tests' captured console */
public sealed class CliInlineSynchronizationContext : SynchronizationContext
{
    /* Executes the callback right away */
    public override void Post(SendOrPostCallback d, object? state) => d(state);

    /* The context is stateless; the same instance can be shared */
    public override SynchronizationContext CreateCopy() => this;

    /* Makes this context current on the calling thread until the returned scope is disposed */
    public static IDisposable Install()
    {
        var previous = Current;
        SetSynchronizationContext(new CliInlineSynchronizationContext());
        return new Scope(previous);
    }

    /* Restores the previous context */
    private sealed class Scope(SynchronizationContext? previous) : IDisposable
    {
        /* Puts the original context back */
        public void Dispose() => SetSynchronizationContext(previous);
    }
}
