using CryptoSpot.Domain.Entities;
using CryptoSpot.Core.Interfaces.Repositories;
using CryptoSpot.Core.Extensions;
using Microsoft.Extensions.Logging;

namespace CryptoSpot.Infrastructure.Services
{
    /// <summary>
    /// 数据初始化服务
    /// </summary>
    public class DataInitializationService
    {
        private readonly IRepository<TradingPair> _tradingPairRepository;
        private readonly IRepository<User> _userRepository;
        private readonly IRepository<Asset> _assetRepository;
        private readonly ILogger<DataInitializationService> _logger;

        public DataInitializationService(
            IRepository<TradingPair> tradingPairRepository,
            IRepository<User> userRepository,
            IRepository<Asset> assetRepository,
            ILogger<DataInitializationService> logger)
        {
            _tradingPairRepository = tradingPairRepository;
            _userRepository = userRepository;
            _assetRepository = assetRepository;
            _logger = logger;
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
                    LastUpdated = DateTimeExtensions.GetCurrentUnixTimeMilliseconds()
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
                    LastUpdated = DateTimeExtensions.GetCurrentUnixTimeMilliseconds()
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
                    LastUpdated = DateTimeExtensions.GetCurrentUnixTimeMilliseconds()
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
            // 获取系统做市商用户
            var marketMaker = (await _userRepository.FindAsync(u => u.Username == "SystemMarketMaker" && u.Type == UserType.MarketMaker)).FirstOrDefault();
            if (marketMaker == null)
            {
                _logger.LogWarning("未找到系统做市商用户，跳过资产初始化");
                return;
            }

            var systemAssets = new[]
            {
                new Asset
                {
                    UserId = marketMaker.Id,
                    Symbol = "USDT",
                    Available = 1000000m, // 100万USDT
                    Frozen = 0m,
                    MinReserve = 100000m, // 保留10万USDT
                    TargetBalance = 1000000m,
                    AutoRefillEnabled = true
                },
                new Asset
                {
                    UserId = marketMaker.Id,
                    Symbol = "BTC",
                    Available = 100m, // 100 BTC
                    Frozen = 0m,
                    MinReserve = 10m, // 保留10 BTC
                    TargetBalance = 100m,
                    AutoRefillEnabled = true
                },
                new Asset
                {
                    UserId = marketMaker.Id,
                    Symbol = "ETH",
                    Available = 5000m, // 5000 ETH
                    Frozen = 0m,
                    MinReserve = 500m, // 保留500 ETH
                    TargetBalance = 5000m,
                    AutoRefillEnabled = true
                },
                new Asset
                {
                    UserId = marketMaker.Id,
                    Symbol = "SOL",
                    Available = 50000m, // 50000 SOL
                    Frozen = 0m,
                    MinReserve = 5000m, // 保留5000 SOL
                    TargetBalance = 50000m,
                    AutoRefillEnabled = true
                }
            };

            foreach (var asset in systemAssets)
            {
                var existing = await _assetRepository.FindAsync(a => a.UserId == asset.UserId && a.Symbol == asset.Symbol);
                if (!existing.Any())
                {
                    await _assetRepository.AddAsync(asset);
                    _logger.LogInformation("创建系统资产: {Symbol} - {Amount}", asset.Symbol, asset.Available);
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
