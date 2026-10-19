using System.Threading;
using System.Windows;

namespace ScreenTranslator;

public partial class App : Application
{
    private static Mutex? _mutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _mutex = new Mutex(true, "ScreenTranslator.SingleInstance", out bool createdNew);
        if (!createdNew)
        {
            MessageBox.Show("Screen Translator уже запущен. Заверши работу через меню «Выход».");
            Shutdown();
            return;
        }

        var main = new MainWindow();
        main.Initialize();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        TranslationService.Instance.Local.Dispose();
        _mutex?.Dispose();
        base.OnExit(e);
    }
}
