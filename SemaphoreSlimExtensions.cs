namespace MIDIRift;

internal static class SemaphoreSlimExtensions
{
    public static void ReleaseSafely(this SemaphoreSlim semaphore)
    {
        try { semaphore.Release(); }
        catch (SemaphoreFullException) { }
        catch (ObjectDisposedException) { }
    }
}
