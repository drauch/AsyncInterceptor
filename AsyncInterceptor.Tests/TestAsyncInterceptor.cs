using System;
using Castle.DynamicProxy;
using FakeItEasy;

namespace AsyncInterceptor.Tests
{
    /// <summary>
    ///     Test implementation allowing us to test the <see cref="AsyncInterceptorBase" /> abstract base class. Offers
    ///     public virtual counterparts to the protected overridable methods, so they can be mocked properly.
    /// </summary>
    // ReSharper disable once ClassWithVirtualMembersNeverInherited.Global
    public class TestAsyncInterceptor : AsyncInterceptorBase
    {
        private readonly IAsyncInterceptorBaseInterface _fake;

        public TestAsyncInterceptor(IAsyncInterceptorBaseInterface fake)
        {
            _fake = fake;
            A.CallTo(() => _fake.BeforeProceed(A<IInvocation>._))
                .ReturnsLazily(x => base.BeforeProceed((IInvocation) x.Arguments[0]));
            A.CallTo(() => _fake.AfterProceed(A<IInvocation>._, A<object>._, A<object>._))
                .ReturnsLazily(x => base.AfterProceed((IInvocation) x.Arguments[0], x.Arguments[1], x.Arguments[2]));
        }

        protected override BeforeProceedResult BeforeProceed(IInvocation invocation)
        {
            return _fake.BeforeProceed(invocation);
        }

        protected override object AfterProceed(IInvocation invocation, object state, object originalReturnValue)
        {
            return _fake.AfterProceed(invocation, state, originalReturnValue);
        }

        protected override void OnException(IInvocation invocation, Exception exception, object state)
        {
            _fake.OnException(invocation, exception, state);
        }

        protected override void Finally(IInvocation invocation, object state)
        {
            _fake.Finally(invocation, state);
        }
    }
}