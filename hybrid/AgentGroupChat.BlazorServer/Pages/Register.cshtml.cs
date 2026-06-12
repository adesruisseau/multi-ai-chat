using System.ComponentModel.DataAnnotations;
using AgentGroupChat.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AgentGroupChat.BlazorServer.Pages;

public class RegisterModel : PageModel
{
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly UserManager<ApplicationUser> _userManager;

    public RegisterModel(
        SignInManager<ApplicationUser> signInManager,
        UserManager<ApplicationUser> userManager)
    {
        _signInManager = signInManager;
        _userManager = userManager;
    }

    [BindProperty(SupportsGet = true)]
    public string ReturnUrl { get; set; } = "/";

    [BindProperty]
    public RegisterInputModel RegisterInput { get; set; } = new();

    public IActionResult OnGet()
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            return LocalRedirect(NormalizeReturnUrl(ReturnUrl));
        }

        ReturnUrl = NormalizeReturnUrl(ReturnUrl);
        return Page();
    }
    public async Task<IActionResult> OnPostRegisterAsync()
    {
        ReturnUrl = NormalizeReturnUrl(ReturnUrl);

        if (!ModelState.IsValid)
        {
            return Page();
        }

        if (!string.Equals(RegisterInput.Password, RegisterInput.ConfirmPassword, StringComparison.Ordinal))
        {
            ModelState.AddModelError(nameof(RegisterInput.ConfirmPassword), "Passwords do not match.");
            return Page();
        }

        var user = new ApplicationUser
        {
            UserName = RegisterInput.UserName,
            Email = RegisterInput.Email
        };

        var result = await _userManager.CreateAsync(user, RegisterInput.Password);
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(string.Empty, error.Description);
            }

            return Page();
        }

        await _signInManager.SignInAsync(user, isPersistent: false);
        return LocalRedirect(ReturnUrl);
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

    public sealed class RegisterInputModel
    {
        [Required]
        [EmailAddress]
        [Display(Name = "Email")]
        public string Email { get; set; } = string.Empty;

        [Required]
        [Display(Name = "Username")]
        public string UserName { get; set; } = string.Empty;

        [Required]
        [DataType(DataType.Password)]
        [Display(Name = "Password")]
        [StringLength(100, MinimumLength = 8, ErrorMessage = "Password must be at least 8 characters long.")]
        public string Password { get; set; } = string.Empty;

        [Required]
        [DataType(DataType.Password)]
        [Display(Name = "Confirm Password")]
        public string ConfirmPassword { get; set; } = string.Empty;
    }
}