using Castle.DynamicProxy;

namespace AsyncInterceptor
{
    /// <summary>
    ///     Reports whether to actually call <see cref="IInvocation.Proceed" /> and an optional state which is handed to
    ///     the other callback methods (e.g., <see cref="AsyncInterceptorBase.AfterProceed" />).
    /// </summary>
    public class BeforeProceedResult
    {
        public static readonly BeforeProceedResult Proceed = new BeforeProceedResult(true);
        public static readonly BeforeProceedResult DontProceed = new BeforeProceedResult(false);

        public BeforeProceedResult(bool shouldProceed, object? state = null)
        {
            ShouldProceed = shouldProceed;
            State = state;
        }

        public bool ShouldProceed { get; }
        public object? State { get; }
    }
}