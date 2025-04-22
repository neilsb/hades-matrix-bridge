using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using System.Threading.Tasks;

namespace MatrixBridgeSdk
{
    /// <summary>
    /// Validate requests from Homeserver, ensuring that the request is authorized.
    /// </summary>
    public class AuthorizationMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly string _expectedToken;

        public AuthorizationMiddleware(RequestDelegate next, IOptions<string> bearerTokenValue)
        {
            _next = next;
            _expectedToken = bearerTokenValue.Value;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            if (!context.Request.Headers.TryGetValue("Authorization", out var authorization))
            {
                context.Response.StatusCode = 401; // Unauthorized
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync("""{"errcode": "NET.ZOIT.BRIDGESDK_UNAUTHORIZED" }""");
                return;
            }
            if (authorization != $"Bearer {_expectedToken}")
            {
                context.Response.StatusCode = 403; // Forbidden
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync("""{ "errcode": "NET.ZOIT.BRIDGESDK_FORBIDDEN" }""");
                return;
            }

            await _next(context);
        }
    }
}