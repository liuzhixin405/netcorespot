using CryptoSpot.Domain.Entities;
using CryptoSpot.Application.Abstractions.Repositories;
using CryptoSpot.Application.Common.Interfaces;
using CryptoSpot.Infrastructure.MarketData;
using Microsoft.Extensions.Logging;

using Microsoft.Extensions.Options; // 读取 MarketMakerOptions

namespace CryptoSpot.Infrastructure.Services
{
    /// <summary>
    /// 数据初始化服务
    /// </summary>
    public class DataInitializationService
    {
        private readonly ITradingPairRepository _tradingPairRepository;
        private readonly IUserRepository _userRepository;
        private readonly IAssetRepository _assetRepository;
        private readonly IPasswordHasher _passwordHasher;
        private readonly ILogger<DataInitializationService> _logger;
        private readonly IOptions<MarketMakerOptions>? _mmOptions; // 可为空以兼容旧构造
        private const string TestUserPassword = "test123";

        private static readonly IReadOnlyDictionary<string, decimal> MarketMakerAssetAmounts =
            new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
            {
                ["USDT"] = 1_000_000m,
                ["BTC"] = 100m,
                ["ETH"] = 5_000m,
                ["BNB"] = 5_000m,
                ["XRP"] = 500_000m,
                ["ADA"] = 500_000m,
                ["DOGE"] = 10_000_000m,
                ["SOL"] = 50_000m,
                ["DOT"] = 100_000m,
                ["LINK"] = 50_000m,
                ["LTC"] = 5_000m
            };

        private static readonly IReadOnlyDictionary<string, decimal> TestUserAssetAmounts =
            new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
            {
                ["USDT"] = 10_000m,
                ["BTC"] = 1m,
                ["ETH"] = 10m,
                ["BNB"] = 50m,
                ["XRP"] = 5_000m,
                ["ADA"] = 5_000m,
                ["DOGE"] = 100_000m,
                ["SOL"] = 100m,
                ["DOT"] = 1_000m,
                ["LINK"] = 500m,
                ["LTC"] = 50m
            };

        public DataInitializationService(
           ITradingPairRepository tradingPairRepository,
           IUserRepository userRepository,
           IAssetRepository assetRepository,
            IPasswordHasher passwordHasher,
            ILogger<DataInitializationService> logger,
            IOptions<MarketMakerOptions>? mmOptions = null)
        {
            _tradingPairRepository = tradingPairRepository;
            _userRepository = userRepository;
            _assetRepository = assetRepository;
            _passwordHasher = passwordHasher;
            _logger = logger;
            _mmOptions = mmOptions;
        }

        /// <summary>
        /// 初始化基础数据
        /// </summary>
        public async Task InitializeDataAsync()
        {
            try
            {
                _logger.LogInformation("开始初始化数据...");

                await InitializeTradingPairsAsync();
                await InitializeSystemUsersAsync();
                await InitializeSystemAssetsAsync();
                await InitializeTestUsersAsync();
                await InitializeTestUserAssetsAsync();

                _logger.LogInformation("数据初始化完成");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "数据初始化失败");
                throw;
            }
        }

        /// <summary>
        /// 初始化交易对
        /// </summary>
        private async Task InitializeTradingPairsAsync()
        {
            var tradingPairs = LocalMarketCatalog.Pairs.Select(pair => new TradingPair
            {
                Symbol = pair.Symbol,
                BaseAsset = pair.BaseAsset,
                QuoteAsset = "USDT",
                MinQuantity = pair.MinQuantity,
                MaxQuantity = pair.MaxQuantity,
                PricePrecision = pair.PricePrecision,
                QuantityPrecision = pair.QuantityPrecision,
                IsActive = true,
                LastUpdated = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            }).ToArray();

            foreach (var tradingPair in tradingPairs)
            {
                var existing = (await _tradingPairRepository.FindAsync(tp => tp.Symbol == tradingPair.Symbol))
                    .FirstOrDefault();
                if (existing is null)
                {
                    await _tradingPairRepository.AddAsync(tradingPair);
                    _logger.LogInformation("创建交易对: {Symbol}", tradingPair.Symbol);
                    continue;
                }

                existing.BaseAsset = tradingPair.BaseAsset;
                existing.QuoteAsset = tradingPair.QuoteAsset;
                existing.MinQuantity = tradingPair.MinQuantity;
                existing.MaxQuantity = tradingPair.MaxQuantity;
                existing.PricePrecision = tradingPair.PricePrecision;
                existing.QuantityPrecision = tradingPair.QuantityPrecision;
                existing.IsActive = true;
                existing.LastUpdated = tradingPair.LastUpdated;
                existing.Touch();
                await _tradingPairRepository.UpdateAsync(existing);
            }

            foreach (var symbol in LocalMarketCatalog.ObsoleteSymbols)
            {
                var obsolete = await _tradingPairRepository.FindAsync(tp => tp.Symbol == symbol && tp.IsActive);
                foreach (var pair in obsolete)
                {
                    pair.IsActive = false;
                    pair.Touch();
                    await _tradingPairRepository.UpdateAsync(pair);
                    _logger.LogInformation("停用已下架交易对: {Symbol}", pair.Symbol);
                }
            }
        }

        /// <summary>
        /// 初始化系统用户
        /// </summary>
        private async Task InitializeSystemUsersAsync()
        {
            var systemUsers = new[]
            {
                new User
                {
                    Username = "SystemMarketMaker",
                    Type = UserType.MarketMaker,
                    Description = "系统做市商账号",
                    IsActive = true,
                    IsAutoTradingEnabled = true,
                    MaxRiskRatio = 0.1m,
                    DailyTradingLimit = 1000000m,
                    DailyTradedAmount = 0m
                },
                new User
                {
                    Username = "SystemAdmin",
                    Type = UserType.Admin,
                    Description = "系统管理员账号",
                    IsActive = true,
                    IsAutoTradingEnabled = false,
                    MaxRiskRatio = 0.05m,
                    DailyTradingLimit = 500000m,
                    DailyTradedAmount = 0m
                }
            };

            foreach (var user in systemUsers)
            {
                var existing = await _userRepository.FindAsync(u => u.Username == user.Username && u.Type != UserType.Regular);
                if (!existing.Any())
                {
                    await _userRepository.AddAsync(user);
                    _logger.LogInformation("创建系统用户: {Username}", user.Username);
                }
            }
        }

        /// <summary>
        /// 初始化系统资产
        /// </summary>
        private async Task InitializeSystemAssetsAsync()
        {
            // 支持多做市账号
            var makerIds = _mmOptions?.Value?.UserIds?.Distinct().ToList() ?? new List<long>();
            if (makerIds.Count == 0)
            {
                // 兼容旧逻辑：按用户名查找单一做市账号
                var single = (await _userRepository.FindAsync(u => u.Username == "SystemMarketMaker" && u.Type == UserType.MarketMaker)).FirstOrDefault();
                if (single != null) makerIds.Add(single.Id);
            }

            if (makerIds.Count == 0)
            {
                _logger.LogWarning("未找到任何做市商用户，跳过系统资产初始化");
                return;
            }

            foreach (var makerId in makerIds)
            {
                var user = await _userRepository.GetByIdAsync(makerId);
                if (user == null)
                {
                    _logger.LogWarning("配置的做市商用户 {MakerId} 不存在，跳过", makerId);
                    continue;
                }

                var systemAssets = BuildAssets(makerId, MarketMakerAssetAmounts, autoRefill: true).ToArray();

                foreach (var asset in systemAssets)
                {
                    var existing = await _assetRepository.FindAsync(a => a.UserId == asset.UserId && a.Symbol == asset.Symbol);
                    if (!existing.Any())
                    {
                        await _assetRepository.AddAsync(asset);
                        _logger.LogInformation("创建做市商 {MakerId} 系统资产: {Symbol} - {Amount}", makerId, asset.Symbol, asset.Available);
                    }
                }
            }
        }

        /// <summary>
        /// 初始化测试用户
        /// </summary>
        private async Task InitializeTestUsersAsync()
        {
            var testUsers = new[]
            {
                new User
                {
                    Username = "test_user_1",
                    Email = "test1@example.com",
                    PasswordHash = _passwordHasher.Hash(TestUserPassword),
                    Type = UserType.Regular,
                    Description = "测试用户1",
                    IsActive = true,
                    IsAutoTradingEnabled = false,
                    MaxRiskRatio = 0.3m,
                    DailyTradingLimit = 10000m,
                    DailyTradedAmount = 0m
                },
                new User
                {
                    Username = "test_user_2",
                    Email = "test2@example.com",
                    PasswordHash = _passwordHasher.Hash(TestUserPassword),
                    Type = UserType.Regular,
                    Description = "测试用户2",
                    IsActive = true,
                    IsAutoTradingEnabled = false,
                    MaxRiskRatio = 0.3m,
                    DailyTradingLimit = 10000m,
                    DailyTradedAmount = 0m
                },
                new User
                {
                    Username = "test_user_3",
                    Email = "test3@example.com",
                    PasswordHash = _passwordHasher.Hash(TestUserPassword),
                    Type = UserType.Regular,
                    Description = "测试用户3",
                    IsActive = true,
                    IsAutoTradingEnabled = false,
                    MaxRiskRatio = 0.3m,
                    DailyTradingLimit = 10000m,
                    DailyTradedAmount = 0m
                }
            };

            foreach (var user in testUsers)
            {
                var existing = (await _userRepository.FindAsync(u => u.Username == user.Username)).FirstOrDefault();
                if (existing == null)
                {
                    await _userRepository.AddAsync(user);
                    _logger.LogInformation("创建测试用户: {Username}", user.Username);
                    continue;
                }

                if (string.IsNullOrEmpty(existing.Email) || string.IsNullOrEmpty(existing.PasswordHash))
                {
                    existing.Email ??= user.Email;
                    existing.PasswordHash = user.PasswordHash;
                    existing.Touch();
                    await _userRepository.UpdateAsync(existing);
                    _logger.LogInformation("补全测试用户登录信息: {Username}", user.Username);
                }
            }
        }

        /// <summary>
        /// 初始化测试用户资产
        /// </summary>
        private async Task InitializeTestUserAssetsAsync()
        {
            var testUserNames = new[] { "test_user_1", "test_user_2", "test_user_3" };
            
            foreach (var username in testUserNames)
            {
                var user = (await _userRepository.FindAsync(u => u.Username == username)).FirstOrDefault();
                if (user == null)
                {
                    _logger.LogWarning("测试用户 {Username} 不存在，跳过资产初始化", username);
                    continue;
                }

                var testAssets = BuildAssets(user.Id, TestUserAssetAmounts, autoRefill: false).ToArray();

                foreach (var asset in testAssets)
                {
                    var existing = await _assetRepository.FindAsync(a => a.UserId == asset.UserId && a.Symbol == asset.Symbol);
                    if (!existing.Any())
                    {
                        await _assetRepository.AddAsync(asset);
                        _logger.LogInformation("创建测试用户 {Username} 资产: {Symbol} - {Amount}", username, asset.Symbol, asset.Available);
                    }
                }
            }
        }

        private static IEnumerable<Asset> BuildAssets(
            long userId,
            IReadOnlyDictionary<string, decimal> amounts,
            bool autoRefill)
        {
            foreach (var (symbol, amount) in amounts)
            {
                yield return new Asset
                {
                    UserId = userId,
                    Symbol = symbol,
                    Available = amount,
                    Frozen = 0m,
                    MinReserve = autoRefill ? amount * 0.1m : 0m,
                    TargetBalance = amount,
                    AutoRefillEnabled = autoRefill
                };
            }
        }

        /// <summary>
        /// 检查是否需要初始化数据
        /// </summary>
        public async Task<bool> NeedsInitializationAsync()
        {
            var activePairs = (await _tradingPairRepository.FindAsync(tp => tp.IsActive)).ToList();
            var activeSymbols = activePairs
                .Select(tp => tp.Symbol)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var allPairsConfigured = LocalMarketCatalog.Pairs.All(definition =>
            {
                var pair = activePairs.FirstOrDefault(value =>
                    string.Equals(value.Symbol, definition.Symbol, StringComparison.OrdinalIgnoreCase));
                return pair is not null
                    && pair.MinQuantity == definition.MinQuantity
                    && pair.MaxQuantity == definition.MaxQuantity
                    && pair.PricePrecision == definition.PricePrecision
                    && pair.QuantityPrecision == definition.QuantityPrecision;
            });
            var hasObsoletePairs = LocalMarketCatalog.ObsoleteSymbols.Any(activeSymbols.Contains);

            var systemUsers = (await _userRepository.FindAsync(
                u => u.Username == "SystemMarketMaker" || u.Username == "SystemAdmin")).ToList();
            var systemUsersExist = systemUsers.Any(u => u.Username == "SystemMarketMaker")
                && systemUsers.Any(u => u.Username == "SystemAdmin");
            var testUserNames = new List<string> { "test_user_1", "test_user_2", "test_user_3" };
            var testUsers = (await _userRepository.FindAsync(u => testUserNames.Contains(u.Username))).ToList();
            var testUsersNeedLoginInfo = testUsers.Count < testUserNames.Count
                || testUsers.Any(u => string.IsNullOrEmpty(u.Email) || string.IsNullOrEmpty(u.PasswordHash));

            var maker = (await _userRepository.FindAsync(u => u.Type == UserType.MarketMaker)).FirstOrDefault();
            var usersToCheck = new List<long>();
            if (maker is not null)
            {
                usersToCheck.Add(maker.Id);
            }
            usersToCheck.AddRange(testUsers.Select(u => u.Id));

            var assetsIncomplete = false;
            foreach (var userId in usersToCheck)
            {
                var existingAssets = (await _assetRepository.FindAsync(a => a.UserId == userId))
                    .Select(a => a.Symbol)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                var expected = userId == maker?.Id
                    ? MarketMakerAssetAmounts.Keys
                    : TestUserAssetAmounts.Keys;

                if (expected.Any(symbol => !existingAssets.Contains(symbol)))
                {
                    assetsIncomplete = true;
                    break;
                }
            }

            return !allPairsConfigured || hasObsoletePairs || !systemUsersExist || testUsersNeedLoginInfo || assetsIncomplete;
        }
    }
}
