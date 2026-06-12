using Microsoft.AspNetCore.Components.Authorization;
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

        public string? UserName { get; private set; }
        public bool IsAuthenticated { get; private set; }

        public async Task InitializeAsync()
        {
            var state = await _auth.GetAuthenticationStateAsync();

            var user = state.User;

            IsAuthenticated = user.Identity?.IsAuthenticated == true;
            UserName = user.Identity?.Name;
        }
    }
}
