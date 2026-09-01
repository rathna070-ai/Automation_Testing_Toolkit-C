using Microsoft.AspNetCore.Mvc;
using WebTestToolkit.Api.Dtos;
using WebTestToolkit.Api.Services;
using WebTestToolkit.Contracts.Models;

namespace WebTestToolkit.Api.Controllers;

[ApiController]
[Route("api/settings")]
public class SettingsController : ControllerBase
{
    private readonly ISettingsStore _settingsStore;

    public SettingsController(ISettingsStore settingsStore)
    {
        _settingsStore = settingsStore;
    }

    [HttpGet]
    public async Task<ActionResult<SettingsResponse>> Get(CancellationToken ct)
    {
        var settings = await _settingsStore.GetAsync(ct);
        return Ok(new SettingsResponse(settings.GroqModel, !string.IsNullOrWhiteSpace(settings.GroqApiKey)));
    }

    [HttpPut]
    public async Task<ActionResult<SettingsResponse>> Update([FromBody] UpdateSettingsRequest request, CancellationToken ct)
    {
        var current = await _settingsStore.GetAsync(ct);

        var updated = new AppSettings
        {
            GroqApiKey = request.GroqApiKey ?? current.GroqApiKey,
            GroqModel = string.IsNullOrWhiteSpace(request.GroqModel) ? current.GroqModel : request.GroqModel
        };

        await _settingsStore.SaveAsync(updated, ct);
        return Ok(new SettingsResponse(updated.GroqModel, !string.IsNullOrWhiteSpace(updated.GroqApiKey)));
    }
}
