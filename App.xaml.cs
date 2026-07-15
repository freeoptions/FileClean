namespace FileClean;

public partial class App : System.Windows.Application
{
    private const string SmokeTestArgument = "--smoke-test";

    private void App_Startup(object sender, System.Windows.StartupEventArgs e)
    {
        var isSmokeTest = e.Args.Any(argument => string.Equals(argument, SmokeTestArgument, StringComparison.OrdinalIgnoreCase));
        var window = new MainWindow
        {
            IsSmokeTestMode = isSmokeTest
        };

        MainWindow = window;

        if (isSmokeTest)
        {
            window.ShowInTaskbar = false;
            window.WindowState = System.Windows.WindowState.Minimized;
        }

        window.Show();
    }
}
