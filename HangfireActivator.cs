using Hangfire;

namespace Syracuse;

public class HangfireActivator : JobActivator
{
    private readonly IServiceProvider _serviceProvider;

    public HangfireActivator(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public override object ActivateJob(Type type) => _serviceProvider.GetService(type) ?? throw new InvalidOperationException();
}