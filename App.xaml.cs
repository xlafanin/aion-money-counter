// Скопируйте и замените всё содержимое файла App.xaml.cs

using Microsoft.Extensions.DependencyInjection;
using System.Text;
using System.Windows;

namespace AionMoneyCounter
{
    public partial class App : Application
    {
        // ИЗМЕНЕНИЕ: Добавляем '?' чтобы указать, что свойство может быть null
        public static ServiceProvider? ServiceProvider { get; private set; }

        public App()
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

            var serviceCollection = new ServiceCollection();
            ConfigureServices(serviceCollection);
            ServiceProvider = serviceCollection.BuildServiceProvider();
        }

        private void ConfigureServices(IServiceCollection services)
        {
            services.AddSingleton<LogProcessorService>();
            services.AddSingleton<MainViewModel>();
            services.AddSingleton<MainWindow>();
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            // Добавляем проверку на null, так как ServiceProvider теперь nullable
            var mainWindow = ServiceProvider?.GetService<MainWindow>();
            mainWindow?.Show();
        }
    }
}