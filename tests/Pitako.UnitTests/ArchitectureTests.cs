using Pitako.Modules.Tenants.Domain;
using Pitako.SharedKernel.Results;
using Xunit;

namespace Pitako.UnitTests;

public sealed class ArchitectureTests
{
    [Fact]
    public void DomainAndSharedKernel_ShouldNotDependOnEntityFrameworkOrNpgsql()
    {
        var assemblies = new[]
        {
            typeof(Result).Assembly,
            typeof(Tenant).Assembly
        };

        foreach (var assembly in assemblies)
        {
            var referenced = assembly.GetReferencedAssemblies().Select(a => a.Name ?? string.Empty).ToList();
            Assert.DoesNotContain(referenced, name => name.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(referenced, name => name.StartsWith("Npgsql", StringComparison.OrdinalIgnoreCase));
        }
    }
}
