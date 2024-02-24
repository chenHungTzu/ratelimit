

using AspNetCoreRateLimit;
using AspNetCoreRateLimit.Redis;
using StackExchange.Redis;

internal class Program
{
    private static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        builder.Services.AddLogging(config =>
           {
               config.AddConsole();
               config.AddDebug();
           });

        // Add services to the container.
        builder.Services.AddControllers();

        // Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen();

        // 從appsettings.json取得設定
        builder.Services.AddOptions();

        // 限流全域設定
        builder.Services.Configure<IpRateLimitOptions>(builder.Configuration.GetSection("IpRateLimiting"));

        // IP限制策略
        builder.Services.Configure<IpRateLimitPolicies>(builder.Configuration.GetSection("IpRateLimitPolicies"));

        // 共通設定，如IP解析器、ClientID解析器，以及計數器鍵生成器..等
        // 裡面的邏輯會依賴上面的設定
        builder.Services.AddSingleton<IRateLimitConfiguration, RateLimitConfiguration>();

        // REDIS計數器的資料儲存方式，需要依賴IConnectionMultiplexer
        builder.Services.AddSingleton<IConnectionMultiplexer>(
            ConnectionMultiplexer.Connect(builder.Configuration["Redis:ConnectionString"]));

        // 設定給 IP 限制策略 & ClientID 限制策略 的資料儲存方式，這邊儲存於Redis
        builder.Services.AddStackExchangeRedisCache(option =>
        {
            option.Configuration = builder.Configuration["Redis:ConnectionString"];
            option.InstanceName = builder.Configuration["Redis:InstanceName"];
        });

        // 註冊 REDIS計數器處理策略(fixed window) 
        // 註冊 IP 限制策略 & ClientID 限制策略 的資料來源 
        builder.Services.AddRedisRateLimiting();

        var app = builder.Build();

        // 設定中繼管道，在流量進入時進行限流判斷
        app.UseIpRateLimiting();

        // 這個方法會同步到Redis，將IP限制策略資料寫入Redis如
        // 果是分散式部署，這個方法可能會覆蓋掉已修改的資料，建議集中式管理
        using (var scope = app.Services.CreateScope())
        {
            var clientPolicyStore = scope.ServiceProvider.GetRequiredService<IIpPolicyStore>();

            await clientPolicyStore.SeedAsync();
        }

        // Configure the HTTP request pipeline.
        if (app.Environment.IsDevelopment())
        {
            app.UseSwagger();
            app.UseSwaggerUI(c =>
            {
                c.RoutePrefix = string.Empty;
                c.SwaggerEndpoint("/swagger/v1/swagger.json", "api v1");
            });
        }

        app.UseHttpsRedirection();

        app.UseAuthorization();

        app.MapControllers();

        app.UseHttpLogging();

        await app.RunAsync();
    }
}