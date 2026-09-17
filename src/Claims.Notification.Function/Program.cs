using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Claims.Notification.Function;

var host = new HostBuilder()
    .ConfigureFunctionsWebApplication()
    .ConfigureServices(services => services.AddSingleton<NotificationStore>())
    .Build();

host.Run();
