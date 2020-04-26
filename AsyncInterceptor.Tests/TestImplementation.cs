using System.Threading.Tasks;
using FakeItEasy;

namespace AsyncInterceptor.Tests
{
    /// <summary>
    ///     We can't use a Fake object directly, as a Fake is a proxy already and Castle forbids proxy-ing proxies (i.e., we
    ///     cannot
    ///     add an Autofac interceptor for a registered Fake instance).
    /// </summary>
    internal class TestImplementation : ITestInterface
    {
        public const int FuncResult = 5;

        public TestImplementation()
        {
            A.CallTo(() => Fake.SyncFunc()).Returns(FuncResult);
            A.CallTo(() => Fake.AsyncAction()).Returns(Task.CompletedTask);
            A.CallTo(() => Fake.AsyncFunc()).Returns(Task.FromResult(FuncResult));
            A.CallTo(() => Fake.AsyncValueTaskAction()).Returns(default);
            A.CallTo(() => Fake.AsyncValueTaskFunc()).Returns(new ValueTask<int>(FuncResult));
        }

        public ITestInterface Fake { get; } = A.Fake<ITestInterface>();

        public void SyncAction()
        {
            Fake.SyncAction();
        }

        public int SyncFunc()
        {
            return Fake.SyncFunc();
        }

        public Task AsyncAction()
        {
            return Fake.AsyncAction();
        }

        public Task<int> AsyncFunc()
        {
            return Fake.AsyncFunc();
        }

        public ValueTask AsyncValueTaskAction()
        {
            return Fake.AsyncValueTaskAction();
        }

        public ValueTask<int> AsyncValueTaskFunc()
        {
            return Fake.AsyncValueTaskFunc();
        }
    }
}