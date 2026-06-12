using System;
using System.Collections.Generic;
using System.Text;

namespace AgentGroupChat.Infrastructure.Identity
{
    public interface IUserContext
    {
        string? UserName { get; }
        bool IsAuthenticated { get; }
    }
}
