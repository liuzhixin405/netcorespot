using CryptoSpot.Domain.Entities;
using CryptoSpot.Application.Abstractions.Repositories;
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
        private readonly ILogger<DataInitializationService> _logger;
        private readonly IOptions<MarketMakerOptions>? _mmOptions; // 可为空以兼容旧构造

        public DataInitializationService(
           ITradingPairRepository tradingPairRepository,
           IUserRepository userRepository,
           IAssetRepository assetRepository,
            ILogger<DataInitializationService> logger,
            IOptions<MarketMakerOptions>? mmOptions = null)
        {
            _tradingPairRepository = tradingPairRepository;
            _userRepository = userRepository;
            _assetRepository = assetRepository;
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
            var tradingPairs = new[]
            {
                new TradingPair
                {
                    Symbol = "BTCUSDT",
                    BaseAsset = "BTC",
                    QuoteAsset = "USDT",
                    MinQuantity = 0.00001m,
                    MaxQuantity = 1000m,
                    PricePrecision = 2,
                    QuantityPrecision = 5,
                    IsActive = true,
                    LastUpdated = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                },
                new TradingPair
                {
                    Symbol = "ETHUSDT",
                    BaseAsset = "ETH",
                    QuoteAsset = "USDT",
                    MinQuantity = 0.001m,
                    MaxQuantity = 10000m,
                    PricePrecision = 2,
                    QuantityPrecision = 3,
                    IsActive = true,
                    LastUpdated = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                },
                new TradingPair
                {
                    Symbol = "SOLUSDT",
                    BaseAsset = "SOL",
                    QuoteAsset = "USDT",
                    MinQuantity = 0.01m,
                    MaxQuantity = 100000m,
                    PricePrecision = 3,
                    QuantityPrecision = 2,
                    IsActive = true,
                    LastUpdated = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                }
            };

            foreach (var tradingPair in tradingPairs)
            {
                var existing = await _tradingPairRepository.FindAsync(tp => tp.Symbol == tradingPair.Symbol);
                if (!existing.Any())
                {
                    await _tradingPairRepository.AddAsync(tradingPair);
                    _logger.LogInformation("创建交易对: {Symbol}", tradingPair.Symbol);
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
            var makerIds = _mmOptions?.Value?.UserIds?.Distinct().ToList() ?? new List<int>();
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

                var systemAssets = new[]
                {
                    new Asset { UserId = makerId, Symbol = "USDT", Available = 1000000m, Frozen = 0m, MinReserve = 100000m, TargetBalance = 1000000m, AutoRefillEnabled = true },
                    new Asset { UserId = makerId, Symbol = "BTC",  Available = 100m,     Frozen = 0m, MinReserve = 10m,     TargetBalance = 100m,     AutoRefillEnabled = true },
                    new Asset { UserId = makerId, Symbol = "ETH",  Available = 5000m,    Frozen = 0m, MinReserve = 500m,    TargetBalance = 5000m,    AutoRefillEnabled = true },
                    new Asset { UserId = makerId, Symbol = "SOL",  Available = 50000m,   Frozen = 0m, MinReserve = 5000m,   TargetBalance = 50000m,   AutoRefillEnabled = true }
                };

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
                var existing = await _userRepository.FindAsync(u => u.Username == user.Username);
                if (!existing.Any())
                {
                    await _userRepository.AddAsync(user);
                    _logger.LogInformation("创建测试用户: {Username}", user.Username);
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

                var testAssets = new[]
                {
                    new Asset 
                    { 
                        UserId = user.Id, 
                        Symbol = "USDT", 
                        Available = 10000m, 
                        Frozen = 0m, 
                        MinReserve = 0m, 
                        TargetBalance = 10000m, 
                        AutoRefillEnabled = false 
                    },
                    new Asset 
                    { 
                        UserId = user.Id, 
                        Symbol = "BTC", 
                        Available = 1m, 
                        Frozen = 0m, 
                        MinReserve = 0m, 
                        TargetBalance = 1m, 
                        AutoRefillEnabled = false 
                    },
                    new Asset 
                    { 
                        UserId = user.Id, 
                        Symbol = "ETH", 
                        Available = 10m, 
                        Frozen = 0m, 
                        MinReserve = 0m, 
                        TargetBalance = 10m, 
                        AutoRefillEnabled = false 
                    },
                    new Asset 
                    { 
                        UserId = user.Id, 
                        Symbol = "SOL", 
                        Available = 100m, 
                        Frozen = 0m, 
                        MinReserve = 0m, 
                        TargetBalance = 100m, 
                        AutoRefillEnabled = false 
                    }
                };

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

        /// <summary>
        /// 检查是否需要初始化数据
        /// </summary>
        public async Task<bool> NeedsInitializationAsync()
        {
            var tradingPairsExist = (await _tradingPairRepository.FindAsync(tp => tp.IsActive)).Any();
            var systemUsersExist = (await _userRepository.FindAsync(u => u.Type != UserType.Regular)).Any();
            
            return !tradingPairsExist || !systemUsersExist;
        }
    }
}
