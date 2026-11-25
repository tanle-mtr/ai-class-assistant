namespace AiClassAssistant.Core.Ai;

/// <summary>Ollama 调用契约（跨层使用，签名严格对齐架构契约 §3）。</summary>
public interface IOllamaClient
{
    Task<List<ModelInfo>> ListModelsAsync();
    Task<string> GenerateAsync(string model, string prompt, CancellationToken ct = default);
    Task<List<double>> EmbedAsync(string model, string text, CancellationToken ct = default);
    Task<bool> EnsureModelAsync(string model);
    Task<bool> CreateModelAsync(string name, string baseModel, string systemPrompt);
}
