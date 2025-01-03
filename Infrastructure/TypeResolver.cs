using Microsoft.Extensions.DependencyInjection;
using Spectre.Console.Cli;

namespace FhirMarshal.Infrastructure;

public sealed class TypeResolver : ITypeResolver, IDisposable
{
    private readonly ServiceProvider _provider;

    public TypeResolver(ServiceProvider provider)
    {
        _provider = provider;
    }

    public object Resolve(Type? type)
    {
        ArgumentNullException.ThrowIfNull(type);
        var service = _provider.GetService(type);
        if (service is null)
        {
            throw new InvalidOperationException(
                $"Cannot resolve service of type '{type.FullName}'"
            );
        }
        return _provider.GetService(type)!;
    }

    public void Dispose()
    {
        _provider.Dispose();
        GC.SuppressFinalize(this);
    }
}
