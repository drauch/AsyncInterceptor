using System.Threading.Tasks;

namespace AsyncInterceptor.Tests
{
    public interface ITestInterface
    {
        void SyncAction();
        int SyncFunc();
        Task AsyncAction();
        Task<int> AsyncFunc();
        ValueTask AsyncValueTaskAction();
        ValueTask<int> AsyncValueTaskFunc();
    }
}