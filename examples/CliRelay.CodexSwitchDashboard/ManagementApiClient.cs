using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CliRelay.CodexSwitchDashboard;

internal sealed class ManagementApiClient : IDisposable
{
    public static readonly JsonSerializerOptions ReadJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static readonly JsonSerializerOptions PrettyJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private static readonly JsonSerializerOptions WriteJsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(20)
    };

    public string BaseUrl { get; set; } = "http://127.0.0.1:8317";

    public string ManagementKey { get; set; } = string.Empty;

    public Task<AuthFilesResponse?> GetAuthFilesAsync()
    {
        return GetJsonAsync<AuthFilesResponse>("/v0/management/auth-files");
    }

    public Task<DashboardSummaryResponse?> GetDashboardSummaryAsync(int days = 7)
    {
        return GetJsonAsync<DashboardSummaryResponse>($"/v0/management/dashboard-summary?days={days}");
    }

    public Task<SystemStatsResponse?> GetSystemStatsAsync()
    {
        return GetJsonAsync<SystemStatsResponse>("/v0/management/system-stats");
    }

    public Task<IdentityFingerprintResponse?> GetIdentityFingerprintAsync()
    {
        return GetJsonAsync<IdentityFingerprintResponse>("/v0/management/identity-fingerprint");
    }

    public Task<BooleanEnvelope?> GetBooleanSettingAsync(string path)
    {
        return GetJsonAsync<BooleanEnvelope>(path);
    }

    public Task PutBooleanSettingAsync(string path, bool value)
    {
        return SendJsonAsync(HttpMethod.Put, path, new { value });
    }

    public Task<ModelListResponse?> GetModelsAsync()
    {
        return GetJsonAsync<ModelListResponse>("/v0/management/models");
    }

    public Task<UsageLogsResponse?> GetUsageLogsAsync(int days, int size)
    {
        return GetJsonAsync<UsageLogsResponse>($"/v0/management/usage/logs?days={days}&page=1&size={size}");
    }

    public Task<TextLogsResponse?> GetSystemLogsAsync(int limit)
    {
        return GetJsonAsync<TextLogsResponse>($"/v0/management/logs?limit={limit}");
    }

    public async Task<string> DownloadAuthFileJsonAsync(string name)
    {
        using var response = await SendAsync(HttpMethod.Get, $"/v0/management/auth-files/download?name={Uri.EscapeDataString(name)}");
        return await response.Content.ReadAsStringAsync();
    }

    public Task PatchAuthStatusAsync(string name, bool disabled)
    {
        return SendJsonAsync(HttpMethod.Patch, "/v0/management/auth-files/status", new AuthStatusPatchRequest
        {
            Name = name,
            Disabled = disabled
        });
    }

    public Task PatchAuthFieldsAsync(AuthFieldPatchRequest request)
    {
        return SendJsonAsync(HttpMethod.Patch, "/v0/management/auth-files/fields", request);
    }

    public Task ReconcileQuotaAsync(string authIndex)
    {
        return SendJsonAsync(HttpMethod.Post, "/v0/management/quota/reconcile", new { auth_index = authIndex });
    }

    private async Task<T?> GetJsonAsync<T>(string path)
    {
        using var response = await SendAsync(HttpMethod.Get, path);
        var content = await response.Content.ReadAsStringAsync();
        if (string.IsNullOrWhiteSpace(content))
        {
            return default;
        }

        return JsonSerializer.Deserialize<T>(content, ReadJsonOptions);
    }

    private async Task SendJsonAsync(HttpMethod method, string path, object payload)
    {
        var json = JsonSerializer.Serialize(payload, WriteJsonOptions);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var response = await SendAsync(method, path, content);
        _ = await response.Content.ReadAsStringAsync();
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, HttpContent? content = null)
    {
        var request = new HttpRequestMessage(method, BuildUrl(path))
        {
            Content = content
        };

        if (!string.IsNullOrWhiteSpace(ManagementKey))
        {
            request.Headers.TryAddWithoutValidation("X-Management-Key", ManagementKey.Trim());
        }

        var response = await _httpClient.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            response.Dispose();
            throw new InvalidOperationException($"{method} {BuildUrl(path)}\n{(int)response.StatusCode} {response.ReasonPhrase}\n{body}");
        }

        return response;
    }

    private string BuildUrl(string path)
    {
        if (Uri.TryCreate(path, UriKind.Absolute, out var absolute))
        {
            return absolute.ToString();
        }

        var baseUrl = (BaseUrl ?? string.Empty).Trim().TrimEnd('/');
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            throw new InvalidOperationException("Management base URL is required.");
        }

        return $"{baseUrl}/{path.TrimStart('/')}";
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }
}

internal sealed class BooleanEnvelope
{
    [JsonExtensionData]
    public Dictionary<string, JsonElement> Data { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public bool? GetFirstBoolean()
    {
        foreach (var value in Data.Values)
        {
            if (value.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                return value.GetBoolean();
            }
        }

        return null;
    }
}

internal sealed class DashboardSummaryResponse
{
    [JsonPropertyName("kpi")]
    public DashboardKpi? Kpi { get; set; }

    [JsonPropertyName("counts")]
    public DashboardCounts? Counts { get; set; }

    [JsonPropertyName("days")]
    public int Days { get; set; }

    [JsonPropertyName("meta")]
    public DashboardMeta? Meta { get; set; }
}

internal sealed class DashboardKpi
{
    [JsonPropertyName("total_requests")]
    public long TotalRequests { get; set; }

    [JsonPropertyName("success_requests")]
    public long SuccessRequests { get; set; }

    [JsonPropertyName("failed_requests")]
    public long FailedRequests { get; set; }

    [JsonPropertyName("success_rate")]
    public double SuccessRate { get; set; }

    [JsonPropertyName("input_tokens")]
    public long InputTokens { get; set; }

    [JsonPropertyName("output_tokens")]
    public long OutputTokens { get; set; }

    [JsonPropertyName("reasoning_tokens")]
    public long ReasoningTokens { get; set; }

    [JsonPropertyName("cached_tokens")]
    public long CachedTokens { get; set; }

    [JsonPropertyName("total_tokens")]
    public long TotalTokens { get; set; }

    [JsonPropertyName("total_cost")]
    public double TotalCost { get; set; }
}

internal sealed class DashboardCounts
{
    [JsonPropertyName("api_keys")]
    public int ApiKeys { get; set; }

    [JsonPropertyName("providers_total")]
    public int ProvidersTotal { get; set; }

    [JsonPropertyName("gemini_keys")]
    public int GeminiKeys { get; set; }

    [JsonPropertyName("claude_keys")]
    public int ClaudeKeys { get; set; }

    [JsonPropertyName("codex_keys")]
    public int CodexKeys { get; set; }

    [JsonPropertyName("vertex_keys")]
    public int VertexKeys { get; set; }

    [JsonPropertyName("openai_providers")]
    public int OpenAIProviders { get; set; }

    [JsonPropertyName("auth_files")]
    public int AuthFiles { get; set; }
}

internal sealed class DashboardMeta
{
    [JsonPropertyName("generated_at")]
    public DateTimeOffset? GeneratedAt { get; set; }
}

internal sealed class SystemStatsResponse
{
    [JsonPropertyName("db_size_bytes")]
    public long DbSizeBytes { get; set; }

    [JsonPropertyName("log_content_store_bytes")]
    public long LogContentStoreBytes { get; set; }

    [JsonPropertyName("log_dir_size_bytes")]
    public long LogDirSizeBytes { get; set; }

    [JsonPropertyName("process_mem_bytes")]
    public ulong ProcessMemBytes { get; set; }

    [JsonPropertyName("process_mem_pct")]
    public double ProcessMemPct { get; set; }

    [JsonPropertyName("process_cpu_pct")]
    public double ProcessCpuPct { get; set; }

    [JsonPropertyName("go_routines")]
    public int GoRoutines { get; set; }

    [JsonPropertyName("go_heap_bytes")]
    public ulong GoHeapBytes { get; set; }

    [JsonPropertyName("system_cpu_pct")]
    public double SystemCpuPct { get; set; }

    [JsonPropertyName("system_mem_total")]
    public ulong SystemMemTotal { get; set; }

    [JsonPropertyName("system_mem_used")]
    public ulong SystemMemUsed { get; set; }

    [JsonPropertyName("system_mem_pct")]
    public double SystemMemPct { get; set; }

    [JsonPropertyName("net_bytes_sent")]
    public ulong NetBytesSent { get; set; }

    [JsonPropertyName("net_bytes_recv")]
    public ulong NetBytesRecv { get; set; }

    [JsonPropertyName("net_send_rate")]
    public double NetSendRate { get; set; }

    [JsonPropertyName("net_recv_rate")]
    public double NetRecvRate { get; set; }

    [JsonPropertyName("disk_total")]
    public ulong DiskTotal { get; set; }

    [JsonPropertyName("disk_used")]
    public ulong DiskUsed { get; set; }

    [JsonPropertyName("disk_free")]
    public ulong DiskFree { get; set; }

    [JsonPropertyName("disk_pct")]
    public double DiskPct { get; set; }

    [JsonPropertyName("uptime_seconds")]
    public long UptimeSeconds { get; set; }

    [JsonPropertyName("start_time")]
    public DateTimeOffset? StartTime { get; set; }

    [JsonPropertyName("channel_latency")]
    public List<ChannelLatencyEntry> ChannelLatency { get; set; } = new();

    [JsonPropertyName("active_concurrency")]
    public List<ActiveConcurrencyEntry> ActiveConcurrency { get; set; } = new();

    [JsonPropertyName("total_in_flight")]
    public long TotalInFlight { get; set; }

    [JsonPropertyName("total_rpm")]
    public int TotalRpm { get; set; }

    [JsonPropertyName("total_tpm")]
    public long TotalTpm { get; set; }
}

internal sealed class ChannelLatencyEntry
{
    [JsonPropertyName("source")]
    public string? Source { get; set; }

    [JsonPropertyName("count")]
    public long Count { get; set; }

    [JsonPropertyName("avg_ms")]
    public double AvgMs { get; set; }
}

internal sealed class ActiveConcurrencyEntry
{
    [JsonPropertyName("api_key")]
    public string? ApiKey { get; set; }

    [JsonPropertyName("rpm")]
    public int Rpm { get; set; }

    [JsonPropertyName("tpm")]
    public long Tpm { get; set; }

    [JsonPropertyName("rpm_limit")]
    public int RpmLimit { get; set; }

    [JsonPropertyName("tpm_limit")]
    public int TpmLimit { get; set; }
}

internal sealed class IdentityFingerprintResponse
{
    [JsonPropertyName("identity-fingerprint")]
    public IdentityFingerprintPayload? IdentityFingerprint { get; set; }
}

internal sealed class IdentityFingerprintPayload
{
    [JsonPropertyName("codex")]
    public CodexIdentityFingerprintPayload? Codex { get; set; }
}

internal sealed class CodexIdentityFingerprintPayload
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; }

    [JsonPropertyName("user-agent")]
    public string? UserAgent { get; set; }

    [JsonPropertyName("session-mode")]
    public string? SessionMode { get; set; }
}

internal sealed class AuthFilesResponse
{
    [JsonPropertyName("files")]
    public List<AuthFileEntry> Files { get; set; } = new();
}

internal sealed class AuthFileEntry
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("auth_index")]
    public string? AuthIndex { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("provider")]
    public string? Provider { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("label")]
    public string? Label { get; set; }

    [JsonPropertyName("email")]
    public string? Email { get; set; }

    [JsonPropertyName("account")]
    public string? Account { get; set; }

    [JsonPropertyName("account_type")]
    public string? AccountType { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("status_message")]
    public string? StatusMessage { get; set; }

    [JsonPropertyName("plan_type")]
    public string? PlanType { get; set; }

    [JsonPropertyName("disabled")]
    public bool Disabled { get; set; }

    [JsonPropertyName("unavailable")]
    public bool Unavailable { get; set; }

    [JsonPropertyName("runtime_only")]
    public bool RuntimeOnly { get; set; }

    [JsonPropertyName("source")]
    public string? Source { get; set; }

    [JsonPropertyName("path")]
    public string? Path { get; set; }

    [JsonPropertyName("size")]
    public long Size { get; set; }

    [JsonPropertyName("created_at")]
    public DateTimeOffset? CreatedAt { get; set; }

    [JsonPropertyName("updated_at")]
    public DateTimeOffset? UpdatedAt { get; set; }

    [JsonPropertyName("modtime")]
    public DateTimeOffset? Modtime { get; set; }

    [JsonPropertyName("last_refresh")]
    public DateTimeOffset? LastRefresh { get; set; }

    [JsonPropertyName("next_retry_after")]
    public DateTimeOffset? NextRetryAfter { get; set; }

    [JsonPropertyName("default_tags")]
    public List<string> DefaultTags { get; set; } = new();

    [JsonPropertyName("custom_tags")]
    public List<string> CustomTags { get; set; } = new();

    [JsonPropertyName("hidden_default_tags")]
    public List<string> HiddenDefaultTags { get; set; } = new();

    [JsonPropertyName("display_tags")]
    public List<string> DisplayTags { get; set; } = new();

    [JsonPropertyName("restrictions")]
    public List<AuthRestrictionEntry> Restrictions { get; set; } = new();
}

internal sealed class AuthRestrictionEntry
{
    [JsonPropertyName("scope")]
    public string? Scope { get; set; }

    [JsonPropertyName("model")]
    public string? Model { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("status_message")]
    public string? StatusMessage { get; set; }

    [JsonPropertyName("reason")]
    public string? Reason { get; set; }

    [JsonPropertyName("code")]
    public string? Code { get; set; }

    [JsonPropertyName("http_status")]
    public int? HttpStatus { get; set; }

    [JsonPropertyName("unavailable")]
    public bool Unavailable { get; set; }

    [JsonPropertyName("quota_exceeded")]
    public bool QuotaExceeded { get; set; }

    [JsonPropertyName("next_retry_after")]
    public DateTimeOffset? NextRetryAfter { get; set; }
}

internal sealed class ModelListResponse
{
    [JsonPropertyName("data")]
    public List<ModelEntry> Data { get; set; } = new();
}

internal sealed class ModelEntry
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("object")]
    public string? Object { get; set; }

    [JsonPropertyName("owned_by")]
    public string? OwnedBy { get; set; }

    [JsonPropertyName("pricing")]
    public ModelPricingPayload? Pricing { get; set; }
}

internal sealed class ModelPricingPayload
{
    [JsonPropertyName("input_price_per_million")]
    public double InputPricePerMillion { get; set; }

    [JsonPropertyName("output_price_per_million")]
    public double OutputPricePerMillion { get; set; }

    [JsonPropertyName("cached_price_per_million")]
    public double CachedPricePerMillion { get; set; }
}

internal sealed class UsageLogsResponse
{
    [JsonPropertyName("items")]
    public List<UsageLogItem> Items { get; set; } = new();

    [JsonPropertyName("total")]
    public long Total { get; set; }

    [JsonPropertyName("page")]
    public int Page { get; set; }

    [JsonPropertyName("size")]
    public int Size { get; set; }

    [JsonPropertyName("stats")]
    public UsageLogStats? Stats { get; set; }
}

internal sealed class UsageLogStats
{
    [JsonPropertyName("total")]
    public long Total { get; set; }

    [JsonPropertyName("success_rate")]
    public double SuccessRate { get; set; }

    [JsonPropertyName("total_tokens")]
    public long TotalTokens { get; set; }

    [JsonPropertyName("total_cost")]
    public double TotalCost { get; set; }
}

internal sealed class UsageLogItem
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("timestamp")]
    public DateTimeOffset? Timestamp { get; set; }

    [JsonPropertyName("api_key")]
    public string? ApiKey { get; set; }

    [JsonPropertyName("api_key_name")]
    public string? ApiKeyName { get; set; }

    [JsonPropertyName("model")]
    public string? Model { get; set; }

    [JsonPropertyName("source")]
    public string? Source { get; set; }

    [JsonPropertyName("channel_name")]
    public string? ChannelName { get; set; }

    [JsonPropertyName("auth_index")]
    public string? AuthIndex { get; set; }

    [JsonPropertyName("failed")]
    public bool Failed { get; set; }

    [JsonPropertyName("latency_ms")]
    public long LatencyMs { get; set; }

    [JsonPropertyName("first_token_ms")]
    public long FirstTokenMs { get; set; }

    [JsonPropertyName("input_tokens")]
    public long InputTokens { get; set; }

    [JsonPropertyName("output_tokens")]
    public long OutputTokens { get; set; }

    [JsonPropertyName("reasoning_tokens")]
    public long ReasoningTokens { get; set; }

    [JsonPropertyName("cached_tokens")]
    public long CachedTokens { get; set; }

    [JsonPropertyName("total_tokens")]
    public long TotalTokens { get; set; }

    [JsonPropertyName("cost")]
    public double Cost { get; set; }

    [JsonPropertyName("has_content")]
    public bool HasContent { get; set; }
}

internal sealed class TextLogsResponse
{
    [JsonPropertyName("lines")]
    public List<string> Lines { get; set; } = new();

    [JsonPropertyName("line-count")]
    public int LineCount { get; set; }

    [JsonPropertyName("latest-timestamp")]
    public long LatestTimestamp { get; set; }
}

internal sealed class AuthStatusPatchRequest
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("disabled")]
    public bool Disabled { get; set; }
}

internal sealed class AuthFieldPatchRequest
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("label")]
    public string? Label { get; set; }

    [JsonPropertyName("prefix")]
    public string? Prefix { get; set; }

    [JsonPropertyName("proxy_url")]
    public string? ProxyUrl { get; set; }

    [JsonPropertyName("proxy_id")]
    public string? ProxyId { get; set; }
}
