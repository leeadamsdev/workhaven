namespace Workhaven.Api.Features.Identity.EmailConfirmation;

internal sealed partial class ConfirmationEmailWorker(
    IServiceScopeFactory scopes, ILogger<ConfirmationEmailWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                if (await scope.ServiceProvider.GetRequiredService<ConfirmationEmailDelivery>().SendNextAsync(stoppingToken))
                {
                    continue;
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                // Provider exception messages can contain recipient details; log only the failure type.
                LogWorkerFailure(logger, exception.GetType().Name);
            }

            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Confirmation email processing failed ({FailureType}); processing will retry.")]
    private static partial void LogWorkerFailure(ILogger logger, string failureType);
}
