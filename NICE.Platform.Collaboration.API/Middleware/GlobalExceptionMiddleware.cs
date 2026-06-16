namespace NICE.Platform.Collaboration.API.Middleware;
using NICE.Platform.Collaboration.Core.Exceptions;
using System.Text.Json;

public class GlobalExceptionMiddleware(RequestDelegate next,
    ILogger<GlobalExceptionMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try { await next(context); }
        catch (CollaborationNotFoundException ex)          { await Write(context, 404, ex.Message); }
        catch (UnauthorizedCollaborationAccessException ex){ await Write(context, 403, ex.Message); }
        catch (AgentCapacityExceededException ex)          { await Write(context, 409, ex.Message); }
        catch (InvalidApiKeyException ex)                  { await Write(context, 401, ex.Message); }
        catch (AuthValidationException ex)                 { await Write(context, 401, ex.Message); }
        catch (FluentValidation.ValidationException ex)
        {
            await Write(context, 400, string.Join("; ", ex.Errors.Select(e => e.ErrorMessage)));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unhandled exception");
            await Write(context, 500, "An unexpected error occurred.");
        }
    }

    private static Task Write(HttpContext ctx, int status, string message)
    {
        ctx.Response.StatusCode  = status;
        ctx.Response.ContentType = "application/json";
        return ctx.Response.WriteAsync(
            JsonSerializer.Serialize(new { error = message }));
    }
}
