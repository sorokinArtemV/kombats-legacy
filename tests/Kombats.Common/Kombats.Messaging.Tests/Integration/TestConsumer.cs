using System.Collections.Concurrent;
using MassTransit;

namespace Kombats.Messaging.Tests.Integration;

public class TestConsumer : IConsumer<TestEvent>
{
    public static readonly ConcurrentBag<TestEvent> ReceivedMessages = new();
    public static readonly SemaphoreSlim MessageReceived = new(0);

    public Task Consume(ConsumeContext<TestEvent> context)
    {
        ReceivedMessages.Add(context.Message);
        MessageReceived.Release();
        return Task.CompletedTask;
    }

    public static void Reset()
    {
        ReceivedMessages.Clear();
        // Drain the semaphore
        while (MessageReceived.CurrentCount > 0)
        {
            MessageReceived.Wait(0);
        }
    }
}
