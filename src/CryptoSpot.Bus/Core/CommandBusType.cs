namespace CryptoSpot.Bus.Core
{
    /// <summary>
    /// CommandBus实现类型枚举
    /// </summary>
    public enum CommandBusType
    {
        /// <summary>
        /// 标准命令总线
        /// </summary>
        Standard,
        
        /// <summary>
        /// TPL Dataflow命令总线
        /// </summary>
        Dataflow,
        
        /// <summary>
        /// 批处理Dataflow命令总线
        /// </summary>
        BatchDataflow,
        
        /// <summary>
        /// 类型安全Dataflow命令总线
        /// </summary>
        TypedDataflow,
        
        /// <summary>
        /// 带监控的命令总线
        /// </summary>
        Monitored
    }
}
