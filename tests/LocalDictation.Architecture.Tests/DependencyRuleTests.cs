using LocalDictation.Application.Pipeline;
using LocalDictation.Domain;
using LocalDictation.Infrastructure;
using NetArchTest.Rules;

namespace LocalDictation.Architecture.Tests;

/// <summary>
/// Enforces the Clean Architecture dependency rule as a build gate: dependencies point inward
/// only. These tests fail the build if a layer takes an illegal reference (design §3.4).
/// </summary>
public class DependencyRuleTests
{
    private const string Application = "LocalDictation.Application";
    private const string Infrastructure = "LocalDictation.Infrastructure";
    private const string Desktop = "LocalDictation.Desktop";

    [Fact]
    public void Domain_has_no_outward_dependencies()
    {
        var result = Types.InAssembly(typeof(Transcript).Assembly)
            .ShouldNot()
            .HaveDependencyOnAny(Application, Infrastructure, Desktop)
            .GetResult();

        Assert.True(result.IsSuccessful, Fail(result));
    }

    [Fact]
    public void Application_does_not_depend_on_Infrastructure_or_Desktop()
    {
        var result = Types.InAssembly(typeof(DictationPipeline).Assembly)
            .ShouldNot()
            .HaveDependencyOnAny(Infrastructure, Desktop)
            .GetResult();

        Assert.True(result.IsSuccessful, Fail(result));
    }

    [Fact]
    public void Infrastructure_does_not_depend_on_Desktop()
    {
        var result = Types.InAssembly(typeof(AppPaths).Assembly)
            .ShouldNot()
            .HaveDependencyOn(Desktop)
            .GetResult();

        Assert.True(result.IsSuccessful, Fail(result));
    }

    private static string Fail(TestResult result) =>
        "Illegal dependencies: " + string.Join(", ", result.FailingTypeNames ?? Array.Empty<string>());
}
