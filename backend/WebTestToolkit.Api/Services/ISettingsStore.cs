using WebTestToolkit.Contracts.Models;

namespace WebTestToolkit.Api.Services;

public interface ISettingsStore
{
    Task<AppSettings> GetAsync(CancellationToken ct = default);
    Task SaveAsync(AppSettings settings, CancellationToken ct = default);
}
