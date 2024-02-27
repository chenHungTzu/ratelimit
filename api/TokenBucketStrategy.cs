using AspNetCoreRateLimit;
using StackExchange.Redis;

public class TokenBucketStraregy
{

    private readonly IConnectionMultiplexer _connectionMultiplexer;

    private readonly IDatabase _db;

     private const string _tokenBucketRateLimiter = @"
    local num_windows = ARGV[1]
    local time = redis.call('TIME')
    local now = math.floor((time[1] * 1000) + (time[2] / 1000))
    local dict = {}
    for i=2, num_windows*2, 2 do
        local key = KEYS[i/2]
        local window = ARGV[i]
        local max_requests = ARGV[i+1]
      
        -- 根據上個時間戳（lastTokenAddedTime），取得剩餘容量（tokens）
        local bucket = redis.call('HMGET', key, 'lastTokenAddedTime', 'tokens')
        local lastTokenAddedTime = tonumber(bucket[1])
        local tokens = tonumber(bucket[2])

        -- 如果不存在，就初始化，並壓上當前時間戳。
        if lastTokenAddedTime == nil or tokens == nil then
            lastTokenAddedTime = now
            tokens = 0

            redis.call('HMSET', key, 'lastTokenAddedTime', now, 'tokens', tokens)
           
        end

        -- 從上次添加Token到現在的時間間隔
        local elapsedTime = now - lastTokenAddedTime

        -- 算出這次應該添加多少Token
        local addTokens = math.max(((elapsedTime) / (window*1000)) * max_requests, 0)

        -- 上次執行剩餘的Token ，加上這次應該添加的Token作為基礎
        local newTokens = math.min(tokens + addTokens, max_requests)

        -- 比對token是否超出請求數
        if newTokens < 1 then
           return 1
        end
        
        -- 準備更新至Redis
        dict[key] = tostring(now) .. '@' ..  tostring(newTokens - 1) .. '@' .. tostring(window)
    end

    for k, v in pairs(dict) do
    
        local parts = {}
        for part in string.gmatch(v, ""[^{'@'}]+"") do
            table.insert(parts, part)
        end
        redis.call('HMSET', k, 'lastTokenAddedTime', tonumber(parts[1]), 'tokens', tonumber(parts[2]))
    end

    return 0
    ";

    public TokenBucketStraregy(IConnectionMultiplexer connectionMultiplexer,ILogger<TokenBucketStraregy> logger)
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
        return (int)await _db.ScriptEvaluateAsync(_tokenBucketRateLimiter, keys.ToArray(), args.ToArray()) == 1;
    }


}