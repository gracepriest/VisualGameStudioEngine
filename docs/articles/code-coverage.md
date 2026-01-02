# Code Coverage Guide

This guide explains how to generate and analyze code coverage reports for Visual Game Studio Engine.

## Overview

Code coverage measures how much of your code is executed during tests. Higher coverage generally indicates better test quality, though 100% coverage doesn't guarantee bug-free code.

## Setup

The test project already includes Coverlet for code coverage:

```xml
<PackageReference Include="coverlet.collector" Version="6.0.0" />
```

## Running Tests with Coverage

### Basic Coverage

```bash
dotnet test --collect:"XPlat Code Coverage"
```

### Using RunSettings

For configured coverage options:

```bash
dotnet test --collect:"XPlat Code Coverage" --settings VisualGameStudio.Tests/coverlet.runsettings
```

### Output Formats

The runsettings file configures multiple output formats:
- **Cobertura** - XML format for CI/CD integration
- **OpenCover** - Detailed XML format
- **LCOV** - Line coverage format

## Configuration

The `coverlet.runsettings` file contains:

```xml
<?xml version="1.0" encoding="utf-8" ?>
<RunSettings>
  <DataCollectionRunSettings>
    <DataCollectors>
      <DataCollector friendlyName="XPlat code coverage">
        <Configuration>
          <Format>cobertura,opencover,lcov</Format>
          <Exclude>[*]*.Tests.*,[*]*.Mock*</Exclude>
          <Include>[BasicLang]*,[VisualGameStudio.Core]*</Include>
          <ExcludeByAttribute>Obsolete,GeneratedCodeAttribute</ExcludeByAttribute>
          <SkipAutoProps>true</SkipAutoProps>
        </Configuration>
      </DataCollector>
    </DataCollectors>
  </DataCollectionRunSettings>
</RunSettings>
```

### Configuration Options

| Option | Description |
|--------|-------------|
| `Format` | Output format(s) |
| `Exclude` | Assemblies/namespaces to exclude |
| `Include` | Assemblies/namespaces to include |
| `ExcludeByAttribute` | Exclude by attribute |
| `SkipAutoProps` | Skip auto-properties |
| `SingleHit` | Count line once regardless of hits |

## Viewing Reports

### Finding Reports

Coverage reports are saved to:
```
TestResults/{guid}/coverage.cobertura.xml
TestResults/{guid}/coverage.opencover.xml
TestResults/{guid}/coverage.info
```

### HTML Reports with ReportGenerator

Install ReportGenerator:
```bash
dotnet tool install -g dotnet-reportgenerator-globaltool
```

Generate HTML report:
```bash
reportgenerator -reports:"TestResults/**/coverage.cobertura.xml" -targetdir:"CoverageReport" -reporttypes:Html
```

Open `CoverageReport/index.html` in a browser.

### Visual Studio Integration

1. Install "Fine Code Coverage" extension
2. Run tests
3. View coverage highlighting in editor

### VS Code Integration

1. Install "Coverage Gutters" extension
2. Run tests with coverage
3. View coverage in gutters

## Interpreting Results

### Coverage Metrics

| Metric | Description |
|--------|-------------|
| Line Coverage | Percentage of lines executed |
| Branch Coverage | Percentage of branches taken |
| Method Coverage | Percentage of methods called |
| Class Coverage | Percentage of classes tested |

### Coverage Goals

| Level | Recommended Minimum |
|-------|-------------------|
| Unit Tests | 80%+ |
| Integration Tests | 60%+ |
| Overall | 70%+ |

### What to Cover

**High Priority:**
- Business logic
- Error handling paths
- Edge cases
- Public API

**Lower Priority:**
- Auto-generated code
- Simple getters/setters
- Framework integration

## Improving Coverage

### Identifying Gaps

1. Generate HTML report
2. Look for red (uncovered) lines
3. Sort by coverage percentage
4. Focus on critical code first

### Common Uncovered Areas

```vb
' Error paths often uncovered
Try
    RiskyOperation()
Catch ex As Exception
    ' Add test that triggers exception
    HandleError(ex)
End Try

' Branch coverage gaps
If condition Then
    ' Covered
Else
    ' Often missed - add test case
End If
```

### Writing Tests for Coverage

```vb
<Test>
Public Sub Method_ErrorPath_HandlesGracefully()
    ' Arrange
    Dim sut = New MyClass()

    ' Act & Assert
    Assert.Throws(Of InvalidOperationException)(
        Sub() sut.MethodThatThrows())
End Sub
```

## CI/CD Integration

### GitHub Actions

```yaml
- name: Test with Coverage
  run: dotnet test --collect:"XPlat Code Coverage"

- name: Upload Coverage
  uses: codecov/codecov-action@v3
  with:
    files: ./TestResults/**/coverage.cobertura.xml
```

### Azure DevOps

```yaml
- task: DotNetCoreCLI@2
  inputs:
    command: test
    arguments: '--collect:"XPlat Code Coverage"'

- task: PublishCodeCoverageResults@1
  inputs:
    codeCoverageTool: Cobertura
    summaryFileLocation: '$(Agent.TempDirectory)/**/coverage.cobertura.xml'
```

## Best Practices

### 1. Set Coverage Thresholds

```bash
dotnet test /p:Threshold=80 /p:ThresholdType=line
```

Fails if coverage drops below 80%.

### 2. Track Coverage Over Time

Monitor coverage in CI/CD to detect regressions.

### 3. Focus on Meaningful Coverage

Don't write tests just for coverage numbers. Test behavior and edge cases.

### 4. Exclude Generated Code

Use attributes or configuration to exclude:
- Designer-generated code
- Auto-generated wrappers
- Migrations

### 5. Review Uncovered Code

Before excluding code, ask:
- Should this be tested?
- Is this dead code?
- Is the design testable?

## Troubleshooting

### No Coverage Data

1. Ensure coverlet package is installed
2. Check for build errors
3. Verify assemblies are not excluded

### Inaccurate Coverage

1. Clean and rebuild
2. Delete old TestResults
3. Check exclusion patterns

### Missing Lines

1. Enable debug symbols
2. Check optimization settings
3. Verify source file paths

## Command Reference

```bash
# Basic coverage
dotnet test --collect:"XPlat Code Coverage"

# With settings
dotnet test --settings coverlet.runsettings

# Specific project
dotnet test MyProject.Tests --collect:"XPlat Code Coverage"

# With threshold
dotnet test /p:Threshold=80

# Generate report
reportgenerator -reports:"**/coverage.cobertura.xml" -targetdir:"CoverageReport"
```

## Next Steps

- [Getting Started](getting-started.md) - Project setup
- [IDE User Guide](ide-guide.md) - Using the IDE
