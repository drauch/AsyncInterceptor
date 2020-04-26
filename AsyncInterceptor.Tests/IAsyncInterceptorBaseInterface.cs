using System;
using Castle.DynamicProxy;

namespace AsyncInterceptor.Tests
{
    public interface IAsyncInterceptorBaseInterface
    {
        BeforeProceedResult BeforeProceed(IInvocation invocation);
        object AfterProceed(IInvocation invocation, object state, object originalReturnValue);
        void OnException(IInvocation invocation, Exception exception, object state);
        void Finally(IInvocation invocation, object state);
    }
}