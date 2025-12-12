using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ZapJobs.Execution;

namespace ZapJobs.HostedServices;

/// <summary>
/// Registers job types with the executor on startup
/// </summary>
public class JobRegistrationHostedService : IHostedService
{
    private readonly IJobExecutor _executor;
    private readonly IEnumerable<IJobRegistration> _registrations;
    private readonly ILogger<JobRegistrationHostedService> _logger;

    public JobRegistrationHostedService(
        IJobExecutor executor,
        IEnumerable<IJobRegistration> registrations,
        ILogger<JobRegistrationHostedService> logger)
    {
        _executor = executor;
        _registrations = registrations;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        foreach (var registration in _registrations)
        {
            _executor.RegisterJobType(registration.JobType);
            _logger.LogInformation("Registered job type: {JobType}", registration.JobType.Name);
        }

        _logger.LogInformation("Registered {Count} job types", _registrations.Count());
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
