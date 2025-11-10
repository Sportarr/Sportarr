# Sportarr Testing Guide

This directory contains all test suites for the Sportarr application.

## Test Structure

```
tests/
├── Sportarr.Api.Tests/         # Backend C# tests
│   ├── Services/                # Service unit tests
│   ├── Integration/             # API integration tests
│   └── Sportarr.Api.Tests.csproj
└── README.md                    # This file

frontend/
└── src/
    ├── components/__tests__/    # Component tests
    ├── pages/__tests__/         # Page tests
    ├── hooks/__tests__/         # Hook tests
    └── test/                    # Test utilities and setup
```

## Running Tests

### Backend Tests (.NET)

```bash
# Run all backend tests
dotnet test tests/Sportarr.Api.Tests/Sportarr.Api.Tests.csproj

# Run with verbose output
dotnet test tests/Sportarr.Api.Tests/Sportarr.Api.Tests.csproj --verbosity detailed

# Run specific test
dotnet test --filter "FullyQualifiedName~MediaFileParserTests"

# Run with coverage (requires coverlet)
dotnet test /p:CollectCoverage=true /p:CoverletOutputFormat=opencover
```

### Frontend Tests (React/Vitest)

```bash
cd frontend

# Run all tests
npm test

# Run in watch mode (for development)
npm test -- --watch

# Run with UI (interactive)
npm run test:ui

# Run with coverage
npm run test:coverage

# Run specific test file
npm test -- AddEventModal.test.tsx
```

## Test Technologies

### Backend
- **xUnit** - Testing framework
- **FluentAssertions** - Assertion library for readable tests
- **Moq** - Mocking framework
- **Microsoft.AspNetCore.Mvc.Testing** - Integration testing
- **Microsoft.EntityFrameworkCore.InMemory** - In-memory database for tests

### Frontend
- **Vitest** - Fast unit test framework (Vite-native)
- **React Testing Library** - Component testing utilities
- **@testing-library/user-event** - User interaction simulation
- **@testing-library/jest-dom** - DOM matchers

## Writing Tests

### Backend Test Example

```csharp
[Fact]
public void Parse_ShouldExtractEventTitle()
{
    // Arrange
    var parser = new MediaFileParser(_mockLogger.Object);
    var filename = "UFC.300.1080p.WEB-DL.x264-GROUP";

    // Act
    var result = parser.Parse(filename);

    // Assert
    result.EventTitle.Should().Be("UFC 300");
}
```

### Frontend Test Example

```typescript
it('should render modal when isOpen is true', () => {
  renderWithProviders(
    <AddEventModal
      isOpen={true}
      onClose={mockOnClose}
      event={mockEvent}
      onSuccess={mockOnSuccess}
    />
  );

  expect(screen.getByText('Add Event')).toBeInTheDocument();
});
```

## Test Coverage Goals

- **Backend**: Minimum 60% coverage for business logic
- **Frontend**: Minimum 70% coverage for components and hooks
- **Critical paths**: 100% coverage (authentication, file imports, quality evaluation)

## CI/CD Integration

Tests run automatically on:
- All pull requests to `main` and `develop` branches
- All pushes to `main` and `develop` branches

See `.github/workflows/test.yml` for the CI configuration.

## Best Practices

1. **Test Naming**: Use descriptive names that explain what is being tested
   - ✅ `Parse_ShouldExtractEventTitle_WhenGivenValidFilename`
   - ❌ `Test1`

2. **Arrange-Act-Assert**: Structure tests clearly
   ```csharp
   // Arrange - Set up test data
   var parser = new MediaFileParser();

   // Act - Execute the code under test
   var result = parser.Parse(filename);

   // Assert - Verify the results
   result.Should().NotBeNull();
   ```

3. **Test Independence**: Each test should be independent and not rely on others

4. **Mock External Dependencies**: Use mocks for API calls, databases, file system

5. **Test Edge Cases**: Include tests for:
   - Empty/null inputs
   - Invalid data
   - Error conditions
   - Boundary values

6. **Keep Tests Fast**: Unit tests should run in milliseconds

7. **Avoid Test Logic**: Tests should be simple and straightforward

## Debugging Tests

### Backend
```bash
# Debug in Visual Studio Code
# Set breakpoints and use "Debug Test" in Test Explorer

# Or use command line with debugger
dotnet test --logger "console;verbosity=detailed"
```

### Frontend
```bash
# Run single test with debugging
npm test -- --no-coverage AddEventModal.test.tsx

# Use Vitest UI for visual debugging
npm run test:ui
```

## Continuous Improvement

- Add tests for new features
- Update tests when refactoring
- Review test coverage regularly
- Remove or update obsolete tests

## Resources

- [xUnit Documentation](https://xunit.net/)
- [FluentAssertions Docs](https://fluentassertions.com/)
- [Vitest Documentation](https://vitest.dev/)
- [React Testing Library](https://testing-library.com/react)
- [Testing Best Practices](https://kentcdodds.com/blog/common-mistakes-with-react-testing-library)
