using EmergencyDispatch.Application.Localization;
using Microsoft.AspNetCore.Mvc;

namespace EmergencyDispatch.API.Filters;

/// <summary>
/// Translate automatic model validation errors while preserving the default
/// ValidationProblemDetails structure, status code and field keys.
/// </summary>
public static class LocalizedValidationResponseFactory
{
    public static void Configure(ApiBehaviorOptions options)
    {
        var defaultFactory = options.InvalidModelStateResponseFactory;
        options.InvalidModelStateResponseFactory = context =>
        {
            var result = defaultFactory(context);
            if (result is ObjectResult { Value: ValidationProblemDetails problem })
            {
                problem.Title = LocalizationHelper.Localize(problem.Title);
                foreach (var field in problem.Errors.Keys.ToArray())
                {
                    problem.Errors[field] = problem.Errors[field]
                        .Select(error => LocalizationHelper.Localize(error)).ToArray();
                }
            }
            return result;
        };
    }
}
