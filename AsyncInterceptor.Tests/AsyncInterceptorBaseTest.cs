using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Autofac;
using Autofac.Extras.DynamicProxy;
using Castle.DynamicProxy;
using FakeItEasy;
using FluentAssertions;
using NUnit.Framework;

namespace AsyncInterceptor.Tests
{
    [TestFixture]
    public class AsyncInterceptorBaseTest
    {
        [SetUp]
        public void SetUp()
        {
            _sutAsserter = A.Fake<IAsyncInterceptorBaseInterface>();
            _sut = new TestAsyncInterceptor(_sutAsserter);

            _toBeIntercepted = new TestImplementation();
            var builder = new ContainerBuilder();
            builder.RegisterInstance(_sut).As<TestAsyncInterceptor>();
            builder.RegisterInstance(_toBeIntercepted).As<ITestInterface>()
                .EnableInterfaceInterceptors()
                .InterceptedBy(typeof(TestAsyncInterceptor));
            _container = builder.Build();

            _intercepted = _container.Resolve<ITestInterface>();
        }

        [TearDown]
        public void TearDown()
        {
            _container?.Dispose();
        }

        private static readonly object NoResult = new object();

        private IAsyncInterceptorBaseInterface _sutAsserter;
        private TestAsyncInterceptor _sut;
        private TestImplementation _toBeIntercepted;
        private IContainer _container;
        private ITestInterface _intercepted;

        private static IEnumerable<TestCaseData> TestCases()
        {
            yield return new TestCaseData(nameof(ITestInterface.SyncAction),
                (Func<ITestInterface, Task<object>>) (ti =>
                {
                    ti.SyncAction();
                    return Task.FromResult(NoResult);
                }),
                NoResult);

            yield return new TestCaseData(nameof(ITestInterface.SyncFunc),
                (Func<ITestInterface, Task<object>>) (ti => Task.FromResult((object) ti.SyncFunc())),
                TestImplementation.FuncResult);

            yield return new TestCaseData(nameof(ITestInterface.AsyncAction),
                (Func<ITestInterface, Task<object>>) (async ti =>
                {
                    await ti.AsyncAction();
                    return NoResult;
                }),
                NoResult);

            yield return new TestCaseData(nameof(ITestInterface.AsyncFunc),
                (Func<ITestInterface, Task<object>>) (async ti => await ti.AsyncFunc()),
                TestImplementation.FuncResult);

            yield return new TestCaseData(nameof(ITestInterface.AsyncValueTaskAction),
                (Func<ITestInterface, Task<object>>) (async ti =>
                {
                    await ti.AsyncValueTaskAction();
                    return NoResult;
                }),
                NoResult);

            yield return new TestCaseData(nameof(ITestInterface.AsyncValueTaskFunc),
                (Func<ITestInterface, Task<object>>) (async ti => await ti.AsyncValueTaskFunc().AsTask()),
                TestImplementation.FuncResult);
        }

        [TestCaseSource(nameof(TestCases))]
        public async Task RunToCompletion(string method, Func<ITestInterface, Task<object>> callSutMethod,
            object expectedResult)
        {
            // ACT
            var result = await callSutMethod(_intercepted);

            // ASSERT
            if (expectedResult != NoResult)
                result.Should().Be(expectedResult);

            var expectedOriginalReturnValue = expectedResult == NoResult ? null : expectedResult;
            A.CallTo(() => _sutAsserter.BeforeProceed(A<IInvocation>._)).MustHaveHappened()
                .Then(A.CallTo(_toBeIntercepted.Fake).Where(x => x.Method.Name == method).MustHaveHappened())
                .Then(A.CallTo(() => _sutAsserter.AfterProceed(A<IInvocation>._, null, expectedOriginalReturnValue))
                    .MustHaveHappened())
                .Then(A.CallTo(() => _sutAsserter.Finally(A<IInvocation>._, null)).MustHaveHappened());

            A.CallTo(() => _sutAsserter.OnException(A<IInvocation>._, A<Exception>._, A<object>._))
                .MustNotHaveHappened();
        }

        [TestCaseSource(nameof(TestCases))]
        public async Task RunToCompletion_WithState(string method,
            Func<ITestInterface, Task<object>> callSutMethod, object expectedResult)
        {
            var state = new object();
            A.CallTo(() => _sutAsserter.BeforeProceed(A<IInvocation>._)).Returns(new BeforeProceedResult(true, state));

            // ACT
            var result = await callSutMethod(_intercepted);

            // ASSERT
            if (expectedResult != NoResult)
                result.Should().Be(expectedResult);

            var expectedOriginalReturnValue = expectedResult == NoResult ? null : expectedResult;
            A.CallTo(() => _sutAsserter.AfterProceed(A<IInvocation>._, state, expectedOriginalReturnValue))
                .MustHaveHappened();
            A.CallTo(() => _sutAsserter.Finally(A<IInvocation>._, state)).MustHaveHappened();
        }

        [TestCaseSource(nameof(TestCases))]
        public void WithExceptionInProceed_RunsIntoExceptionHandler_RethrowsException(string method,
            Func<ITestInterface, Task<object>> callSutMethod, object expectedResult)
        {
            var exception = new TestException();
            A.CallTo(_toBeIntercepted.Fake).Where(x => x.Method.Name == method).Throws(exception);

            // ACT
            Func<Task> act = async () => await callSutMethod(_intercepted);

            // ASSERT
            act.Should().ThrowExactly<TestException>().Which.Should().BeSameAs(exception);

            A.CallTo(() => _sutAsserter.BeforeProceed(A<IInvocation>._)).MustHaveHappened()
                .Then(A.CallTo(_toBeIntercepted.Fake).Where(x => x.Method.Name == method).MustHaveHappened())
                .Then(A.CallTo(() => _sutAsserter.OnException(A<IInvocation>._, exception, null)).MustHaveHappened())
                .Then(A.CallTo(() => _sutAsserter.Finally(A<IInvocation>._, null)).MustHaveHappened());

            A.CallTo(() => _sutAsserter.AfterProceed(A<IInvocation>._, A<object>._, A<object>._)).MustNotHaveHappened();
        }

        [TestCaseSource(nameof(TestCases))]
        public void WithExceptionInProceed_WithState_RunsIntoExceptionHandler_RethrowsException(string method,
            Func<ITestInterface, Task<object>> callSutMethod, object expectedResult)
        {
            var exception = new TestException();
            A.CallTo(_toBeIntercepted.Fake).Where(x => x.Method.Name == method).Throws(exception);

            var state = new object();
            A.CallTo(() => _sutAsserter.BeforeProceed(A<IInvocation>._)).Returns(new BeforeProceedResult(true, state));

            // ACT
            Func<Task> act = async () => await callSutMethod(_intercepted);

            // ASSERT
            act.Should().ThrowExactly<TestException>().Which.Should().BeSameAs(exception);

            A.CallTo(() => _sutAsserter.BeforeProceed(A<IInvocation>._)).MustHaveHappened()
                .Then(A.CallTo(_toBeIntercepted.Fake).Where(x => x.Method.Name == method).MustHaveHappened())
                .Then(A.CallTo(() => _sutAsserter.OnException(A<IInvocation>._, exception, state)).MustHaveHappened())
                .Then(A.CallTo(() => _sutAsserter.Finally(A<IInvocation>._, state)).MustHaveHappened());

            A.CallTo(() => _sutAsserter.AfterProceed(A<IInvocation>._, A<object>._, A<object>._)).MustNotHaveHappened();
        }

        [TestCaseSource(nameof(TestCases))]
        public void WithExceptionInBeforeProceed_RethrowsException(string method,
            Func<ITestInterface, Task<object>> callSutMethod, object expectedResult)
        {
            var exception = new TestException();
            A.CallTo(() => _sutAsserter.BeforeProceed(A<IInvocation>._)).Throws(exception);

            // ACT
            Func<Task> act = async () => await callSutMethod(_intercepted);

            // ASSERT
            act.Should().ThrowExactly<TestException>().Which.Should().BeSameAs(exception);

            A.CallTo(() => _sutAsserter.BeforeProceed(A<IInvocation>._)).MustHaveHappened();
            A.CallTo(_toBeIntercepted.Fake).Where(x => x.Method.Name == method).MustNotHaveHappened();
            A.CallTo(() => _sutAsserter.AfterProceed(A<IInvocation>._, A<object>._, A<object>._)).MustNotHaveHappened();
            A.CallTo(() => _sutAsserter.OnException(A<IInvocation>._, exception, null)).MustNotHaveHappened();
            A.CallTo(() => _sutAsserter.Finally(A<IInvocation>._, null)).MustNotHaveHappened();
        }

        [TestCaseSource(nameof(TestCases))]
        public void WithExceptionInAfterProceed_RunsIntoExceptionHandler_RethrowsException(string method,
            Func<ITestInterface, Task<object>> callSutMethod, object expectedResult)
        {
            var exception = new TestException();
            A.CallTo(() => _sutAsserter.AfterProceed(A<IInvocation>._, A<object>._, A<object>._)).Throws(exception);

            // ACT
            Func<Task> act = async () => await callSutMethod(_intercepted);

            // ASSERT
            act.Should().ThrowExactly<TestException>().Which.Should().BeSameAs(exception);

            A.CallTo(() => _sutAsserter.BeforeProceed(A<IInvocation>._)).MustHaveHappened()
                .Then(A.CallTo(_toBeIntercepted.Fake).Where(x => x.Method.Name == method).MustHaveHappened())
                .Then(A.CallTo(() => _sutAsserter.AfterProceed(A<IInvocation>._, A<object>._, A<object>._))
                    .MustHaveHappened())
                .Then(A.CallTo(() => _sutAsserter.OnException(A<IInvocation>._, exception, null)).MustHaveHappened())
                .Then(A.CallTo(() => _sutAsserter.Finally(A<IInvocation>._, null)).MustHaveHappened());
        }

        [TestCaseSource(nameof(TestCases))]
        public void WithExceptionInOnException_DropsOriginalException_ThrowsNewException(string method,
            Func<ITestInterface, Task<object>> callSutMethod, object expectedResult)
        {
            var actionException = new TestException();
            A.CallTo(_toBeIntercepted.Fake).Where(x => x.Method.Name == method).Throws(actionException);

            var onExceptionException = new TestException();
            A.CallTo(() => _sutAsserter.OnException(A<IInvocation>._, A<Exception>._, A<object>._))
                .Throws(onExceptionException);

            // ACT
            Func<Task> act = async () => await callSutMethod(_intercepted);

            // ASSERT
            act.Should().ThrowExactly<TestException>().Which.Should().BeSameAs(onExceptionException);

            A.CallTo(() => _sutAsserter.BeforeProceed(A<IInvocation>._)).MustHaveHappened()
                .Then(A.CallTo(_toBeIntercepted.Fake).Where(x => x.Method.Name == method).MustHaveHappened())
                .Then(A.CallTo(() => _sutAsserter.OnException(A<IInvocation>._, actionException, null))
                    .MustHaveHappened())
                .Then(A.CallTo(() => _sutAsserter.Finally(A<IInvocation>._, null)).MustHaveHappened());
        }

        [TestCaseSource(nameof(TestCases))]
        public void WithExceptionInFinally_DropsAnyOriginalException_ThrowsNewException(string method,
            Func<ITestInterface, Task<object>> callSutMethod, object expectedResult)
        {
            var actionException = new TestException();
            A.CallTo(_toBeIntercepted.Fake).Where(x => x.Method.Name == method).Throws(actionException);

            var finallyException = new TestException();
            A.CallTo(() => _sutAsserter.Finally(A<IInvocation>._, A<object>._)).Throws(finallyException);

            // ACT
            Func<Task> act = async () => await callSutMethod(_intercepted);

            // ASSERT
            act.Should().ThrowExactly<TestException>().Which.Should().BeSameAs(finallyException);

            A.CallTo(() => _sutAsserter.BeforeProceed(A<IInvocation>._)).MustHaveHappened()
                .Then(A.CallTo(_toBeIntercepted.Fake).Where(x => x.Method.Name == method).MustHaveHappened())
                .Then(A.CallTo(() => _sutAsserter.OnException(A<IInvocation>._, actionException, null))
                    .MustHaveHappened())
                .Then(A.CallTo(() => _sutAsserter.Finally(A<IInvocation>._, null)).MustHaveHappened());
        }

        [TestCaseSource(nameof(TestCases))]
        public async Task WithDontProceed_ReturnsImmediately(string method,
            Func<ITestInterface, Task<object>> callSutMethod, object expectedResult)
        {
            A.CallTo(() => _sutAsserter.BeforeProceed(A<IInvocation>._)).Returns(BeforeProceedResult.DontProceed);

            // ACT
            var result = await callSutMethod(_intercepted);

            // ASSERT
            if (expectedResult != NoResult)
                result.Should().Be(default(int));

            A.CallTo(_toBeIntercepted.Fake).Where(x => x.Method.Name == method).MustNotHaveHappened();
            A.CallTo(() => _sutAsserter.AfterProceed(A<IInvocation>._, A<object>._, A<object>._)).MustNotHaveHappened();
            A.CallTo(() => _sutAsserter.OnException(A<IInvocation>._, A<Exception>._, A<object>._))
                .MustNotHaveHappened();
            A.CallTo(() => _sutAsserter.Finally(A<IInvocation>._, A<object>._)).MustNotHaveHappened();
        }
    }
}