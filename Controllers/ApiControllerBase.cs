using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace LogiTrack.Controllers
{
    [ApiController]
    [Produces("application/json")]
    public abstract class ApiControllerBase : ControllerBase
    {
        protected readonly ILogger Logger;

        protected ApiControllerBase(ILogger logger)
        {
            Logger = logger;
        }

        protected ActionResult NotFoundResource(string resource, object id)
        {
            Logger?.LogWarning("{Resource} {Id} not found. Path={Path}", resource, id, HttpContext?.Request.Path);
            return NotFound(new ProblemDetails
            {
                Title = $"{resource} no encontrado",
                Detail = $"{resource} con id {id} no existe.",
                Status = StatusCodes.Status404NotFound,
                Instance = HttpContext?.Request.Path
            });
        }

        protected ActionResult BadRequestResource(string title, string detail)
        {
            Logger?.LogWarning("Bad request: {Title}. Path={Path}", title, HttpContext?.Request.Path);
            return BadRequest(new ProblemDetails
            {
                Title = title,
                Detail = detail,
                Status = StatusCodes.Status400BadRequest,
                Instance = HttpContext?.Request.Path
            });
        }

        protected ActionResult ConflictResource(string title, string detail)
        {
            Logger?.LogWarning("Conflict: {Title}. Path={Path}", title, HttpContext?.Request.Path);
            return Conflict(new ProblemDetails
            {
                Title = title,
                Detail = detail,
                Status = StatusCodes.Status409Conflict,
                Instance = HttpContext?.Request.Path
            });
        }

        // Lets a client skip re-downloading a response it already has by comparing against the
        // ETag it sent back on its last request.
        protected bool IsETagMatch(string etag) =>
            Request.Headers.TryGetValue("If-None-Match", out var ifNoneMatch) && ifNoneMatch == etag;

        protected void SetCacheHeaders(string etag, TimeSpan maxAge)
        {
            Response.Headers.ETag = etag;
            Response.Headers.CacheControl = $"private, max-age={(int)maxAge.TotalSeconds}";
        }
    }
}
