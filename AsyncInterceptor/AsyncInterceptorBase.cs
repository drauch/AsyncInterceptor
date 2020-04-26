using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading.Tasks;
using Castle.DynamicProxy;
using JetBrains.Annotations;

namespace AsyncInterceptor
{
    /// <summary>
    ///   Async-aware implementation of an <see cref="IInterceptor"/>. Instead of a universal <see cref="Intercept"/>,
    ///   this class allows you to override four callbacks to perform your interception code (namely,
    ///   <see cref="BeforeProceed"/>, <see cref="AfterProceed"/>, <see cref="OnException"/> and
    ///   <see cref="Finally"/>). In order to perform properly this class needs to add its own continuation to the
    ///   returned awaitable, therefore you cannot adjust the <see cref="IInvocation"/>'s
    ///   <see cref="IInvocation.ReturnValue"/> property - instead you can adjust the return value by overriding
    ///   <see cref="AfterProceed"/>.
    /// </summary>
    public abstract class AsyncInterceptorBase : IInterceptor
    {
        private readonly ConcurrentDictionary<Type, Delegate> _delegateCache = new ConcurrentDictionary<Type, Delegate>();

        public void Intercept(IInvocation invocation)
        {
            var returnType = invocation.Method.ReturnType;
            var isSupportedAwaitable = IsSupportedAwaitable(returnType);

            var beforeProceedResult = BeforeProceed(invocation);
            if (!beforeProceedResult.ShouldProceed)
            {
                if (returnType != typeof(void))
                    invocation.ReturnValue = GetDefaultValue(returnType);
                return;
            }

            var state = beforeProceedResult.State;

            if (!isSupportedAwaitable)
                ProceedSynchronously(invocation, returnType, state);
            else
                invocation.ReturnValue = ProceedAsynchronously(invocation, returnType, state);
        }

        /// <summary>
        ///   Override this method in order to execute code before <see cref="IInvocation.Proceed"/> is called.
        /// </summary>
        /// <param name="invocation">The intercepted invocation.</param>
        /// <returns>See <see cref="BeforeProceedResult"/>.</returns>
        protected virtual BeforeProceedResult BeforeProceed(IInvocation invocation)
        {
            return BeforeProceedResult.Proceed;
        }

        /// <summary>
        ///   Override this method in order to execute code after <see cref="IInvocation.Proceed"/> successfully completed.
        /// </summary>
        /// <param name="invocation">The intercepted invocation.</param>
        /// <param name="state">The state returned by <see cref="BeforeProceed"/>.</param>
        /// <param name="originalReturnValue">The return value of the intercepted method, null for void methods.</param>
        /// <returns>The return value you want the method to return to the caller. Ignored for void methods.</returns>
        protected virtual object? AfterProceed(IInvocation invocation, object? state, object? originalReturnValue)
        {
            return originalReturnValue;
        }

        /// <summary>
        ///   Override this method in order to execute code when <see cref="IInvocation.Proceed"/> faulted or has been canceled.
        /// </summary>
        /// <param name="invocation">The intercepted invocation.</param>
        /// <param name="exception">The exception.</param>
        /// <param name="state">The state returned by <see cref="BeforeProceed"/>.</param>
        protected virtual void OnException(IInvocation invocation, Exception exception, object? state)
        {
        }

        /// <summary>
        ///   Override this method in order to execute code when <see cref="IInvocation.Proceed"/> completed (regardless of the completed task's status).
        /// </summary>
        /// <param name="invocation">The intercepted invocation.</param>
        /// <param name="state">The state returned by <see cref="BeforeProceed"/>.</param>
        protected virtual void Finally(IInvocation invocation, object? state)
        {
        }

        /// <summary>
        ///   Returns whether the <paramref name="type"/> is a supported awaitable type of
        ///    <see cref="AsyncInterceptorBase"/>. Unsupported awaitable types are simply continued synchronously.
        /// </summary>
        private bool IsSupportedAwaitable(Type type)
        {
            return type == typeof(Task)
                   || type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Task<>)
                   || type == typeof(ValueTask)
                   || type.IsGenericType && type.GetGenericTypeDefinition() == typeof(ValueTask<>);
        }

        /// <summary>
        ///   Returns the default value of the given <paramref name="returnType"/>. Special handling for
        ///   <see cref="Task"/> and <see cref="ValueTask"/>: a completed (value-) task is returned, with the
        ///   default value as its result.
        /// </summary>
        private object? GetDefaultValue(Type returnType)
        {
            if (returnType == typeof(Task))
            {
                return Task.CompletedTask;
            }

            else if (returnType.IsGenericType && returnType.GetGenericTypeDefinition() == typeof(Task<>))
            {
                return typeof(AsyncInterceptorBase).GetMethod(nameof(GetDefaultTask),
                        BindingFlags.Instance | BindingFlags.NonPublic)!
                    .MakeGenericMethod(returnType.GetGenericArguments().Single()).Invoke(this, new object[0]);
            }

            else if (returnType == typeof(ValueTask))
            {
                return new ValueTask();
            }

            else if (returnType.IsGenericType && returnType.GetGenericTypeDefinition() == typeof(ValueTask<>))
            {
                return typeof(AsyncInterceptorBase).GetMethod(nameof(GetDefaultValueTask),
                        BindingFlags.Instance | BindingFlags.NonPublic)!
                    .MakeGenericMethod(returnType.GetGenericArguments().Single()).Invoke(this, new object[0]);
            }

            else if (returnType.IsValueType)
            {
                return Activator.CreateInstance(returnType);
            }
            else
            {
                return null;
            }
        }

        private Task<T> GetDefaultTask<T>() => Task.FromResult(default(T));
        private ValueTask<T> GetDefaultValueTask<T>() => new ValueTask<T>(default(T));

        private void ProceedSynchronously(IInvocation invocation, Type returnType, object? state)
        {
            try
            {
                invocation.Proceed();
                if (returnType == typeof(void))
                    AfterProceed(invocation, state, null);
                else
                    invocation.ReturnValue = AfterProceed(invocation, state, invocation.ReturnValue);
            }
            catch (Exception ex)
            {
                OnException(invocation, ex, state);
                throw;
            }
            finally
            {
                Finally(invocation, state);
            }
        }

        private object ProceedAsynchronously(IInvocation invocation, Type returnType, object? state)
        {
            if (returnType == typeof(Task))
            {
                return ProceedAsynchronouslyForTask(invocation, state);
            }
            else if (returnType == typeof(ValueTask))
            {
                return ProceedAsynchronouslyForValueTask(invocation, state);
            }
            else
            {
                var proceedAsynchronouslyDelegate = GetProceedAsynchronouslyForReturnTypeDelegate(returnType);
                return proceedAsynchronouslyDelegate.DynamicInvoke(invocation, state);
            }
        }

        private Delegate GetProceedAsynchronouslyForReturnTypeDelegate(Type returnType)
        {
            Delegate? proceedAsynchronouslyDelegate = null;

            if (returnType.IsGenericType && returnType.GetGenericTypeDefinition() == typeof(Task<>))
            {
                proceedAsynchronouslyDelegate = _delegateCache.GetOrAdd(returnType, _ =>
                {
                    var type = Expression.GetFuncType(typeof(IInvocation), typeof(object), returnType);
                    // ReSharper disable once PossibleNullReferenceException - we're sure the method exists.
                    var method = typeof(AsyncInterceptorBase).GetMethod("ProceedAsynchronouslyForTaskT",
                            BindingFlags.Instance | BindingFlags.NonPublic)!
                        .MakeGenericMethod(returnType.GetGenericArguments().Single());
                    return Delegate.CreateDelegate(type, this, method);
                });
            }
            else if (returnType.IsGenericType && returnType.GetGenericTypeDefinition() == typeof(ValueTask<>))
            {
                proceedAsynchronouslyDelegate = _delegateCache.GetOrAdd(returnType, _ =>
                {
                    var type = Expression.GetFuncType(typeof(IInvocation), typeof(object), returnType);
                    // ReSharper disable once PossibleNullReferenceException - we're sure the method exists.
                    var method = typeof(AsyncInterceptorBase).GetMethod("ProceedAsynchronouslyForValueTaskT",
                            BindingFlags.Instance | BindingFlags.NonPublic)!
                        .MakeGenericMethod(returnType.GetGenericArguments().Single());
                    return Delegate.CreateDelegate(type, this, method);
                });
            }

            Trace.Assert(proceedAsynchronouslyDelegate != null, "IsSupportedAwaitable returned true although we don't support it");
            return proceedAsynchronouslyDelegate!;
        }

        private async Task ProceedAsynchronouslyForTask(IInvocation invocation, object? state)
        {
            try
            {
                invocation.Proceed();
                var task = (Task)invocation.ReturnValue;
                await task;
                AfterProceed(invocation, state, null);
            }
            catch (Exception ex)
            {
                OnException(invocation, ex, state);
                throw;
            }
            finally
            {
                Finally(invocation, state);
            }
        }

        [UsedImplicitly]
        private async Task<T> ProceedAsynchronouslyForTaskT<T>(IInvocation invocation, object? state)
        {
            try
            {
                invocation.Proceed();
                var task = (Task<T>)invocation.ReturnValue;
                var originalResult = await task;
                return (T) AfterProceed(invocation, state, originalResult) ?? originalResult;
            }
            catch (Exception ex)
            {
                OnException(invocation, ex, state);
                throw;
            }
            finally
            {
                Finally(invocation, state);
            }
        }

        private async ValueTask ProceedAsynchronouslyForValueTask(IInvocation invocation, object? state)
        {
            try
            {
                invocation.Proceed();
                var valueTask = (ValueTask)invocation.ReturnValue;
                await valueTask;
                AfterProceed(invocation, state, null);
            }
            catch (Exception ex)
            {
                OnException(invocation, ex, state);
                throw;
            }
            finally
            {
                Finally(invocation, state);
            }
        }

        [UsedImplicitly]
        private async ValueTask<T> ProceedAsynchronouslyForValueTaskT<T>(IInvocation invocation, object? state)
        {
            try
            {
                invocation.Proceed();
                var valueTask = (ValueTask<T>)invocation.ReturnValue;
                var originalResult = await valueTask;
                return (T) AfterProceed(invocation, state, originalResult) ?? originalResult;
            }
            catch (Exception ex)
            {
                OnException(invocation, ex, state);
                throw;
            }
            finally
            {
                Finally(invocation, state);
            }
        }
    }
}
