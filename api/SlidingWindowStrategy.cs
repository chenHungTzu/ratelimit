using AspNetCoreRateLimit;
using StackExchange.Redis;

public class SlidingWindowStrategy 
{

    private readonly IConnectionMultiplexer _connectionMultiplexer;

    private readonly IDatabase _db;

    private const string _slidingRateLimiter = @"
    -- 取得時間
    local now = redis.call('TIME') 
    local num_windows = ARGV[1]
    for i=2, num_windows*2, 2 do
       
        -- 取得窗口
        local window = ARGV[i]

        -- 取得最大請求數
        local max_requests = ARGV[i+1]
        local key = KEYS[i/2]
        local trim_time = tonumber(now[1]) - window

        -- 移除過期的請求
        redis.call('ZREMRANGEBYSCORE', key, 0, trim_time)

        -- 截至目前有效窗口中的請求總數，並比對是否超出最大請求數
        local request_count = redis.call('ZCARD',key)
        if request_count >= tonumber(max_requests) then
            return 1
        end
    end
    for i=2, num_windows*2, 2 do
        local key = KEYS[i/2]
        local window = ARGV[i]

        -- 寫入請求紀錄
        redis.call('ZADD', key, now[1], now[1] .. now[2])

        -- 寫入TTL
        redis.call('EXPIRE', key, window)
    end
    return 0
    ";

    public SlidingWindowStrategy(IConnectionMultiplexer connectionMultiplexer,ILogger<SlidingWindowStrategy> logger)
    {
        _connectionMultiplexer = connectionMultiplexer ?? throw new ArgumentException("IConnectionMultiplexer was null. Ensure StackExchange.Redis was successfully registered");
        _db = _connectionMultiplexer.GetDatabase();
    }

    public async Task<bool> ProcessRequestAsync(ClientRequestIdentity requestIdentity,IEnumerable<RateLimitRule> ruleset)
    {
       
        var keys  = new List<RedisKey>();
        var args = new List<RedisValue> { ruleset.Count() };
        foreach (var rule in ruleset)
        {
            keys.Add($"{requestIdentity.ClientIp}_{rule.Endpoint}_{rule.Period}_{rule.Limit}");
            args.Add(rule.PeriodTimespan.Value.TotalSeconds);
            args.Add(rule.Limit);
        }
        return (int)await _db.ScriptEvaluateAsync(_slidingRateLimiter, keys.ToArray(), args.ToArray()) == 1;
    }

}