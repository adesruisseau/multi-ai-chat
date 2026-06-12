using System.ComponentModel.DataAnnotations;
using AgentGroupChat.Infrastructure.Identity;
using AgentGroupChat.Infrastructure.Seeding;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AgentGroupChat.BlazorServer.Pages;

public class LoginPageModel : PageModel
{
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly IDataSeeder _dataSeeder;

    public LoginPageModel(
        SignInManager<ApplicationUser> signInManager,
        IDataSeeder dataSeeder)
    {
        _signInManager = signInManager;
        _dataSeeder = dataSeeder;
    }

    [BindProperty(SupportsGet = true)]
    public string ReturnUrl { get; set; } = "/";

    [BindProperty]
    public LoginInputModel LoginInput { get; set; } = new();

    public IActionResult OnGet()
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            return LocalRedirect(NormalizeReturnUrl(ReturnUrl));
        }

        ReturnUrl = NormalizeReturnUrl(ReturnUrl);
        return Page();
    }

    public async Task<IActionResult> OnPostLoginAsync()
    {
        ReturnUrl = NormalizeReturnUrl(ReturnUrl);

        if (!ModelState.IsValid)
        {
            return Page();
        }

        var result = await _signInManager.PasswordSignInAsync(
            LoginInput.UserName,
            LoginInput.Password,
            isPersistent: false,
            lockoutOnFailure: false);

        if (result.Succeeded)
        {
            var user = await _signInManager.UserManager.FindByNameAsync(LoginInput.UserName);
            if (user is not null)
            {
                await _dataSeeder.SeedAsync(user.Id);
            }

            return LocalRedirect(ReturnUrl);
        }

        ModelState.AddModelError(string.Empty, "Invalid username or password.");
        return Page();
    }
    private static string NormalizeReturnUrl(string? returnUrl)
    {
        if (string.IsNullOrWhiteSpace(returnUrl))
        {
            return "/";
        }

        returnUrl = returnUrl.Trim();
        if (!returnUrl.StartsWith("/", StringComparison.Ordinal))
        {
            return "/";
        }

        if (returnUrl.StartsWith("//", StringComparison.Ordinal) || returnUrl.StartsWith("/\\", StringComparison.Ordinal))
        {
            return "/";
        }

        return returnUrl;
    }

    public sealed class LoginInputModel
    {
        [Required]
        [Display(Name = "Username")]
        public string UserName { get; set; } = string.Empty;

        [Required]
        [DataType(DataType.Password)]
        [Display(Name = "Password")]
        public string Password { get; set; } = string.Empty;
    }
}