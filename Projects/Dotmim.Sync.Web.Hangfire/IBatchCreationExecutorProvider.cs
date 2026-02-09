using Wormhole.Sync.Async;

namespace Wormhole.Sync.Web.Hangfire
{
    /// <summary>
    /// 
    /// </summary>
    public interface IBatchCreationExecutorProvider
    {
        IBatchCreationExecutor Provide(BatchCreationJobParameters batchCreationJobParameters);
    }
}