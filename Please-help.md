Listen.. I'm having major issues.

I've added a .BlazorServer project and a .Ui.Shared project to my app so that I can start trying to do a web port.

Created everything and the site is generally running like its supposed to- however.
Blazor server running over signal R apparently has issues with using the Asp.net identity code for 
_signInManager.SignInAsync which is trying to load a cookie value in the http headers.. it pukes and login (succeeds but fails at the same time)
so.. I dont really know where to go from here.
I started making an api route in my Program.cs but i dont think its hydrating the cookie properly this way either.

Files of importance:
AgentGroupChat.UI.Shared > Login.razor : complete login logic
AgentGroupChat.UI.Shared > MainLayout.razor : validating login/session cookie is good


AgentGroupChat.Infrastructure > Identity/ folder, /Migrations/ folder... 
AgentGroupChat.BlazorServer / Program.cs

can you please take a look and tell me if it would be best to just route out to a .cshtml page for login completely, or something else may stick out to you?

