using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Resort.Application.Common.Utility;
using Resort.Domain.Entities;
using Resort.Web.Controllers;
using Resort.Web.ViewModels;
using Xunit;

namespace Authorization.Tests;

public class RegistrationSecurityTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("Customer")]
    [InlineData("Admin")]
    [InlineData("unexpected-role")]
    public async Task PublicRegistrationCannotSelectAnElevatedRole(string requestedRole)
    {
        var users = createUsers();
        var signIn = createSignIn(users);
        var controller = createController(users, signIn);
        var result = await controller.Register(new RegisterVM
        {
            Email = "learner@example.invalid", Name = "Learner", Password = "NotARealCredential!123", Role = requestedRole
        });
        Assert.IsType<RedirectToActionResult>(result);
        users.Verify(u => u.AddToRoleAsync(It.IsAny<ApplicationUser>(), SD.Role_Customer), Times.Once);
        users.Verify(u => u.AddToRoleAsync(It.IsAny<ApplicationUser>(), It.Is<string>(r => r != SD.Role_Customer)), Times.Never);
    }

    [Fact]
    public async Task RoleAssignmentFailureDoesNotSignInTheNewAccount()
    {
        var users = createUsers();
        users.Setup(u => u.AddToRoleAsync(It.IsAny<ApplicationUser>(), SD.Role_Customer))
            .ReturnsAsync(IdentityResult.Failed(new IdentityError { Description = "Assignment failed" }));
        var signIn = createSignIn(users);
        var controller = createController(users, signIn);
        var result = await controller.Register(new RegisterVM
        {
            Email = "learner@example.invalid", Name = "Learner", Password = "NotARealCredential!123", Role = SD.Role_Admin
        });
        Assert.IsType<ViewResult>(result);
        Assert.False(controller.ModelState.IsValid);
        users.Verify(u => u.DeleteAsync(It.IsAny<ApplicationUser>()), Times.Once);
        signIn.Verify(s => s.SignInAsync(It.IsAny<ApplicationUser>(), It.IsAny<bool>(), It.IsAny<string>()), Times.Never);
    }

    private static AccountController createController(Mock<UserManager<ApplicationUser>> users, Mock<SignInManager<ApplicationUser>> signIn)
    {
        var roles = new Mock<RoleManager<IdentityRole>>(new Mock<IRoleStore<IdentityRole>>().Object, null, null, null, null);
        return new AccountController(users.Object, roles.Object, signIn.Object)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
    }

    private static Mock<UserManager<ApplicationUser>> createUsers()
    {
        var users = new Mock<UserManager<ApplicationUser>>(new Mock<IUserStore<ApplicationUser>>().Object,
            Options.Create(new IdentityOptions()), null, null, null, null, null, null, null);
        users.Setup(u => u.CreateAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>())).ReturnsAsync(IdentityResult.Success);
        users.Setup(u => u.AddToRoleAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>())).ReturnsAsync(IdentityResult.Success);
        users.Setup(u => u.DeleteAsync(It.IsAny<ApplicationUser>())).ReturnsAsync(IdentityResult.Success);
        return users;
    }

    private static Mock<SignInManager<ApplicationUser>> createSignIn(Mock<UserManager<ApplicationUser>> users)
    {
        var manager = new Mock<SignInManager<ApplicationUser>>(users.Object, new Mock<IHttpContextAccessor>().Object,
            new Mock<IUserClaimsPrincipalFactory<ApplicationUser>>().Object, Options.Create(new IdentityOptions()),
            new Mock<ILogger<SignInManager<ApplicationUser>>>().Object, new Mock<IAuthenticationSchemeProvider>().Object,
            new Mock<IUserConfirmation<ApplicationUser>>().Object);
        manager.Setup(s => s.SignInAsync(It.IsAny<ApplicationUser>(), It.IsAny<bool>(), It.IsAny<string>())).Returns(Task.CompletedTask);
        return manager;
    }
}
