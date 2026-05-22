using System.Configuration;
using System.Data;
using System.Windows;

namespace AgentGroupChat;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
	protected override void OnStartup(StartupEventArgs e)
	{
		UiThemeManager.EnsureMutableBrushResources();
		base.OnStartup(e);
	}
}

