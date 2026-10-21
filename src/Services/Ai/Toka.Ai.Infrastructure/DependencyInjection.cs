using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using Toka.Ai.Application;
using Toka.Ai.Application.UseCases;
using Toka.Ai.Infrastructure.Knowledge;
using Toka.Ai.Infrastructure.Llm;
using Toka.Ai.Infrastructure.Metrics;
using Toka.Ai.Infrastructure.Tools;
using Toka.Ai.Infrastructure.VectorStore;
using Toka.BuildingBlocks.Infrastructure.Hosting;

namespace Toka.Ai.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddAiInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<AgentOptions>(configuration.GetSection(AgentOptions.SectionName));
        services.Configure<LlmOptions>(configuration.GetSection(LlmOptions.SectionName));
        services.Configure<QdrantOptions>(configuration.GetSection(QdrantOptions.SectionName));
        services.Configure<AiMongoOptions>(configuration.GetSection(AiMongoOptions.SectionName));
        services.Configure<KnowledgeOptions>(configuration.GetSection(KnowledgeOptions.SectionName));

        // LLM de chat (DeepSeek por defecto) y embeddings (Ollama local por defecto): endpoints independientes,
        // ambos con protocolo OpenAI. DeepSeek no ofrece API de embeddings.
        var llm = configuration.GetSection(LlmOptions.SectionName).Get<LlmOptions>() ?? new LlmOptions();
        var attempt = TimeSpan.FromSeconds(Math.Max(10, llm.AttemptTimeoutSeconds));
        services.AddHttpClient<ILlmClient, OpenAiChatClient>(client =>
            {
                client.BaseAddress = new Uri(llm.Chat.BaseUrl.TrimEnd('/') + "/");
                client.Timeout = Timeout.InfiniteTimeSpan; // los timeouts los controla el pipeline de resiliencia
            })
            .AddTokaResilience(attemptTimeout: attempt, totalTimeout: attempt * 2 + TimeSpan.FromSeconds(30), retries: 2);
        services.AddHttpClient<IEmbeddingClient, OpenAiEmbeddingClient>(client =>
            {
                client.BaseAddress = new Uri(llm.Embeddings.BaseUrl.TrimEnd('/') + "/");
                client.Timeout = Timeout.InfiniteTimeSpan;
            })
            .AddTokaResilience(attemptTimeout: attempt, totalTimeout: attempt * 2 + TimeSpan.FromSeconds(30), retries: 3);

        // Vector DB
        services.AddHttpClient<IVectorStore, QdrantVectorStore>((sp, client) =>
                client.BaseAddress = new Uri(sp.GetRequiredService<IOptions<QdrantOptions>>().Value.BaseUrl.TrimEnd('/') + "/"))
            .AddTokaResilience();

        // Métricas en MongoDB
        services.AddSingleton<IMongoClient>(sp => new MongoClient(sp.GetRequiredService<IOptions<AiMongoOptions>>().Value.ConnectionString));
        services.AddSingleton<MongoMetricsRepository>();
        services.AddSingleton<IMetricsRepository>(sp => sp.GetRequiredService<MongoMetricsRepository>());

        // Herramientas del agente → microservicios
        services.AddHttpClient(ServiceToolExecutor.UsersClient, c => c.BaseAddress = new Uri(configuration["Services:UserService"] ?? "http://user-service:8080")).AddTokaResilience();
        services.AddHttpClient(ServiceToolExecutor.RolesClient, c => c.BaseAddress = new Uri(configuration["Services:RoleService"] ?? "http://role-service:8080")).AddTokaResilience();
        services.AddHttpClient(ServiceToolExecutor.AuditClient, c => c.BaseAddress = new Uri(configuration["Services:AuditService"] ?? "http://audit-service:8080")).AddTokaResilience();
        services.AddScoped<IAgentToolExecutor, ServiceToolExecutor>();

        // Conocimiento
        services.AddSingleton<IKnowledgeSource, FileKnowledgeSource>();
        services.AddSingleton<IEvaluationDatasetSource, FileEvaluationDatasetSource>();
        services.AddHostedService<KnowledgeBootstrapper>();
        return services;
    }

    public static async Task InitializeAiStoreAsync(this IServiceProvider services)
    {
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("AiInit");
        var metrics = services.GetRequiredService<MongoMetricsRepository>();
        await StartupRetry.RunAsync(() => metrics.EnsureIndexesAsync(CancellationToken.None), logger, "Índices de métricas IA");
    }
}
