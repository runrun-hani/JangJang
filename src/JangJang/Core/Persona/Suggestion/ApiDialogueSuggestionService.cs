using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace JangJang.Core.Persona.Suggestion;

/// <summary>
/// 대사 추천 서비스. 프로바이더별로 적절한 API 형식을 사용한다.
/// - Gemini: 네이티브 REST API (?key= 쿼리 파라미터, Authorization 헤더 없음)
/// - Groq/Cerebras/기타: OpenAI 호환 REST API (Authorization: Bearer 헤더)
/// </summary>
public sealed class ApiDialogueSuggestionService : IDialogueSuggestionService
{
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(30) };

    private readonly string _provider;
    private readonly string _baseUrl;
    private readonly string _apiKey;
    private readonly string _model;

    private const string GeminiBaseUrl = "https://generativelanguage.googleapis.com/v1beta";
    private const string GeminiDefaultModel = "gemini-2.5-flash-lite";

    public ApiDialogueSuggestionService(string apiKey, string? baseUrl = null, string? model = null)
    {
        _provider = "gemini";
        _apiKey = apiKey;
        _baseUrl = baseUrl ?? GeminiBaseUrl;
        _model = model ?? GeminiDefaultModel;
    }

    public static ApiDialogueSuggestionService? FromSettings(AppSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.SuggestionApiKey))
            return null;
        return new ApiDialogueSuggestionService(
            settings.SuggestionApiKey,
            settings.SuggestionApiBaseUrl,
            settings.SuggestionApiModel);
    }

    private bool IsGemini => _provider == "gemini";

    /// <summary>
    /// 텍스트 생성용이 아닌 모델을 필터링하기 위한 키워드.
    /// </summary>
    private static readonly string[] ExcludeKeywords =
        { "tts", "image", "vision", "embedding", "robotics", "nano", "aqa", "bisheng" };

    /// <summary>
    /// 주어진 API 키로 실제 무료 사용 가능한 Gemini 텍스트 생성 모델만 반환한다.
    /// 1. 모델 목록 조회 → 이름/기능 필터 → 실제 호출 테스트 → 성공한 것만 반환.
    /// progress 콜백으로 진행 상황을 알린다.
    /// </summary>
    public static async Task<List<(string Name, bool Recommended)>> DiscoverAvailableModelsAsync(
        string apiKey, Action<string>? progress = null)
    {
        var results = new List<(string Name, bool Recommended)>();
        var candidates = new List<string>();

        try
        {
            progress?.Invoke("모델 목록 조회 중...");
            var url = $"{GeminiBaseUrl}/models?key={apiKey}";
            var response = await _http.GetAsync(url);
            if (!response.IsSuccessStatusCode) return results;

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("models", out var models)) return results;

            foreach (var model in models.EnumerateArray())
            {
                var name = model.GetProperty("name").GetString();
                if (name == null) continue;

                var shortName = name.StartsWith("models/") ? name[7..] : name;

                // generateContent 지원 확인
                if (model.TryGetProperty("supportedGenerationMethods", out var methods))
                {
                    var ok = false;
                    foreach (var m in methods.EnumerateArray())
                        if (m.GetString() == "generateContent") { ok = true; break; }
                    if (!ok) continue;
                }

                // 이름 기반 필터: 텍스트 생성과 무관한 모델 제외
                var lower = shortName.ToLowerInvariant();
                if (ExcludeKeywords.Any(kw => lower.Contains(kw))) continue;
                if (lower.Contains("pro")) continue; // pro 모델은 대부분 유료

                candidates.Add(shortName);
            }
        }
        catch { return results; }

        // 각 후보 모델을 실제 호출 테스트
        int tested = 0;
        foreach (var model in candidates)
        {
            tested++;
            progress?.Invoke($"모델 테스트 중... ({tested}/{candidates.Count}) {model}");
            var ok = await TestModelAsync(apiKey, model);
            if (ok)
            {
                var recommended = model.Contains("flash") && model.Contains("lite");
                results.Add((model, recommended));
            }
        }

        // 추천 모델 먼저, 그 다음 이름순
        results.Sort((a, b) =>
        {
            if (a.Recommended != b.Recommended) return b.Recommended.CompareTo(a.Recommended);
            return string.Compare(a.Name, b.Name, StringComparison.Ordinal);
        });

        return results;
    }

    /// <summary>
    /// 특정 모델로 간단한 호출을 시도하여 실제 사용 가능한지 확인한다.
    /// </summary>
    public static async Task<bool> TestModelAsync(string apiKey, string model)
    {
        try
        {
            var url = $"{GeminiBaseUrl}/models/{model}:generateContent?key={apiKey}";
            var body = JsonSerializer.Serialize(new
            {
                contents = new[] { new { parts = new[] { new { text = "Hi" } } } },
                generationConfig = new { maxOutputTokens = 5 }
            });
            var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
            var response = await _http.SendAsync(request);
            return response.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    public async Task<List<SuggestedLine>> SuggestAsync(SuggestionContext context, int count = 3)
    {
        var systemPrompt = PromptBuilder.BuildSystemPrompt();
        var userPrompt = PromptBuilder.BuildUserPrompt(context, count);

        var response = IsGemini
            ? await CallGeminiNative(systemPrompt, userPrompt)
            : await CallOpenAiCompat(systemPrompt, userPrompt, 200, 0.8);

        return ParseTextToLines(response);
    }

    public async Task<string?> TestConnectionAsync()
    {
        try
        {
            var response = IsGemini
                ? await CallGeminiNativeRaw("Hi", null)
                : await CallOpenAiCompatRaw("Hi", null, 10);

            if (response.IsSuccessStatusCode)
                return null;

            var body = await response.Content.ReadAsStringAsync();
            return $"{(int)response.StatusCode}: {body}";
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    // --- Gemini 네이티브 API ---

    private async Task<string> CallGeminiNative(string systemPrompt, string userPrompt)
    {
        var resp = await CallGeminiNativeRaw(userPrompt, systemPrompt);
        resp.EnsureSuccessStatusCode();
        var json = await resp.Content.ReadAsStringAsync();
        return ParseGeminiResponse(json);
    }

    private async Task<HttpResponseMessage> CallGeminiNativeRaw(string userText, string? systemText)
    {
        var url = $"{_baseUrl}/models/{_model}:generateContent?key={_apiKey}";

        object requestBody;
        if (!string.IsNullOrEmpty(systemText))
        {
            requestBody = new
            {
                systemInstruction = new { parts = new[] { new { text = systemText } } },
                contents = new[] { new { parts = new[] { new { text = userText } } } },
                generationConfig = new { temperature = 0.8, maxOutputTokens = 200 }
            };
        }
        else
        {
            requestBody = new
            {
                contents = new[] { new { parts = new[] { new { text = userText } } } },
                generationConfig = new { maxOutputTokens = 10 }
            };
        }

        var jsonContent = JsonSerializer.Serialize(requestBody);
        var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(jsonContent, Encoding.UTF8, "application/json")
        };
        // Gemini 네이티브: Authorization 헤더 없음, ?key= 만 사용
        return await _http.SendAsync(request);
    }

    private static string ParseGeminiResponse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement
                .GetProperty("candidates")[0]
                .GetProperty("content")
                .GetProperty("parts")[0]
                .GetProperty("text")
                .GetString() ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    // --- OpenAI 호환 API (Groq, Cerebras 등) ---

    private async Task<string> CallOpenAiCompat(string systemPrompt, string userPrompt, int maxTokens, double temperature)
    {
        var resp = await CallOpenAiCompatRaw(userPrompt, systemPrompt, maxTokens, temperature);
        resp.EnsureSuccessStatusCode();
        var json = await resp.Content.ReadAsStringAsync();
        return ParseOpenAiResponse(json);
    }

    private async Task<HttpResponseMessage> CallOpenAiCompatRaw(string userText, string? systemText, int maxTokens, double temperature = 0.7)
    {
        var messages = new List<object>();
        if (!string.IsNullOrEmpty(systemText))
            messages.Add(new { role = "system", content = systemText });
        messages.Add(new { role = "user", content = userText });

        var requestBody = new
        {
            model = _model,
            messages,
            max_tokens = maxTokens,
            temperature
        };

        var jsonContent = JsonSerializer.Serialize(requestBody);
        var request = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/chat/completions")
        {
            Content = new StringContent(jsonContent, Encoding.UTF8, "application/json")
        };
        request.Headers.Add("Authorization", $"Bearer {_apiKey}");
        return await _http.SendAsync(request);
    }

    private static string ParseOpenAiResponse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString() ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    // --- 공통 파싱 ---

    internal static List<SuggestedLine> ParseResponse(string responseJson)
    {
        // OpenAI 형식 응답 파싱 (하위 호환 + 테스트용)
        var text = ParseOpenAiResponse(responseJson);
        return ParseTextToLines(text);
    }

    internal static List<SuggestedLine> ParseTextToLines(string text)
    {
        var results = new List<SuggestedLine>();
        if (string.IsNullOrWhiteSpace(text))
            return results;

        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var line in lines)
        {
            var cleaned = CleanLine(line);
            if (!string.IsNullOrWhiteSpace(cleaned))
                results.Add(new SuggestedLine { Text = cleaned });
        }
        return results;
    }

    internal static string CleanLine(string line)
    {
        var s = line.Trim();
        if (s.Length > 2 && char.IsDigit(s[0]) && (s[1] == '.' || s[1] == ')'))
            s = s[2..].TrimStart();
        else if (s.Length > 3 && char.IsDigit(s[0]) && char.IsDigit(s[1]) && (s[2] == '.' || s[2] == ')'))
            s = s[3..].TrimStart();
        else if (s.StartsWith("- ") || s.StartsWith("* "))
            s = s[2..].TrimStart();

        if (s.Length >= 2 && s[0] == '"' && s[^1] == '"')
            s = s[1..^1];
        if (s.Length >= 2 && s[0] == '\u201C' && s[^1] == '\u201D')
            s = s[1..^1];

        return s.Trim();
    }
}
