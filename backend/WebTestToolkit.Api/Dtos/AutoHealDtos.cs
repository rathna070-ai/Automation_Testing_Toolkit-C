namespace WebTestToolkit.Api.Dtos;

public record LocatorKeyDto(string Key, string Strategy, string Value);

public record LocatorPageDto(string Page, string Url, IReadOnlyList<LocatorKeyDto> Keys);

public record AutoHealStartRequest(string Page, string Key);

public record AutoHealApplyRequest(string Page, string Key, string Strategy, string Value);
