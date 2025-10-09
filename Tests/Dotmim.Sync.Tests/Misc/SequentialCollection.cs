using Xunit;

namespace Wormhole.Sync.Tests.Misc
{
    /// <summary>
    /// Defines a test collection that forces sequential execution of all tests within it.
    /// </summary>
    [CollectionDefinition("Sequential", DisableParallelization = true)]
    public class SequentialCollection { }
}