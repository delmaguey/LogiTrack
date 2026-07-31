using System;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
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

        private readonly ILogger<AuthController> _logger;

        public AuthController(UserManager<ApplicationUser> userManager,
                              SignInManager<ApplicationUser> signInManager,
                              IConfiguration configuration,
                              ILogger<AuthController> logger):base(logger)
        {
            _userManager = userManager;
            _signInManager = signInManager;
            _configuration = configuration;
            _logger = logger;
        }

        
        [HttpPost("register")]
        [AllowAnonymous]
        public async Task<IActionResult> Register([FromBody] RegisterRequest model)
        {
            Stopwatch stopwatch = new Stopwatch();
            stopwatch.Start();

            if (model == null || string.IsNullOrWhiteSpace(model.Email) || string.IsNullOrWhiteSpace(model.Password))
                return BadRequest("Email and password are required.");

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
        public async Task<IActionResult> Login([FromBody] LoginRequest model)
        {
            Stopwatch stopwatch = new Stopwatch();
            stopwatch.Start();

            if (model == null || string.IsNullOrWhiteSpace(model.Email) || string.IsNullOrWhiteSpace(model.Password))
                return BadRequest("Email and password are required.");

            var user = await _userManager.FindByEmailAsync(model.Email);
            if (user == null)
                return Unauthorized();

            var result = await _signInManager.CheckPasswordSignInAsync(user, model.Password, false);
            if (!result.Succeeded)
                return Unauthorized();

            var token = GenerateJwtToken(user);
            stopwatch.Stop();
            _logger.LogInformation("User logged in in {ElapsedMilliseconds} ms.", stopwatch.ElapsedMilliseconds);
            return Ok(new { token });
        }

        private string GenerateJwtToken(ApplicationUser user)
        {
            var key = _configuration["Jwt:Key"] ?? throw new InvalidOperationException("JWT key not configured");
            var issuer = _configuration["Jwt:Issuer"] ?? "LogiTrack";
            var audience = _configuration["Jwt:Audience"] ?? "LogiTrackUsers";

            Stopwatch stopwatch = new Stopwatch();
            stopwatch.Start();

            var claims = new[]
            {
                new Claim(JwtRegisteredClaimNames.Sub, user.Id),
                new Claim(JwtRegisteredClaimNames.Email, user.Email ?? string.Empty),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
            };

            var keyBytes = Encoding.UTF8.GetBytes(key);
            var creds = new SigningCredentials(new SymmetricSecurityKey(keyBytes), SecurityAlgorithms.HmacSha256);

            var token = new JwtSecurityToken(
                issuer: issuer,
                audience: audience,
                claims: claims,
                expires: DateTime.UtcNow.AddHours(6),
                signingCredentials: creds);

            stopwatch.Stop();
            _logger.LogInformation("JWT token generated in {ElapsedMilliseconds} ms.", stopwatch.ElapsedMilliseconds);
            return new JwtSecurityTokenHandler().WriteToken(token);
        }
    }
}
