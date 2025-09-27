using CryptoSpot.Domain.Entities;
using CryptoSpot.Application.Abstractions.Repositories; // replaced Core.Interfaces.Repositories
using Microsoft.Extensions.Logging;
using CryptoSpot.Redis;
using StackExchange.Redis;
using System.Globalization;
using CryptoSpot.Application.Abstractions.Services.Users;

namespace CryptoSpot.Infrastructure.Services
{
    public class AssetService : IAssetService
    {
        private readonly IAssetRepository _assetRepository;
        private readonly ILogger<AssetService> _logger;
        private readonly IUnitOfWork _unitOfWork; // 仅用于后台flush
        private readonly IRedisCache _redis;

        private static readonly TimeSpan AssetKeyTtl = TimeSpan.FromDays(30); // 可选：保持活跃
        private const string DirtyListKey = "assets:dirty"; // Redis 列表，写后写入此列表

        // 预编译 Lua 脚本 (使用占位符而不是在脚本中直接访问 KEYS/ARGV，避免参数映射错误)
        private static readonly LuaScript FreezeScript = LuaScript.Prepare(@"
local amount = tonumber(@amount)
local now = @now
local avail = redis.call('HGET', @key, 'available')
if not avail then return 0 end
avail = tonumber(avail)
if avail < amount then return 0 end
redis.call('HINCRBYFLOAT', @key, 'available', -amount)
redis.call('HINCRBYFLOAT', @key, 'frozen', amount)
redis.call('HSET', @key, 'updatedAt', now)
return 1");

        private static readonly LuaScript UnfreezeScript = LuaScript.Prepare(@"
local amount = tonumber(@amount)
local now = @now
local frozen = redis.call('HGET', @key, 'frozen')
if not frozen then return 0 end
frozen = tonumber(frozen)
if frozen < amount then return 0 end
redis.call('HINCRBYFLOAT', @key, 'frozen', -amount)
redis.call('HINCRBYFLOAT', @key, 'available', amount)
redis.call('HSET', @key, 'updatedAt', now)
return 1");

        private static readonly LuaScript DeductAvailableScript = LuaScript.Prepare(@"
local amount = tonumber(@amount)
local now = @now
local avail = redis.call('HGET', @key, 'available')
if not avail then return 0 end
avail = tonumber(avail)
if avail < amount then return 0 end
redis.call('HINCRBYFLOAT', @key, 'available', -amount)
redis.call('HSET', @key, 'updatedAt', now)
return 1");

        private static readonly LuaScript DeductFrozenScript = LuaScript.Prepare(@"
local amount = tonumber(@amount)
local now = @now
local frozen = redis.call('HGET', @key, 'frozen')
if not frozen then return 0 end
frozen = tonumber(frozen)
if frozen < amount then return 0 end
redis.call('HINCRBYFLOAT', @key, 'frozen', -amount)
redis.call('HSET', @key, 'updatedAt', now)
return 1");

        private static readonly LuaScript AddAvailableScript = LuaScript.Prepare(@"
local amount = tonumber(@amount)
local now = @now
if redis.call('EXISTS', @key) == 0 then
  redis.call('HSET', @key, 'available', 0)
  redis.call('HSET', @key, 'frozen', 0)
  redis.call('HSET', @key, 'createdAt', now)
  redis.call('HSET', @key, 'updatedAt', now)
end
redis.call('HINCRBYFLOAT', @key, 'available', amount)
redis.call('HSET', @key, 'updatedAt', now)
return 1");

        public AssetService(
            IAssetRepository assetRepository,
            ILogger<AssetService> logger,
            IUnitOfWork unitOfWork,
            IRedisCache redis)
        {
            _assetRepository = assetRepository;
            _logger = logger;
            _unitOfWork = unitOfWork;
            _redis = redis;
        }

        private static string GetAssetKey(int userId, string symbol) => $"asset:{userId}:{symbol}";

        private async Task<Asset?> GetFromRedisAsync(int userId, string symbol, bool allowDbFallback = true)
        {
            var key = GetAssetKey(userId, symbol);
            var map = await _redis.HGetAllAsync(key);
            if (map != null && map.Count > 0)
            {
                return MapToAsset(userId, symbol, map);
            }

            if (!allowDbFallback) return null;

            // 懒加载 DB -> Redis
            var dbAsset = await _assetRepository.GetUserAssetAsync(userId, symbol);
            if (dbAsset != null)
            {
                await WriteRedisAsync(dbAsset);
                return dbAsset;
            }
            return null;
        }

        private Asset MapToAsset(int userId, string symbol, Dictionary<string, string> map)
        {
            var asset = new Asset
            {
                UserId = userId,
                Symbol = symbol,
                Available = map.TryGetValue("available", out var a) && decimal.TryParse(a, NumberStyles.Any, CultureInfo.InvariantCulture, out var av) ? av : 0m,
                Frozen = map.TryGetValue("frozen", out var f) && decimal.TryParse(f, NumberStyles.Any, CultureInfo.InvariantCulture, out var fr) ? fr : 0m,
            };
            if (map.TryGetValue("updatedAt", out var u) && long.TryParse(u, out var up)) asset.UpdatedAt = up;
            if (map.TryGetValue("createdAt", out var c) && long.TryParse(c, out var cp)) asset.CreatedAt = cp;
            return asset;
        }

        private async Task WriteRedisAsync(Asset asset)
        {
            var key = GetAssetKey(asset.UserId!.Value, asset.Symbol);
            var now = asset.UpdatedAt == 0 ? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() : asset.UpdatedAt;
            await _redis.HMSetAsync(key, new HashEntry[]
            {
                new HashEntry("available", asset.Available.ToString(CultureInfo.InvariantCulture)),
                new HashEntry("frozen", asset.Frozen.ToString(CultureInfo.InvariantCulture)),
                new HashEntry("createdAt", (asset.CreatedAt==0?now:asset.CreatedAt).ToString()),
                new HashEntry("updatedAt", now.ToString())
            });
            // 可选设置TTL (如果实现需要，可忽略失败)
            try { await _redis.KeyExpireAsync(key, AssetKeyTtl); } catch {}
        }

        private async Task EnqueueDirtyAsync(string key)
        {
            try { await _redis.ListLeftPushAsync(DirtyListKey, key); } catch (Exception ex) { _logger.LogWarning(ex, "Enqueue dirty key failed {Key}", key); }
        }

        public async Task<IEnumerable<Asset>> GetUserAssetsAsync(int userId)
        {
            // 从数据库加载该用户资产并写入Redis（首次/批量）
            var dbAssets = await _assetRepository.GetAssetsByUserIdAsync(userId);
            var list = dbAssets.ToList();
            foreach (var a in list)
            {
                await WriteRedisAsync(a);
            }
            return list;
        }

        public async Task<Asset?> GetUserAssetAsync(int userId, string symbol)
            => await GetFromRedisAsync(userId, symbol);

        public async Task<Asset> CreateUserAssetAsync(int userId, string symbol, decimal available = 0, decimal frozen = 0)
        {
            var existing = await GetFromRedisAsync(userId, symbol, allowDbFallback: true);
            if (existing != null)
            {
                existing.Available = available;
                existing.Frozen = frozen;
                existing.UpdatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                await WriteRedisAsync(existing);
                await EnqueueDirtyAsync(GetAssetKey(userId, symbol));
                return existing;
            }
            var asset = new Asset
            {
                UserId = userId,
                Symbol = symbol,
                Available = available,
                Frozen = frozen,
                CreatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                UpdatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };
            await WriteRedisAsync(asset);
            await EnqueueDirtyAsync(GetAssetKey(userId, symbol));
            return asset;
        }

        public async Task<Asset> UpdateAssetBalanceAsync(int userId, string symbol, decimal available, decimal frozen)
        {
            var asset = await CreateUserAssetAsync(userId, symbol, available, frozen);
            return asset;
        }

        public async Task<bool> HasSufficientBalanceAsync(int userId, string symbol, decimal amount, bool includeFrozen = false)
        {
            var asset = await GetFromRedisAsync(userId, symbol);
            if (asset == null) return false;
            var total = includeFrozen ? asset.Available + asset.Frozen : asset.Available;
            return total >= amount;
        }

        public async Task<bool> FreezeAssetAsync(int userId, string symbol, decimal amount)
        {
            var key = GetAssetKey(userId, symbol);
            await EnsureExistsAsync(userId, symbol);
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var result = await _redis.ScriptEvaluateAsync(FreezeScript, new { key = (RedisKey)key, amount = amount.ToString(CultureInfo.InvariantCulture), now });
            var success = (long)result == 1;
            if (success) await EnqueueDirtyAsync(key);
            if (!success) _logger.LogWarning("Freeze failed insufficient balance user {UserId} {Symbol} {Amount}", userId, symbol, amount);
            return success;
        }

        public async Task<bool> UnfreezeAssetAsync(int userId, string symbol, decimal amount)
        {
            var key = GetAssetKey(userId, symbol);
            await EnsureExistsAsync(userId, symbol);
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var result = await _redis.ScriptEvaluateAsync(UnfreezeScript, new { key = (RedisKey)key, amount = amount.ToString(CultureInfo.InvariantCulture), now });
            var success = (long)result == 1;
            if (success) await EnqueueDirtyAsync(key);
            return success;
        }

        public async Task<bool> DeductAssetAsync(int userId, string symbol, decimal amount, bool fromFrozen = false)
        {
            var key = GetAssetKey(userId, symbol);
            await EnsureExistsAsync(userId, symbol);
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var script = fromFrozen ? DeductFrozenScript : DeductAvailableScript;
            var result = await _redis.ScriptEvaluateAsync(script, new { key = (RedisKey)key, amount = amount.ToString(CultureInfo.InvariantCulture), now });
            var success = (long)result == 1;
            if (success) await EnqueueDirtyAsync(key);
            if (!success) _logger.LogWarning("Deduct failed user {UserId} {Symbol} {Amount} fromFrozen={FromFrozen}", userId, symbol, amount, fromFrozen);
            return success;
        }

        public async Task<bool> AddAssetAsync(int userId, string symbol, decimal amount)
        {
            var key = GetAssetKey(userId, symbol);
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var result = await _redis.ScriptEvaluateAsync(AddAvailableScript, new { key = (RedisKey)key, amount = amount.ToString(CultureInfo.InvariantCulture), now });
            var success = (long)result == 1;
            if (success) await EnqueueDirtyAsync(key);
            return success;
        }

        public async Task InitializeUserAssetsAsync(int userId, Dictionary<string, decimal> initialBalances)
        {
            foreach (var kv in initialBalances)
            {
                await CreateUserAssetAsync(userId, kv.Key, kv.Value, 0);
            }
        }

        private async Task EnsureExistsAsync(int userId, string symbol)
        {
            var key = GetAssetKey(userId, symbol);
            var exists = await _redis.ExistsAsync(key);
            if (!exists)
            {
                var db = await _assetRepository.GetUserAssetAsync(userId, symbol);
                if (db != null)
                {
                    await WriteRedisAsync(db);
                }
                else
                {
                    await CreateUserAssetAsync(userId, symbol, 0, 0);
                }
            }
        }

        // 后台flush调用（不在 IAssetService 接口中）
        public async Task FlushDirtyAssetsAsync(CancellationToken ct = default)
        {
            try
            {
                var uniqueKeys = new HashSet<string>();
                // 逐条弹出（当前 IRedisCache 没有泛型批量获取简单字符串列表的方法，只能循环）
                while (!ct.IsCancellationRequested)
                {
                    var popped = await _redis.ListRightPopAsync(DirtyListKey);
                    if (string.IsNullOrEmpty(popped)) break;
                    uniqueKeys.Add(popped);
                    if (uniqueKeys.Count >= 1000) break; // 批次上限
                }

                if (uniqueKeys.Count == 0) return;

                _logger.LogInformation("Flushing {Count} dirty asset keys to DB", uniqueKeys.Count);
                foreach (var key in uniqueKeys)
                {
                    var parts = key.Split(':'); // asset:{userId}:{symbol}
                    if (parts.Length != 3) continue;
                    if (!int.TryParse(parts[1], out var userId)) continue;
                    var symbol = parts[2];
                    var map = await _redis.HGetAllAsync(key);
                    if (map == null || map.Count == 0) continue;
                    var asset = await _assetRepository.GetUserAssetAsync(userId, symbol);
                    var available = map.TryGetValue("available", out var avs) && decimal.TryParse(avs, NumberStyles.Any, CultureInfo.InvariantCulture, out var av) ? av : 0m;
                    var frozen = map.TryGetValue("frozen", out var frs) && decimal.TryParse(frs, NumberStyles.Any, CultureInfo.InvariantCulture, out var fr) ? fr : 0m;
                    var updatedAt = map.TryGetValue("updatedAt", out var ups) && long.TryParse(ups, out var up) ? up : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                    if (asset == null)
                    {
                        asset = new Asset
                        {
                            UserId = userId,
                            Symbol = symbol,
                            Available = available,
                            Frozen = frozen,
                            CreatedAt = updatedAt,
                            UpdatedAt = updatedAt
                        };
                        await _assetRepository.AddAsync(asset);
                    }
                    else
                    {
                        asset.Available = available;
                        asset.Frozen = frozen;
                        asset.UpdatedAt = updatedAt;
                        await _assetRepository.UpdateAsync(asset);
                    }
                }
                await _unitOfWork.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error flushing dirty assets to database");
            }
        }
    }
}
