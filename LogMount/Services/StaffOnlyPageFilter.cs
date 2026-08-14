using LogMount.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Authorization;

namespace LogMount.Services;

/// <summary>Blocks every state-changing Razor Pages handler for read-only users.</summary>
public sealed class StaffOnlyPageFilter : IAsyncPageFilter
{
    public Task OnPageHandlerSelectionAsync(PageHandlerSelectedContext context) => Task.CompletedTask;

    public Task OnPageHandlerExecutionAsync(PageHandlerExecutingContext context, PageHandlerExecutionDelegate next)
    {
        if (context.HandlerInstance.GetType().IsDefined(typeof(AllowAnonymousAttribute), inherit: true))
            return next();

        if (!HttpMethods.IsGet(context.HttpContext.Request.Method) &&
            !context.HttpContext.User.IsInRole(UserRoles.Staff) &&
            !context.HttpContext.User.IsInRole(UserRoles.Admin))
        {
            context.Result = new ForbidResult();
            return Task.CompletedTask;
        }

        return next();
    }
}
