using Microsoft.AspNetCore.Antiforgery;

namespace Workhaven.Api.Features.Identity;

internal sealed class CsrfValidationFilter(IAntiforgery antiforgery) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        if (!await antiforgery.IsRequestValidAsync(context.HttpContext))
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid request", detail: "Refresh the page and try again.");
        }

        return await next(context);
    }
}
