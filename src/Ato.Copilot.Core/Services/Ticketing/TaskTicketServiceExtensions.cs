using Microsoft.Extensions.DependencyInjection;

namespace Ato.Copilot.Core.Services.Ticketing;

public static class TaskTicketServiceExtensions
{
    public static IServiceCollection AddTaskTicketing(this IServiceCollection services)
    {
        services.AddScoped<TaskTicketService>();
        services.AddSingleton<TicketingCredentialResolver>();
        foreach (var name in new[] { "Jira", "ServiceNow" })
            services.AddHttpClient(name, client => client.Timeout = TimeSpan.FromSeconds(30))
                .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
        return services;
    }
}
