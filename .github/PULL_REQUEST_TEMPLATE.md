## Description

<!-- Provide a clear and concise description of the changes in this PR. -->

## Type of Change

<!-- Check the relevant option(s): -->

- [ ] Bug fix (non-breaking change that fixes an issue)
- [ ] New feature (non-breaking change that adds functionality)
- [ ] Breaking change (fix or feature that would cause existing functionality to not work as expected)
- [ ] Documentation update
- [ ] Performance improvement
- [ ] Code refactoring

## Testing Done

<!-- Describe the tests you ran and how to reproduce them. -->

- [ ] `dotnet build` completes with 0 warnings and 0 errors
- [ ] `dotnet run -- list` works correctly
- [ ] `dotnet run -- info "<printer>"` works correctly
- [ ] `dotnet run -- "<file>" -printer "XPS" -output "test.xps"` works correctly
- [ ] `dotnet publish -c Release -r win-x64 --self-contained false` succeeds
- [ ] Manual testing: <!-- describe what you tested -->

## Breaking Changes

<!-- If this PR introduces breaking changes, describe them here. Otherwise, write "None". -->

## Checklist

- [ ] My code follows the project's coding style and conventions
- [ ] I have added/updated tests that prove my fix is effective or that my feature works
- [ ] New and existing unit tests pass locally with my changes
- [ ] I have updated the documentation accordingly
- [ ] My changes generate no new warnings or errors
