using AspNetCoreRateLimit;
using Microsoft.Extensions.Options;

public class TokenBucketMiddleware 
{

    private readonly RequestDelegate _next;
    private readonly IpRateLimitProcessor _processor;
    private readonly TokenBucketStraregy _processingStrategy;
    private readonly RateLimitOptions _options;
    private readonly IRateLimitConfiguration _config;

    public TokenBucketMiddleware(
         RequestDelegate next,
         TokenBucketStraregy processingStrategy,
         IOptions<IpRateLimitOptions> options,
         IIpPolicyStore policyStore,
         IRateLimitConfiguration config)
    {
        _next = next;
        _options = options?.Value;
        _processor = new IpRateLimitProcessor(options?.Value, policyStore, null);
        _processingStrategy = processingStrategy;
        _config = config;
        _config.RegisterResolvers();
    }

    public async Task Invoke(HttpContext context)
    {
        if (_options == null)
        {
            await _next(context);
            return;
        }

        // 取得 HttpRequest 詳細資訊
        ClientRequestIdentity identity = await ResolveIdentityAsync(context);

        // 如果是白名單，直接放行
        if (_processor.IsWhitelisted(identity))
        {
            await _next(context);
            return;
        }

        // 取得符合的限流規則
        IEnumerable<RateLimitRule> ruleset = await _processor.GetMatchingRulesAsync(identity, context.RequestAborted);

        if (ruleset.Any() == false)
        {
            await _next(context);
            return;
        }

        // 呼叫客制化的處理策略 , Sliding Window strategy
        var isLimit = await _processingStrategy.ProcessRequestAsync(identity, ruleset);

        // 是否限流
        if (isLimit)
        {
            await ReturnQuotaExceededResponse(context);
            return;
        }
        await _next(context);
    }

    /// <summary>
    /// 限流時回應 
    /// </summary>
    /// <param name="httpContext"></param>
    /// <returns></returns>
    public virtual Task ReturnQuotaExceededResponse(HttpContext httpContext)
    {

        string text = string.Format(_options.QuotaExceededResponse?.Content ?? _options.QuotaExceededMessage ?? "API calls quota exceeded! maximum admitted");

        httpContext.Response.StatusCode = _options.QuotaExceededResponse?.StatusCode ?? _options.HttpStatusCode;
        httpContext.Response.ContentType = _options.QuotaExceededResponse?.ContentType ?? "text/plain";
        return httpContext.Response.WriteAsync(text);

    }

    public virtual async Task<ClientRequestIdentity> ResolveIdentityAsync(HttpContext httpContext)
    {
        string clientIp = null;
        string text = null;
        if (_config.ClientResolvers?.Any() ?? false)
        {
            foreach (IClientResolveContributor clientResolver in _config.ClientResolvers)
            {
                text = await clientResolver.ResolveClientAsync(httpContext);
                if (!string.IsNullOrEmpty(text))
                {
                    break;
                }
            }
        }

        if (_config.IpResolvers?.Any() ?? false)
        {
            foreach (IIpResolveContributor ipResolver in _config.IpResolvers)
            {
                clientIp = ipResolver.ResolveIp(httpContext);
                if (!string.IsNullOrEmpty(clientIp))
                {
                    break;
                }
            }
        }

        string text2 = httpContext.Request.Path.ToString().ToLowerInvariant();
        return new ClientRequestIdentity
        {
            ClientIp = clientIp,
            Path = ((text2 == "/") ? text2 : text2.TrimEnd('/')),
            HttpVerb = httpContext.Request.Method.ToLowerInvariant(),
            ClientId = (text ?? "anon")
        };
    }



}