using System;
using System.Threading.Tasks;

namespace CryptoSpot.MatchEngine.Events
{
    public delegate Task MatchEngineEventHandler<in TEvent>(TEvent evt) where TEvent : IMatchEngineEvent;

    public interface IMatchEngineEventBus
    {
        Task PublishAsync<TEvent>(TEvent evt) where TEvent : IMatchEngineEvent;
        void Subscribe<TEvent>(MatchEngineEventHandler<TEvent> handler) where TEvent : IMatchEngineEvent;
    }
}
