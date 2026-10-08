using HRSystem.Domain;

namespace HRSystem.UnitTests;

public class ArchitectureBoundaryTests
{
    [Fact]
    public void Domain_DoesNotReferenceInfrastructureOrWebFrameworks()
    {
        var references = typeof(DomainAssemblyMarker).Assembly
            .GetReferencedAssemblies()
            .Select(x => x.Name ?? string.Empty)
            .ToArray();

        Assert.DoesNotContain(references, x => x.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal));
        Assert.DoesNotContain(references, x => x.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal));
        Assert.DoesNotContain(references, x => x.StartsWith("Microsoft.FluentUI", StringComparison.Ordinal));
        Assert.DoesNotContain(references, x => x.StartsWith("HRSystem.Infrastructure", StringComparison.Ordinal));
        Assert.DoesNotContain(references, x => x.StartsWith("HRSystem.Web", StringComparison.Ordinal));
    }
}
