using System;
using System.Collections.Generic;
using System.Text;

namespace AgentGroupChat.Infrastructure.Seeding
{
    public interface IDataSeeder
    {
        Task SeedAsync();
    }
}
