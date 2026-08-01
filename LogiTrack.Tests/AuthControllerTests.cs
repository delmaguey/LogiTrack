using System.Security.Claims;
using LogiTrack.Controllers;
using LogiTrack.Models;
using LogiTrack.Tests.TestHelpers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace LogiTrack.Tests
{
    public class AuthControllerTests : IDisposable
    {
        private readonly SqliteInMemoryContextFactory _dbFactory = new();
        private readonly Mock<UserManager<ApplicationUser>> _userManager = CreateUserManagerMock();
        private readonly Mock<SignInManager<ApplicationUser>> _signInManager;

        public AuthControllerTests()
        {
            _signInManager = CreateSignInManagerMock(_userManager.Object);
        }

        public void Dispose() => _dbFactory.Dispose();

        private static Mock<UserManager<ApplicationUser>> CreateUserManagerMock()
        {
            var store = new Mock<IUserStore<ApplicationUser>>();
            return new Mock<UserManager<ApplicationUser>>(store.Object, null!, null!, null!, null!, null!, null!, null!, null!);
        }

        private static Mock<SignInManager<ApplicationUser>> CreateSignInManagerMock(UserManager<ApplicationUser> userManager)
        {
            var contextAccessor = new Mock<IHttpContextAccessor>();
            contextAccessor.Setup(a => a.HttpContext).Returns(new DefaultHttpContext());
            var claimsFactory = new Mock<IUserClaimsPrincipalFactory<ApplicationUser>>();
            return new Mock<SignInManager<ApplicationUser>>(userManager, contextAccessor.Object, claimsFactory.Object, null!, null!, null!, null!);
        }

        private static IConfiguration CreateConfiguration() => new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Key"] = "unit-test-signing-key-1234567890123456789012345678",
                ["Jwt:Issuer"] = "LogiTrack",
                ["Jwt:Audience"] = "LogiTrackUsers",
                ["Jwt:AccessTokenMinutes"] = "15",
                ["Jwt:RefreshTokenDays"] = "7"
            })
            .Build();

        private AuthController CreateController(LogiTrackDBContext context, ClaimsPrincipal? user = null)
        {
            var controller = new AuthController(
                _userManager.Object,
                _signInManager.Object,
                CreateConfiguration(),
                context,
                NullLogger<AuthController>.Instance);

            var httpContext = new DefaultHttpContext();
            if (user != null)
                httpContext.User = user;

            controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
            return controller;
        }

        private static ClaimsPrincipal PrincipalFor(string userId) => new(new ClaimsIdentity(
            new[] { new Claim(ClaimTypes.NameIdentifier, userId) }, "TestAuth"));

        // RefreshTokens.UserId has a real FK to AspNetUsers.Id enforced by SQLite. UserManager is
        // mocked and never actually inserts rows, so any test whose flow persists a RefreshToken
        // for a given user must seed that user directly first.
        private static async Task SeedUserAsync(LogiTrackDBContext context, ApplicationUser user)
        {
            context.Users.Add(user);
            await context.SaveChangesAsync();
        }

        [Fact]
        public async Task Register_Success_ReturnsOk()
        {
            _userManager.Setup(m => m.CreateAsync(It.IsAny<ApplicationUser>(), "P@ssw0rd!"))
                .ReturnsAsync(IdentityResult.Success);

            using var context = _dbFactory.CreateContext();
            var controller = CreateController(context);

            var result = await controller.Register(new RegisterRequest { Email = "new@test.com", Password = "P@ssw0rd!" });

            Assert.IsType<OkObjectResult>(result);
        }

        [Fact]
        public async Task Register_Failure_ReturnsBadRequest()
        {
            _userManager.Setup(m => m.CreateAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>()))
                .ReturnsAsync(IdentityResult.Failed(new IdentityError { Description = "Email already taken." }));

            using var context = _dbFactory.CreateContext();
            var controller = CreateController(context);

            var result = await controller.Register(new RegisterRequest { Email = "dup@test.com", Password = "P@ssw0rd!" });

            Assert.IsType<BadRequestObjectResult>(result);
        }

        [Fact]
        public async Task Login_NonexistentUser_ReturnsUnauthorized()
        {
            _userManager.Setup(m => m.FindByEmailAsync("nouser@test.com")).ReturnsAsync((ApplicationUser?)null);

            using var context = _dbFactory.CreateContext();
            var controller = CreateController(context);

            var result = await controller.Login(new LoginRequest { Email = "nouser@test.com", Password = "x" }, CancellationToken.None);

            Assert.IsType<UnauthorizedResult>(result);
        }

        [Fact]
        public async Task Login_WrongPassword_ReturnsUnauthorized()
        {
            var user = new ApplicationUser { Id = "user-1", Email = "user@test.com", UserName = "user@test.com" };
            _userManager.Setup(m => m.FindByEmailAsync("user@test.com")).ReturnsAsync(user);
            _signInManager.Setup(s => s.CheckPasswordSignInAsync(user, "wrong", false)).ReturnsAsync(Microsoft.AspNetCore.Identity.SignInResult.Failed);

            using var context = _dbFactory.CreateContext();
            var controller = CreateController(context);

            var result = await controller.Login(new LoginRequest { Email = "user@test.com", Password = "wrong" }, CancellationToken.None);

            Assert.IsType<UnauthorizedResult>(result);
        }

        [Fact]
        public async Task Login_Success_ReturnsTokenAndRefreshToken_AndPersistsRefreshToken()
        {
            var user = new ApplicationUser { Id = "user-1", Email = "user@test.com", UserName = "user@test.com" };
            _userManager.Setup(m => m.FindByEmailAsync("user@test.com")).ReturnsAsync(user);
            _signInManager.Setup(s => s.CheckPasswordSignInAsync(user, "correct", false)).ReturnsAsync(Microsoft.AspNetCore.Identity.SignInResult.Success);
            _userManager.Setup(m => m.GetRolesAsync(user)).ReturnsAsync(new List<string>());

            using var context = _dbFactory.CreateContext();
            await SeedUserAsync(context, user);
            var controller = CreateController(context);

            var result = await controller.Login(new LoginRequest { Email = "user@test.com", Password = "correct" }, CancellationToken.None);

            var ok = Assert.IsType<OkObjectResult>(result);
            var tokenProp = ok.Value!.GetType().GetProperty("token")!.GetValue(ok.Value) as string;
            var refreshProp = ok.Value!.GetType().GetProperty("refreshToken")!.GetValue(ok.Value) as string;
            Assert.False(string.IsNullOrEmpty(tokenProp));
            Assert.False(string.IsNullOrEmpty(refreshProp));

            Assert.Equal(1, await context.RefreshTokens.CountAsync());
        }

        [Fact]
        public async Task Refresh_ValidToken_RotatesAndReturnsNewTokens()
        {
            var user = new ApplicationUser { Id = "user-1", Email = "user@test.com", UserName = "user@test.com" };
            _userManager.Setup(m => m.FindByEmailAsync("user@test.com")).ReturnsAsync(user);
            _signInManager.Setup(s => s.CheckPasswordSignInAsync(user, "correct", false)).ReturnsAsync(Microsoft.AspNetCore.Identity.SignInResult.Success);
            _userManager.Setup(m => m.GetRolesAsync(user)).ReturnsAsync(new List<string>());
            _userManager.Setup(m => m.FindByIdAsync("user-1")).ReturnsAsync(user);

            using var context = _dbFactory.CreateContext();
            await SeedUserAsync(context, user);
            var controller = CreateController(context);

            var loginResult = Assert.IsType<OkObjectResult>(await controller.Login(new LoginRequest { Email = "user@test.com", Password = "correct" }, CancellationToken.None));
            var originalRefreshToken = (string)loginResult.Value!.GetType().GetProperty("refreshToken")!.GetValue(loginResult.Value)!;

            var refreshResult = await controller.Refresh(new RefreshRequest { RefreshToken = originalRefreshToken }, CancellationToken.None);

            var ok = Assert.IsType<OkObjectResult>(refreshResult);
            var newRefreshToken = (string)ok.Value!.GetType().GetProperty("refreshToken")!.GetValue(ok.Value)!;
            Assert.NotEqual(originalRefreshToken, newRefreshToken);
            Assert.Equal(2, await context.RefreshTokens.CountAsync());
        }

        [Fact]
        public async Task Refresh_NonexistentToken_ReturnsUnauthorized()
        {
            using var context = _dbFactory.CreateContext();
            var controller = CreateController(context);

            var result = await controller.Refresh(new RefreshRequest { RefreshToken = "does-not-exist" }, CancellationToken.None);

            Assert.IsType<UnauthorizedResult>(result);
        }

        [Fact]
        public async Task Refresh_ExpiredToken_ReturnsUnauthorized()
        {
            using var context = _dbFactory.CreateContext();
            await SeedUserAsync(context, new ApplicationUser { Id = "user-1", Email = "user@test.com", UserName = "user@test.com" });
            context.RefreshTokens.Add(new RefreshToken
            {
                UserId = "user-1",
                TokenHash = HashForTest("expired-token"),
                CreatedAt = DateTime.UtcNow.AddDays(-10),
                ExpiresAt = DateTime.UtcNow.AddDays(-3)
            });
            await context.SaveChangesAsync();

            var controller = CreateController(context);
            var result = await controller.Refresh(new RefreshRequest { RefreshToken = "expired-token" }, CancellationToken.None);

            Assert.IsType<UnauthorizedResult>(result);
        }

        [Fact]
        public async Task Refresh_ReusedRevokedToken_RevokesAllActiveTokensForUser()
        {
            using var context = _dbFactory.CreateContext();
            await SeedUserAsync(context, new ApplicationUser { Id = "user-1", Email = "user@test.com", UserName = "user@test.com" });
            context.RefreshTokens.AddRange(
                new RefreshToken
                {
                    UserId = "user-1",
                    TokenHash = HashForTest("already-rotated"),
                    CreatedAt = DateTime.UtcNow.AddMinutes(-10),
                    ExpiresAt = DateTime.UtcNow.AddDays(7),
                    RevokedAt = DateTime.UtcNow.AddMinutes(-5)
                },
                new RefreshToken
                {
                    UserId = "user-1",
                    TokenHash = HashForTest("still-active"),
                    CreatedAt = DateTime.UtcNow.AddMinutes(-5),
                    ExpiresAt = DateTime.UtcNow.AddDays(7)
                });
            await context.SaveChangesAsync();

            var controller = CreateController(context);
            var result = await controller.Refresh(new RefreshRequest { RefreshToken = "already-rotated" }, CancellationToken.None);

            Assert.IsType<UnauthorizedResult>(result);
            var allTokens = await context.RefreshTokens.AsNoTracking().Where(t => t.UserId == "user-1").ToListAsync();
            Assert.All(allTokens, t => Assert.NotNull(t.RevokedAt));
        }

        [Fact]
        public async Task Logout_ValidToken_RevokesAndReturnsNoContent()
        {
            using var context = _dbFactory.CreateContext();
            await SeedUserAsync(context, new ApplicationUser { Id = "user-1", Email = "user@test.com", UserName = "user@test.com" });
            context.RefreshTokens.Add(new RefreshToken
            {
                UserId = "user-1",
                TokenHash = HashForTest("my-token"),
                CreatedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddDays(7)
            });
            await context.SaveChangesAsync();

            var controller = CreateController(context, PrincipalFor("user-1"));
            var result = await controller.Logout(new RefreshRequest { RefreshToken = "my-token" }, CancellationToken.None);

            Assert.IsType<NoContentResult>(result);
            var stored = await context.RefreshTokens.AsNoTracking().FirstAsync(t => t.TokenHash == HashForTest("my-token"));
            Assert.NotNull(stored.RevokedAt);
        }

        [Fact]
        public async Task Logout_TokenBelongingToDifferentUser_ReturnsNotFound()
        {
            using var context = _dbFactory.CreateContext();
            await SeedUserAsync(context, new ApplicationUser { Id = "owner-user", Email = "owner@test.com", UserName = "owner@test.com" });
            context.RefreshTokens.Add(new RefreshToken
            {
                UserId = "owner-user",
                TokenHash = HashForTest("someone-elses-token"),
                CreatedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddDays(7)
            });
            await context.SaveChangesAsync();

            var controller = CreateController(context, PrincipalFor("different-user"));
            var result = await controller.Logout(new RefreshRequest { RefreshToken = "someone-elses-token" }, CancellationToken.None);

            Assert.IsType<NotFoundObjectResult>(result);
        }

        [Fact]
        public async Task AssignManagerRole_UserNotFound_ReturnsNotFound()
        {
            _userManager.Setup(m => m.FindByEmailAsync("ghost@test.com")).ReturnsAsync((ApplicationUser?)null);

            using var context = _dbFactory.CreateContext();
            var controller = CreateController(context, PrincipalFor("manager-1"));

            var result = await controller.AssignManagerRole(new AssignRoleRequest { Email = "ghost@test.com" });

            Assert.IsType<NotFoundObjectResult>(result);
        }

        [Fact]
        public async Task AssignManagerRole_AlreadyManager_ReturnsOkWithMessage()
        {
            var user = new ApplicationUser { Id = "user-1", Email = "user@test.com" };
            _userManager.Setup(m => m.FindByEmailAsync("user@test.com")).ReturnsAsync(user);
            _userManager.Setup(m => m.IsInRoleAsync(user, "Manager")).ReturnsAsync(true);

            using var context = _dbFactory.CreateContext();
            var controller = CreateController(context, PrincipalFor("manager-1"));

            var result = await controller.AssignManagerRole(new AssignRoleRequest { Email = "user@test.com" });

            var ok = Assert.IsType<OkObjectResult>(result);
            var message = (string)ok.Value!.GetType().GetProperty("message")!.GetValue(ok.Value)!;
            Assert.Contains("already a Manager", message);
            _userManager.Verify(m => m.AddToRoleAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task AssignManagerRole_Success_AddsRole()
        {
            var user = new ApplicationUser { Id = "user-1", Email = "user@test.com" };
            _userManager.Setup(m => m.FindByEmailAsync("user@test.com")).ReturnsAsync(user);
            _userManager.Setup(m => m.IsInRoleAsync(user, "Manager")).ReturnsAsync(false);
            _userManager.Setup(m => m.AddToRoleAsync(user, "Manager")).ReturnsAsync(IdentityResult.Success);

            using var context = _dbFactory.CreateContext();
            var controller = CreateController(context, PrincipalFor("manager-1"));

            var result = await controller.AssignManagerRole(new AssignRoleRequest { Email = "user@test.com" });

            var ok = Assert.IsType<OkObjectResult>(result);
            var message = (string)ok.Value!.GetType().GetProperty("message")!.GetValue(ok.Value)!;
            Assert.Contains("is now a Manager", message);
        }

        // Mirrors AuthController's private HashToken so tests can pre-seed rows keyed by hash
        // without needing to call the controller first.
        private static string HashForTest(string token)
        {
            var bytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token));
            return Convert.ToHexString(bytes);
        }
    }
}
