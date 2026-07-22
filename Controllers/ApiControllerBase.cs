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
    }
}
