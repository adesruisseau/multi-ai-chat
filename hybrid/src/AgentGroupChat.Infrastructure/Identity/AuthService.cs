using AgentGroupChat.Core.Services.Interfaces;
using Microsoft.AspNetCore.Identity;
using System;
using System.Collections.Generic;
using System.Text;

namespace AgentGroupChat.Infrastructure.Identity
{
    public class AuthService : IAuthService
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly SignInManager<ApplicationUser> _signInManager;

        public AuthService(
            UserManager<ApplicationUser> userManager,
            SignInManager<ApplicationUser> signInManager)
        {
            _userManager = userManager;
            _signInManager = signInManager;
        }

        bool loginBusy = false;
        public async Task<SignInResult> LoginAsync(string username, string password)
        {
            if (loginBusy)
            {
                return null;
            }
            loginBusy = true;
            var res = await _signInManager.PasswordSignInAsync(
                username,
                password,
                isPersistent: false,
                lockoutOnFailure: false);

            loginBusy = false;
            return res;
            
        }

        public async Task<IdentityResult> RegisterAsync(string username, string email, string password)
        {
            var user = new ApplicationUser
            {
                UserName = username,
                Email = email
            };

            var result = await _userManager.CreateAsync(user, password);

            if (!result.Succeeded)
                return result;

            // IMPORTANT: avoid SignInAsync here in Blazor lifecycle edge cases
            await _signInManager.PasswordSignInAsync(username, password, false, false);

            return result;
        }
    }
}
