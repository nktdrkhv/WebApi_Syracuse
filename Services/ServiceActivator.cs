namespace Syracuse;

public static class ServiceActivator
{
    internal static IServiceProvider _serviceProvider = null;
    public static void Configure(IServiceProvider serviceProvider) => _serviceProvider = serviceProvider;
    public static IServiceScope GetScope(IServiceProvider serviceProvider = null)
    {
        var provider = serviceProvider ?? _serviceProvider;
        return provider?
            .GetRequiredService<IServiceScopeFactory>()
            .CreateScope();
    }
    public static AsyncServiceScope? GetAsyncScope(IServiceProvider serviceProvider = null)
    {
        var provider = serviceProvider ?? _serviceProvider;
        return provider?
            .GetRequiredService<IServiceScopeFactory>()
            .CreateAsyncScope();
    }
}