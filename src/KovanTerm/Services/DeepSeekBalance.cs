using System.Diagnostics;
using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using KovanTerm.Models;

namespace KovanTerm.Services;

/// <summary>One currency's balance on the DeepSeek account.</summary>
public sealed record DeepSeekAmount(string Currency, decimal Total, decimal Granted, decimal ToppedUp)
{
    public string Text => (Currency switch { "USD" => "$", "CNY" => "¥", _ => Currency + " " }) + Total.ToString("0.00", CultureInfo.InvariantCulture);
}

/// <summary>The account's balance: what's left, and whether it can still be used.</summary>
public sealed record DeepSeekAccount(IReadOnlyList<DeepSeekAmount> Amounts, bool IsAvailable)
{
    /// <summary>"$4.20", or "$4.20 · ¥30.00" with both currencies.</summary>
    public string Text => Amounts.Count == 0 ? "—" : string.Join(" · ", Amounts.Select(a => a.Text));

    /// <summary>Little or nothing left: worth a warning colour.</summary>
    public bool IsLow => !IsAvailable || Amounts.All(a => a.Total < (a.Currency == "CNY" ? 7m : 1m));

    public string Details => string.Join("\n", Amounts.Select(a =>
        $"{a.Currency}: {a.Total.ToString("0.00", CultureInfo.InvariantCulture)} (topped up {a.ToppedUp.ToString("0.00", CultureInfo.InvariantCulture)}, granted {a.Granted.ToString("0.00", CultureInfo.InvariantCulture)})"))
        + (IsAvailable ? "" : "\nNot enough to make requests");
}

/// <summary>
/// The DeepSeek balance (#132), from DeepSeek's own <c>/user/balance</c> with the key a DeepSeek profile already uses
/// (saved by KovanLaunch in Credential Manager). The key goes only to DeepSeek, which issued it; without a DeepSeek
/// profile or a saved key nothing is asked and the section stays hidden.
/// </summary>
public sealed class DeepSeekBalance(HttpClient http, Func<string, string?>? readKey = null)
{
    private const string Url = "https://api.deepseek.com/user/balance";
    private readonly Func<string, string?> _readKey = readKey ?? Interop.Credentials.Read;

    /// <summary>
    /// The Credential Manager key name of a profile that talks to DeepSeek's API (<c>secret:deepseek</c> in the built-in
    /// one; a copied profile may use another name).
    /// </summary>
    public static string? KeyName(IEnumerable<TerminalProfile> profiles)
    {
        foreach (var p in profiles)
        {
            if (p.Environment is not { } env) continue;
            if (!env.TryGetValue("ANTHROPIC_BASE_URL", out var url) || !url.Contains("api.deepseek.com", StringComparison.OrdinalIgnoreCase)) continue;
            foreach (var name in (string[])["ANTHROPIC_AUTH_TOKEN", "ANTHROPIC_API_KEY"])
            {
                if (env.TryGetValue(name, out var value) && value.StartsWith(EnvironmentLauncher.SecretPrefix, StringComparison.Ordinal)
                    && value[EnvironmentLauncher.SecretPrefix.Length..].Trim() is { Length: > 0 } key)
                    return key;
            }
        }
        return null;
    }

    /// <summary>The balance, or <c>null</c> when there's no key or DeepSeek didn't answer.</summary>
    public async Task<DeepSeekAccount?> ReadAsync(string keyName, CancellationToken ct = default)
    {
        if (_readKey(keyName) is not { Length: > 0 } key) return null;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, Url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
            request.Headers.Accept.ParseAdd("application/json");
            request.Headers.UserAgent.ParseAdd("KovanTerm");
            using var response = await http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode) return null;
            return Parse(await response.Content.ReadAsStringAsync(ct));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            Debug.WriteLine($"Couldn't read the DeepSeek balance: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// <c>{"is_available": true, "balance_infos": [{"currency": "USD", "total_balance": "4.20", "granted_balance": "0.00",
    /// "topped_up_balance": "4.20"}]}</c>; amounts are strings.
    /// </summary>
    public static DeepSeekAccount? Parse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("balance_infos", out var infos) || infos.ValueKind != JsonValueKind.Array)
                return null;
            var amounts = new List<DeepSeekAmount>();
            foreach (var i in infos.EnumerateArray())
            {
                if (i.ValueKind != JsonValueKind.Object || !i.TryGetProperty("currency", out var c) || c.GetString() is not { Length: > 0 } currency) continue;
                amounts.Add(new DeepSeekAmount(currency, Amount(i, "total_balance"), Amount(i, "granted_balance"), Amount(i, "topped_up_balance")));
            }
            var available = !root.TryGetProperty("is_available", out var a) || a.ValueKind != JsonValueKind.False;
            return new DeepSeekAccount(amounts, available);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static decimal Amount(JsonElement e, string name)
    {
        if (!e.TryGetProperty(name, out var v)) return 0;
        if (v.ValueKind == JsonValueKind.Number) return v.TryGetDecimal(out var n) ? n : 0;
        return v.ValueKind == JsonValueKind.String
            && decimal.TryParse(v.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var s) ? s : 0;
    }
}
