using Confluent.Kafka;
using Shop.Contracts;
using Shop.Web.Diagnostics;

namespace Shop.Tests;

public sealed class DiagnosticsKafkaConfigurationTests
{
    [Fact]
    public void ConsumerConfiguration_IsBounded_ReadOnly_AndBuildsWithoutAvailableBroker()
    {
        var config = KafkaMonitoring.CreateConsumerConfig(new KafkaOptions
        {
            BootstrapServers = "127.0.0.1:1",
            Topic = "diagnostics-configuration-test"
        }, "diagnostics-configuration-test");

        Assert.False(config.EnableAutoCommit);
        Assert.False(config.EnableAutoOffsetStore);
        Assert.False(config.AllowAutoCreateTopics);
        Assert.True(config.MessageMaxBytes <= config.FetchMaxBytes);
        Assert.True(config.MaxPartitionFetchBytes <= config.FetchMaxBytes);
        Assert.True(config.ReceiveMessageMaxBytes >= config.FetchMaxBytes + 512);
        Assert.Equal(256 * 1024, config.FetchMaxBytes);
        Assert.Equal(256, config.QueuedMaxMessagesKbytes);

        // Native validation caught the original incompatible fetch.max.bytes/message.max.bytes pair.
        // Build validates locally; no metadata, Subscribe, Assign, or broker request is performed here.
        using var consumer = new ConsumerBuilder<byte[], byte[]>(config)
            .SetLogHandler((_, _) => { })
            .SetErrorHandler((_, _) => { })
            .Build();
        Assert.Empty(consumer.Assignment);
        Assert.Empty(consumer.Subscription);
    }
}
