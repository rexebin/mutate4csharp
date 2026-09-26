namespace Microsoft.Mutate4CSharp.Tests;

using Microsoft.Mutate4CSharp.Project;

/// <summary>
/// New unit tests (no mutate4java oracle) for the DD2/DD3 <see cref="ModuleResolver"/> that replaces
/// mutate4java's nearest-<c>pom.xml</c> <c>ModuleRootFinder</c>. Covers <c>&lt;Project&gt;</c>
/// derivation, <c>.Tests</c> vs <c>.UnitTests</c> selection, <c>ProjectReference</c> validation
/// (a same-named test project for a different production project is rejected), the tie-break order,
/// marker-based test-project discovery (tiers 2/3) with its ambiguity signal, and the typed not-found
/// signals.
/// </summary>
public sealed class ModuleResolverTests : IDisposable
{
    private const string IsTestProjectGroup = "<PropertyGroup><IsTestProject>true</IsTestProject></PropertyGroup>";

    private const string TestSdkItemGroup =
        @"<ItemGroup><PackageReference Include=""Microsoft.NET.Test.Sdk"" Version=""17.8.0"" /></ItemGroup>";

    // From tests/<Name>/ to src/Foo/Foo.csproj.
    private const string FooReference = @"<ItemGroup><ProjectReference Include=""..\..\src\Foo\Foo.csproj"" /></ItemGroup>";

    private readonly string _root;

    /// <summary>
    /// Initializes a new instance of the <see cref="ModuleResolverTests"/> class, creating a per-test
    /// temporary workspace root.
    /// </summary>
    public ModuleResolverTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "m4cs-module-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    /// <summary>
    /// Deletes the per-test temporary workspace root.
    /// </summary>
    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }

        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// A sibling <c>&lt;Project&gt;.Tests.csproj</c> that references the derived project resolves,
    /// deriving <c>&lt;Project&gt;</c> from the nearest ancestor <c>.csproj</c>.
    /// </summary>
    [Fact]
    [Trait("type", "UnitTests")]
    public void ResolvesProjectAndTestProject()
    {
        WriteProject("Foo", "Foo.csproj");
        string testProject = WriteProject("Foo.Tests", "Foo.Tests.csproj", "../Foo/Foo.csproj");
        string source = WriteSource("Foo", "Sample.cs");

        ModuleResolution result = Resolve(source);

        result.Status.Should().Be(ModuleResolutionStatus.Resolved);
        result.ProjectName.Should().Be("Foo");
        result.ProjectFile.Should().Be(FullPath("Foo", "Foo.csproj"));
        result.TestProjectFile.Should().Be(testProject);
    }

    /// <summary>
    /// When both suffixes reference the project from equally ranked (sibling) locations,
    /// <c>.Tests</c> is preferred over <c>.UnitTests</c>.
    /// </summary>
    [Fact]
    [Trait("type", "UnitTests")]
    public void PrefersTestsOverUnitTestsAsFinalTieBreak()
    {
        WriteProject("Foo", "Foo.csproj");
        string tests = WriteProject("Foo.Tests", "Foo.Tests.csproj", "../Foo/Foo.csproj");
        WriteProject("Foo.UnitTests", "Foo.UnitTests.csproj", "../Foo/Foo.csproj");
        string source = WriteSource("Foo", "Sample.cs");

        Resolve(source).TestProjectFile.Should().Be(tests);
    }

    /// <summary>
    /// The <c>.UnitTests</c> suffix resolves when it is the only referencing test project.
    /// </summary>
    [Fact]
    [Trait("type", "UnitTests")]
    public void ResolvesUnitTestsWhenOnlySuffixPresent()
    {
        WriteProject("Foo", "Foo.csproj");
        string unitTests = WriteProject("Foo.UnitTests", "Foo.UnitTests.csproj", "../Foo/Foo.csproj");
        string source = WriteSource("Foo", "Sample.cs");

        ModuleResolution result = Resolve(source);

        result.Status.Should().Be(ModuleResolutionStatus.Resolved);
        result.TestProjectFile.Should().Be(unitTests);
    }

    /// <summary>
    /// A test project whose name matches but whose <c>ProjectReference</c> closure points at a
    /// different production project is rejected (mono-repo mapping validation, risk R-A/R-B).
    /// </summary>
    [Fact]
    [Trait("type", "UnitTests")]
    public void RejectsSameNamedTestProjectForDifferentProductionProject()
    {
        WriteProject("Foo", "Foo.csproj");
        WriteProject("Bar", "Bar.csproj");
        WriteProject("Foo.Tests", "Foo.Tests.csproj", "../Bar/Bar.csproj");
        string source = WriteSource("Foo", "Sample.cs");

        ModuleResolution result = Resolve(source);

        result.Status.Should().Be(ModuleResolutionStatus.NoTestProject);
        result.ProjectName.Should().Be("Foo");
    }

    /// <summary>
    /// A test project referencing the project transitively (through an intermediate project) is
    /// accepted.
    /// </summary>
    [Fact]
    [Trait("type", "UnitTests")]
    public void AcceptsTransitiveProjectReference()
    {
        WriteProject("Foo", "Foo.csproj");
        WriteProject("Mid", "Mid.csproj", "../Foo/Foo.csproj");
        string tests = WriteProject("Foo.Tests", "Foo.Tests.csproj", "../Mid/Mid.csproj");
        string source = WriteSource("Foo", "Sample.cs");

        ModuleResolution result = Resolve(source);

        result.Status.Should().Be(ModuleResolutionStatus.Resolved);
        result.TestProjectFile.Should().Be(tests);
    }

    /// <summary>
    /// A sibling test project outranks an equally named one under a <c>tests/</c> directory.
    /// </summary>
    [Fact]
    [Trait("type", "UnitTests")]
    public void PrefersSiblingOverTestsDirectory()
    {
        WriteProject("src/Foo", "Foo.csproj");
        string sibling = WriteProject("src/Foo.Tests", "Foo.Tests.csproj", "../Foo/Foo.csproj");
        WriteProject("tests/Foo.Tests", "Foo.Tests.csproj", "../../src/Foo/Foo.csproj");
        string source = WriteSource("src/Foo", "Sample.cs");

        Resolve(source).TestProjectFile.Should().Be(sibling);
    }

    /// <summary>
    /// No owning <c>.csproj</c> above the target yields the <see cref="ModuleResolutionStatus.NoOwningProject"/>
    /// signal.
    /// </summary>
    [Fact]
    [Trait("type", "UnitTests")]
    public void SignalsNoOwningProjectWhenNoCsprojFound()
    {
        string source = WriteSource("Foo", "Sample.cs");

        Resolve(source).Status.Should().Be(ModuleResolutionStatus.NoOwningProject);
    }

    /// <summary>
    /// An owning project with no referencing test project yields the
    /// <see cref="ModuleResolutionStatus.NoTestProject"/> signal, carrying the derived project.
    /// </summary>
    [Fact]
    [Trait("type", "UnitTests")]
    public void SignalsNoTestProjectWhenNoneReferencesProject()
    {
        WriteProject("Foo", "Foo.csproj");
        string source = WriteSource("Foo", "Sample.cs");

        ModuleResolution result = Resolve(source);

        result.Status.Should().Be(ModuleResolutionStatus.NoTestProject);
        result.ProjectFile.Should().Be(FullPath("Foo", "Foo.csproj"));
    }

    /// <summary>
    /// A project marked <c>IsTestProject</c> that references the owner resolves even without the
    /// <c>.Tests</c>/<c>.UnitTests</c> name (tier 3).
    /// </summary>
    [Fact]
    [Trait("type", "UnitTests")]
    public void DiscoversUnconventionallyNamedProjectMarkedIsTestProject()
    {
        WriteProject("src/Foo", "Foo.csproj");
        string test = WriteRawProject("tests/Acceptance", "Acceptance.csproj", IsTestProjectGroup + FooReference);
        string source = WriteSource("src/Foo", "Sample.cs");

        ModuleResolution result = Resolve(source);

        result.Status.Should().Be(ModuleResolutionStatus.Resolved);
        result.TestProjectFile.Should().Be(test);
    }

    /// <summary>A <c>Microsoft.NET.Test.Sdk</c> package reference marks a test project.</summary>
    [Fact]
    [Trait("type", "UnitTests")]
    public void DiscoversProjectReferencingTestSdkPackage()
    {
        WriteProject("src/Foo", "Foo.csproj");
        string test = WriteRawProject("tests/Specs", "Specs.csproj", TestSdkItemGroup + FooReference);
        string source = WriteSource("src/Foo", "Sample.cs");

        Resolve(source).TestProjectFile.Should().Be(test);
    }

    /// <summary>
    /// The marker may come from an explicitly imported shared <c>.targets</c> file (mutate4csharp's
    /// own layout).
    /// </summary>
    [Fact]
    [Trait("type", "UnitTests")]
    public void DiscoversMarkerFromExplicitlyImportedFile()
    {
        File.WriteAllText(Path.Combine(_root, "Tests.Common.targets"), $"<Project>{IsTestProjectGroup}</Project>");
        WriteProject("src/Foo", "Foo.csproj");
        string test = WriteRawProject(
            "tests/BlackBox", "BlackBox.csproj", FooReference + @"<Import Project=""..\..\Tests.Common.targets"" />");
        string source = WriteSource("src/Foo", "Sample.cs");

        Resolve(source).TestProjectFile.Should().Be(test);
    }

    /// <summary>
    /// <c>$(MSBuildThisFileDirectory)</c> in an import resolves against the IMPORTING file's directory.
    /// </summary>
    [Fact]
    [Trait("type", "UnitTests")]
    public void DiscoversMarkerFromImportUsingThisFileDirectoryOfImportingFile()
    {
        string shared = Path.Combine(_root, "tests", "shared");
        Directory.CreateDirectory(shared);
        File.WriteAllText(Path.Combine(shared, "Test.props"), $"<Project>{TestSdkItemGroup}</Project>");
        File.WriteAllText(
            Path.Combine(_root, "tests", "Directory.Build.props"),
            @"<Project><Import Project=""$(MSBuildThisFileDirectory)shared\Test.props"" /></Project>");
        WriteProject("src/Foo", "Foo.csproj");
        string test = WriteRawProject("tests/Scenarios", "Scenarios.csproj", FooReference);
        string source = WriteSource("src/Foo", "Sample.cs");

        Resolve(source).TestProjectFile.Should().Be(test);
    }

    /// <summary>The marker may come from a <c>Directory.Build.props</c> above the project.</summary>
    [Fact]
    [Trait("type", "UnitTests")]
    public void DiscoversMarkerFromDirectoryBuildProps()
    {
        Directory.CreateDirectory(Path.Combine(_root, "tests"));
        File.WriteAllText(Path.Combine(_root, "tests", "Directory.Build.props"), $"<Project>{IsTestProjectGroup}</Project>");
        WriteProject("src/Foo", "Foo.csproj");
        string test = WriteRawProject("tests/Scenarios", "Scenarios.csproj", FooReference);
        string source = WriteSource("src/Foo", "Sample.cs");

        Resolve(source).TestProjectFile.Should().Be(test);
    }

    /// <summary>The marker may come from a <c>Directory.Build.targets</c> above the project.</summary>
    [Fact]
    [Trait("type", "UnitTests")]
    public void DiscoversMarkerFromDirectoryBuildTargets()
    {
        Directory.CreateDirectory(Path.Combine(_root, "tests"));
        File.WriteAllText(Path.Combine(_root, "tests", "Directory.Build.targets"), $"<Project>{TestSdkItemGroup}</Project>");
        WriteProject("src/Foo", "Foo.csproj");
        string test = WriteRawProject("tests/Scenarios", "Scenarios.csproj", FooReference);
        string source = WriteSource("src/Foo", "Sample.cs");

        Resolve(source).TestProjectFile.Should().Be(test);
    }

    /// <summary>An app/host project that references the owner is not a test project.</summary>
    [Fact]
    [Trait("type", "UnitTests")]
    public void IgnoresReferencingProjectWithoutTestMarker()
    {
        WriteProject("src/Foo", "Foo.csproj");
        WriteProject("src/Foo.Api", "Foo.Api.csproj", "../Foo/Foo.csproj");
        string source = WriteSource("src/Foo", "Sample.cs");

        Resolve(source).Status.Should().Be(ModuleResolutionStatus.NoTestProject);
    }

    /// <summary>A conditioned marker cannot be evaluated statically and is ignored (fail-safe).</summary>
    [Fact]
    [Trait("type", "UnitTests")]
    public void IgnoresConditionedMarker()
    {
        const string conditionedMarker =
            @"<PropertyGroup Condition=""'$(CI)'=='true'""><IsTestProject>true</IsTestProject></PropertyGroup>";
        WriteProject("src/Foo", "Foo.csproj");
        WriteRawProject("tests/Specs", "Specs.csproj", conditionedMarker + FooReference);
        string source = WriteSource("src/Foo", "Sample.cs");

        Resolve(source).Status.Should().Be(ModuleResolutionStatus.NoTestProject);
    }

    /// <summary>An import containing an unsupported <c>$(...)</c> token is skipped (fail-safe).</summary>
    [Fact]
    [Trait("type", "UnitTests")]
    public void SkipsImportWithUnsupportedPropertyToken()
    {
        File.WriteAllText(Path.Combine(_root, "Tests.Common.targets"), $"<Project>{IsTestProjectGroup}</Project>");
        WriteProject("src/Foo", "Foo.csproj");
        WriteRawProject("tests/Specs", "Specs.csproj", FooReference + @"<Import Project=""$(RepoRoot)Tests.Common.targets"" />");
        string source = WriteSource("src/Foo", "Sample.cs");

        Resolve(source).Status.Should().Be(ModuleResolutionStatus.NoTestProject);
    }

    /// <summary>A malformed candidate project is treated as unmarked rather than failing resolution.</summary>
    [Fact]
    [Trait("type", "UnitTests")]
    public void TreatsMalformedImportedFileAsUnmarked()
    {
        File.WriteAllText(Path.Combine(_root, "Broken.targets"), "<Project><PropertyGroup>");
        WriteProject("src/Foo", "Foo.csproj");
        WriteRawProject("tests/Specs", "Specs.csproj", FooReference + @"<Import Project=""..\..\Broken.targets"" />");
        string source = WriteSource("src/Foo", "Sample.cs");

        Resolve(source).Status.Should().Be(ModuleResolutionStatus.NoTestProject);
    }

    /// <summary>An import that points above the workspace root is not followed.</summary>
    [Fact]
    [Trait("type", "UnitTests")]
    public void IgnoresImportedFileAboveWorkspaceRoot()
    {
        string outside = Path.Combine(Path.GetDirectoryName(_root)!, Path.GetFileName(_root) + "-outside.targets");
        File.WriteAllText(outside, $"<Project>{IsTestProjectGroup}</Project>");
        try
        {
            WriteProject("src/Foo", "Foo.csproj");
            WriteRawProject(
                "tests/Specs", "Specs.csproj", FooReference + $@"<Import Project=""..\..\..\{Path.GetFileName(outside)}"" />");
            string source = WriteSource("src/Foo", "Sample.cs");

            Resolve(source).Status.Should().Be(ModuleResolutionStatus.NoTestProject);
        }
        finally
        {
            File.Delete(outside);
        }
    }

    /// <summary>Mutually importing files terminate, and a marker in the cycle is still found.</summary>
    [Fact]
    [Trait("type", "UnitTests")]
    public void TerminatesOnImportCycleAndStillFindsMarker()
    {
        File.WriteAllText(Path.Combine(_root, "A.targets"), @"<Project><Import Project=""B.targets"" /></Project>");
        File.WriteAllText(
            Path.Combine(_root, "B.targets"), $@"<Project><Import Project=""A.targets"" />{IsTestProjectGroup}</Project>");
        WriteProject("src/Foo", "Foo.csproj");
        string test = WriteRawProject("tests/Specs", "Specs.csproj", FooReference + @"<Import Project=""..\..\A.targets"" />");
        string source = WriteSource("src/Foo", "Sample.cs");

        Resolve(source).TestProjectFile.Should().Be(test);
    }

    /// <summary>The naming convention (tier 1) wins over a discovered marked project.</summary>
    [Fact]
    [Trait("type", "UnitTests")]
    public void PrefersNamingConventionOverDiscoveredTestProject()
    {
        WriteProject("src/Foo", "Foo.csproj");
        WriteRawProject("tests/Foo.BlackBoxTests", "Foo.BlackBoxTests.csproj", IsTestProjectGroup + FooReference);
        string conventional = WriteProject("tests/Foo.Tests", "Foo.Tests.csproj", "../../src/Foo/Foo.csproj");
        string source = WriteSource("src/Foo", "Sample.cs");

        Resolve(source).TestProjectFile.Should().Be(conventional);
    }

    /// <summary>A marked <c>&lt;Project&gt;.*</c> project (tier 2) wins over other marked projects.</summary>
    [Fact]
    [Trait("type", "UnitTests")]
    public void PrefersProjectNamePrefixOverOtherTestProjects()
    {
        WriteProject("src/Foo", "Foo.csproj");
        WriteRawProject("tests/Acceptance", "Acceptance.csproj", IsTestProjectGroup + FooReference);
        string prefixed = WriteRawProject(
            "tests/Foo.BlackBoxTests", "Foo.BlackBoxTests.csproj", IsTestProjectGroup + FooReference);
        string source = WriteSource("src/Foo", "Sample.cs");

        Resolve(source).TestProjectFile.Should().Be(prefixed);
    }

    /// <summary>
    /// Two marked <c>&lt;Project&gt;.*</c> projects tie at tier 2: the typed ambiguity signal carries both,
    /// ordinal-sorted — never a guess.
    /// </summary>
    [Fact]
    [Trait("type", "UnitTests")]
    public void SignalsAmbiguityWhenMultiplePrefixedTestProjectsQualify()
    {
        WriteProject("src/Foo", "Foo.csproj");
        string specs = WriteRawProject("tests/Foo.Specs", "Foo.Specs.csproj", IsTestProjectGroup + FooReference);
        string blackBox = WriteRawProject(
            "tests/Foo.BlackBoxTests", "Foo.BlackBoxTests.csproj", IsTestProjectGroup + FooReference);
        string source = WriteSource("src/Foo", "Sample.cs");

        ModuleResolution result = Resolve(source);

        result.Status.Should().Be(ModuleResolutionStatus.AmbiguousTestProject);
        result.TestProjectFile.Should().BeNull();
        result.AmbiguousTestProjectFiles.Should().Equal(
            new[] { specs, blackBox }.OrderBy(path => path, StringComparer.Ordinal));
    }

    /// <summary>Two marked projects without the prefix tie at tier 3 and signal ambiguity.</summary>
    [Fact]
    [Trait("type", "UnitTests")]
    public void SignalsAmbiguityWhenMultipleUnprefixedTestProjectsQualify()
    {
        WriteProject("src/Foo", "Foo.csproj");
        string acceptance = WriteRawProject("tests/Acceptance", "Acceptance.csproj", IsTestProjectGroup + FooReference);
        string scenarios = WriteRawProject("tests/Scenarios", "Scenarios.csproj", TestSdkItemGroup + FooReference);
        string source = WriteSource("src/Foo", "Sample.cs");

        ModuleResolution result = Resolve(source);

        result.Status.Should().Be(ModuleResolutionStatus.AmbiguousTestProject);
        result.AmbiguousTestProjectFiles.Should().Equal(
            new[] { acceptance, scenarios }.OrderBy(path => path, StringComparer.Ordinal));
    }

    private string WriteRawProject(string relativeDir, string fileName, string innerXml)
    {
        string directory = Path.Combine(_root, relativeDir.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, fileName);
        File.WriteAllText(path, $"<Project Sdk=\"Microsoft.NET.Sdk\">{innerXml}</Project>");
        return path;
    }

    private ModuleResolution Resolve(string sourceFile)
    {
        return new ModuleResolver(_root).Resolve(sourceFile);
    }

    private string FullPath(params string[] segments)
    {
        return Path.GetFullPath(Path.Combine(_root, Path.Combine(segments)));
    }

    private string WriteProject(string relativeDir, string fileName, params string[] projectReferences)
    {
        string directory = Path.Combine(_root, relativeDir.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, fileName);
        string references = string.Concat(
            projectReferences.Select(reference => $"    <ProjectReference Include=\"{reference}\" />\n"));
        string xml = $"<Project Sdk=\"Microsoft.NET.Sdk\">\n  <ItemGroup>\n{references}  </ItemGroup>\n</Project>\n";
        File.WriteAllText(path, xml);
        return path;
    }

    private string WriteSource(string relativeDir, string fileName)
    {
        string directory = Path.Combine(_root, relativeDir.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, fileName);
        File.WriteAllText(path, "class Sample {}");
        return path;
    }
}
