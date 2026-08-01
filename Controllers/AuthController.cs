using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using System.ComponentModel.DataAnnotations;
using LogiTrack.Models;
using System.Diagnostics;

namespace LogiTrack.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AuthController : ApiControllerBase
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly IConfiguration _configuration;
        private readonly LogiTrackDBContext _context;

        private readonly ILogger<AuthController> _logger;

        public AuthController(UserManager<ApplicationUser> userManager,
                              SignInManager<ApplicationUser> signInManager,
                              IConfiguration configuration,
                              LogiTrackDBContext context,
                              ILogger<AuthController> logger):base(logger)
        {
            _userManager = userManager;
            _signInManager = signInManager;
            _configuration = configuration;
            _context = context;
            _logger = logger;
        }

        
        [HttpPost("register")]
        [AllowAnonymous]
        [EnableRateLimiting("auth")]
        public async Task<IActionResult> Register([FromBody] RegisterRequest model)
        {
            Stopwatch stopwatch = new Stopwatch();
            stopwatch.Start();

            var user = new ApplicationUser { UserName = model.Email, Email = model.Email };
            var result = await _userManager.CreateAsync(user, model.Password);
            if (!result.Succeeded)
                return BadRequest(result.Errors);

            stopwatch.Stop();
            _logger.LogInformation("User registered in {ElapsedMilliseconds} ms.", stopwatch.ElapsedMilliseconds);
            return Ok(new { message = "User registered" });
        }

        [HttpPost("login")]
        [AllowAnonymous]
        [EnableRateLimiting("auth")]
        public async Task<IActionResult> Login([FromBody] LoginRequest model, CancellationToken cancellationToken)
        {
            Stopwatch stopwatch = new Stopwatch();
            stopwatch.Start();

            var user = await _userManager.FindByEmailAsync(model.Email);
            if (user == null)
                return Unauthorized();

            var result = await _signInManager.CheckPasswordSignInAsync(user, model.Password, false);
            if (!result.Succeeded)
                return Unauthorized();

            var token = await GenerateJwtToken(user);
            var refreshToken = IssueRefreshToken(user);
            await _context.SaveChangesAsync(cancellationToken);

            stopwatch.Stop();
            _logger.LogInformation("User logged in in {ElapsedMilliseconds} ms.", stopwatch.ElapsedMilliseconds);
            return Ok(new { token, refreshToken });
        }

        [HttpPost("refresh")]
        [AllowAnonymous]
        [EnableRateLimiting("auth")]
        public async Task<IActionResult> Refresh([FromBody] RefreshRequest model, CancellationToken cancellationToken)
        {
            var tokenHash = HashToken(model.RefreshToken);
            var stored = await _context.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == tokenHash, cancellationToken);

            if (stored == null || DateTime.UtcNow >= stored.ExpiresAt)
                return Unauthorized();

            if (stored.RevokedAt != null)
            {
                // This token was already rotated out for a newer one, so presenting it again means
                // either a client retried a stale token or the token was stolen. Either way, treat
                // it as compromise and kill every active session for this user.
                await _context.RefreshTokens
                    .Where(t => t.UserId == stored.UserId && t.RevokedAt == null)
                    .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, DateTime.UtcNow), cancellationToken);

                _logger.LogWarning("Reused refresh token detected for user {UserId}; all active refresh tokens revoked.", stored.UserId);
                return Unauthorized();
            }

            var user = await _userManager.FindByIdAsync(stored.UserId);
            if (user == null)
                return Unauthorized();

            var newRefreshToken = IssueRefreshToken(user);
            stored.RevokedAt = DateTime.UtcNow;
            stored.ReplacedByTokenHash = HashToken(newRefreshToken);
            await _context.SaveChangesAsync(cancellationToken);

            var accessToken = await GenerateJwtToken(user);
            return Ok(new { token = accessToken, refreshToken = newRefreshToken });
        }

        [HttpPost("logout")]
        [Authorize]
        public async Task<IActionResult> Logout([FromBody] RefreshRequest model, CancellationToken cancellationToken)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue(JwtRegisteredClaimNames.Sub);
            var tokenHash = HashToken(model.RefreshToken);

            var rowsAffected = await _context.RefreshTokens
                .Where(t => t.TokenHash == tokenHash && t.UserId == userId && t.RevokedAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, DateTime.UtcNow), cancellationToken);

            if (rowsAffected == 0)
                return NotFoundResource("RefreshToken", "provided token");

            _logger.LogInformation("User {UserId} logged out; refresh token revoked.", userId);
            return NoContent();
        }

        [HttpPost("assign-manager-role")]
        [Authorize(Roles = "Manager")]
        public async Task<IActionResult> AssignManagerRole([FromBody] AssignRoleRequest model)
        {
            var user = await _userManager.FindByEmailAsync(model.Email);
            if (user == null)
                return NotFoundResource("User", model.Email);

            if (await _userManager.IsInRoleAsync(user, "Manager"))
                return Ok(new { message = $"{model.Email} is already a Manager." });

            var result = await _userManager.AddToRoleAsync(user, "Manager");
            if (!result.Succeeded)
                return BadRequest(result.Errors);

            _logger.LogInformation("User {Email} promoted to Manager by {ActingUser}.", model.Email, User.Identity?.Name);
            return Ok(new { message = $"{model.Email} is now a Manager." });
        }

        private async Task<string> GenerateJwtToken(ApplicationUser user)
        {
            var key = _configuration["Jwt:Key"] ?? throw new InvalidOperationException("JWT key not configured");
            var issuer = _configuration["Jwt:Issuer"] ?? "LogiTrack";
            var audience = _configuration["Jwt:Audience"] ?? "LogiTrackUsers";

            Stopwatch stopwatch = new Stopwatch();
            stopwatch.Start();

            var roles = await _userManager.GetRolesAsync(user);

            var claims = new List<Claim>
            {
                new Claim(JwtRegisteredClaimNames.Sub, user.Id),
                new Claim(JwtRegisteredClaimNames.Email, user.Email ?? string.Empty),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
            };

            claims.AddRange(roles.Select(role => new Claim(ClaimTypes.Role, role)));

            var keyBytes = Encoding.UTF8.GetBytes(key);
            var creds = new SigningCredentials(new SymmetricSecurityKey(keyBytes), SecurityAlgorithms.HmacSha256);

            var accessTokenMinutes = _configuration.GetValue<int?>("Jwt:AccessTokenMinutes") ?? 15;
            var token = new JwtSecurityToken(
                issuer: issuer,
                audience: audience,
                claims: claims,
                expires: DateTime.UtcNow.AddMinutes(accessTokenMinutes),
                signingCredentials: creds);

            stopwatch.Stop();
            _logger.LogInformation("JWT token generated in {ElapsedMilliseconds} ms.", stopwatch.ElapsedMilliseconds);
            return new JwtSecurityTokenHandler().WriteToken(token);
        }

        // Adds (but does not save) a new refresh token for the user. Callers are responsible for
        // calling SaveChangesAsync, so this can be batched with other changes in the same request.
        private string IssueRefreshToken(ApplicationUser user)
        {
            var refreshTokenDays = _configuration.GetValue<int?>("Jwt:RefreshTokenDays") ?? 7;
            var plaintextToken = GenerateSecureRandomToken();

            _context.RefreshTokens.Add(new RefreshToken
            {
                UserId = user.Id,
                TokenHash = HashToken(plaintextToken),
                CreatedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddDays(refreshTokenDays)
            });

            return plaintextToken;
        }

        private static string GenerateSecureRandomToken()
        {
            var bytes = RandomNumberGenerator.GetBytes(32);
            return Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');
        }

        private static string HashToken(string token)
        {
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
            return Convert.ToHexString(bytes);
        }
    }
}
