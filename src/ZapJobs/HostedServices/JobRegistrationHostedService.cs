using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ZapJobs.Core;
using ZapJobs.Execution;

namespace ZapJobs.HostedServices;

/// <summary>
/// Registers job types with the executor on startup
/// </summary>
public class JobRegistrationHostedService : IHostedService
{
    private readonly IJobExecutor _executor;
    private readonly IJobStorage _storage;
    private readonly IEnumerable<IJobRegistration> _registrations;
    private readonly ILogger<JobRegistrationHostedService> _logger;

    public JobRegistrationHostedService(
        IJobExecutor executor,
        IJobStorage storage,
        IEnumerable<IJobRegistration> registrations,
        ILogger<JobRegistrationHostedService> logger)
    {
        _executor = executor;
        _storage = storage;
        _registrations = registrations;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        foreach (var registration in _registrations)
        {
            _executor.RegisterJobType(registration.JobType);
            _logger.LogInformation("Registered job type: {JobType}", registration.JobType.Name);

            // Apply builder configuration to job definition if provided
            if (registration.Builder != null)
            {
                await ApplyBuilderConfigurationAsync(registration, cancellationToken);
            }
        }

        _logger.LogInformation("Registered {Count} job types", _registrations.Count());
    }

    private async Task ApplyBuilderConfigurationAsync(IJobRegistration registration, CancellationToken ct)
    {
        // Get job type ID from the job instance
        var job = (IJob)Activator.CreateInstance(registration.JobType)!;
        var jobTypeId = job.JobTypeId;

        // Get or create job definition
        var definition = await _storage.GetJobDefinitionAsync(jobTypeId, ct);
        if (definition == null)
        {
            definition = new JobDefinition
            {
                JobTypeId = jobTypeId,
                DisplayName = jobTypeId,
                CreatedAt = DateTime.UtcNow
            };
        }

        // Apply builder configuration
        registration.Builder!.ApplyTo(definition);
        definition.UpdatedAt = DateTime.UtcNow;

        await _storage.UpsertDefinitionAsync(definition, ct);

        _logger.LogDebug(
            "Applied configuration for job {JobTypeId}: PreventOverlapping={PreventOverlapping}",
            jobTypeId,
            definition.PreventOverlapping);
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
