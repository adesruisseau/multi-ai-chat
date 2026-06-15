using Microsoft.AspNetCore.Components.Authorization;
using System.Security.Claims;
using System;
using System.Collections.Generic;
using System.Text;

namespace AgentGroupChat.Infrastructure.Identity
{
    public class UserContext : IUserContext
    {
        private readonly AuthenticationStateProvider _auth;

        public UserContext(AuthenticationStateProvider auth)
        {
            _auth = auth;
        }

        public string? UserId { get; private set; }
        public string? UserName { get; private set; }
        public bool IsAuthenticated { get; private set; }

        public async Task InitializeAsync()
        {
            var state = await _auth.GetAuthenticationStateAsync();

            var user = state.User;

            IsAuthenticated = user.Identity?.IsAuthenticated == true;
            UserId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            UserName = user.Identity?.Name;
        }

        public async Task<string> GetRequiredUserIdAsync()
        {
            await InitializeAsync();

            if (!IsAuthenticated || string.IsNullOrWhiteSpace(UserId))
            {
                return "";
            }

            return UserId;
        }
    }
}
