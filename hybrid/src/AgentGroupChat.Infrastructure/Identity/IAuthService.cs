using Microsoft.AspNetCore.Identity;
using System;
using System.Collections.Generic;
using System.Text;

namespace AgentGroupChat.Infrastructure.Identity
{
    public interface IAuthService
    {
        Task<SignInResult> LoginAsync(string username, string password);
        Task<IdentityResult> RegisterAsync(string username, string email, string password);
    }
}
