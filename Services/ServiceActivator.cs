namespace Syracuse;

public static class ServiceActivator
{
    private static IServiceProvider _serviceProvider;

    public static void Configure(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public static IServiceScope GetScope(IServiceProvider serviceProvider = null!)
    {
        IServiceProvider? provider = serviceProvider ?? _serviceProvider;
        return provider?
            .GetRequiredService<IServiceScopeFactory>()
            .CreateScope();
    }

    public static AsyncServiceScope? GetAsyncScope(IServiceProvider serviceProvider = null!)
    {
        IServiceProvider? provider = serviceProvider ?? _serviceProvider;
        return provider?
            .GetRequiredService<IServiceScopeFactory>()
            .CreateAsyncScope();
    }
}