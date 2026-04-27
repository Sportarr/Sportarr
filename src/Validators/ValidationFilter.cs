using FluentValidation;
using Microsoft.AspNetCore.Http;

namespace Sportarr.Api.Validators;

/// <summary>
/// Endpoint filter that runs FluentValidation against the request body argument
/// of an endpoint. Returns 400 with field-level errors on failure.
/// Apply with: <c>app.MapPost(...).WithRequestValidation&lt;TRequest&gt;()</c>
/// </summary>
public class ValidationFilter<TRequest> : IEndpointFilter where TRequest : class
{
    private readonly IValidator<TRequest> _validator;

    public ValidationFilter(IValidator<TRequest> validator)
    {
        _validator = validator;
    }

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var argument = context.Arguments.OfType<TRequest>().FirstOrDefault();
        if (argument is null)
        {
            return await next(context);
        }

        var result = await _validator.ValidateAsync(argument);
        if (result.IsValid)
        {
            return await next(context);
        }

        var errors = result.Errors
            .GroupBy(e => e.PropertyName)
            .ToDictionary(
                g => g.Key,
                g => g.Select(e => e.ErrorMessage).ToArray());

        return Results.ValidationProblem(errors);
    }
}

public static class ValidationFilterExtensions
{
    /// <summary>
    /// Attaches FluentValidation to a minimal-API endpoint for the given request type.
    /// </summary>
    public static RouteHandlerBuilder WithRequestValidation<TRequest>(this RouteHandlerBuilder builder)
        where TRequest : class
    {
        return builder.AddEndpointFilter<ValidationFilter<TRequest>>();
    }
}
