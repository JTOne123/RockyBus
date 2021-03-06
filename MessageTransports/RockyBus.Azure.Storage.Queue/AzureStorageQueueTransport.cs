﻿using Microsoft.Azure.Storage;
using Microsoft.Azure.Storage.Queue;
using RockyBus.Message;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace RockyBus
{
    internal class AzureStorageQueueTransport : IMessageTransport
    {
        private const string MessageTypeKey = "MessageType";
        private readonly AzureStorageQueueConfiguration configuration = new AzureStorageQueueConfiguration { };
        private readonly CloudQueueClient queueClient;
        private CloudQueue receivingQueue;
        private CloudQueue receivingPoisonQueue;
        private CloudQueue debugQueue;
        private readonly System.Timers.Timer timer = new System.Timers.Timer(TimeSpan.FromSeconds(5).TotalMilliseconds);
        private readonly CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();
        public IReceivingMessageTypeNames ReceivingMessageTypeNames { get; set; }

        public bool IsPublishAndSendOnly => string.IsNullOrWhiteSpace(configuration.ReceiveOptions.QueueName);

        public IBus Bus { get; set; }

        public AzureStorageQueueTransport(string connectionString, Action<AzureStorageQueueConfiguration> configuration)
        {
            configuration(this.configuration);

            var storageAccount = CloudStorageAccount.Parse(connectionString);
            queueClient = storageAccount.CreateCloudQueueClient();
        }

        public async Task InitializePublishingEndpoint()
        {
            if (!configuration.PublishAndSendOptions.PublishAndSendToDebugQueue) return;
            debugQueue = queueClient.GetQueueReference("debug");
            await debugQueue.CreateIfNotExistsAsync().ConfigureAwait(false);
        }

        public async Task InitializeReceivingEndpoint()
        {
            var queueName = configuration.ReceiveOptions.QueueName.ToLower();
            if (string.IsNullOrWhiteSpace(queueName)) return;

            this.receivingQueue = queueClient.GetQueueReference(queueName);
            await receivingQueue.CreateIfNotExistsAsync().ConfigureAwait(false);
            this.receivingPoisonQueue = queueClient.GetQueueReference(queueName + "-poison");
            await receivingPoisonQueue.CreateIfNotExistsAsync().ConfigureAwait(false);

            receivingQueue.Metadata.Add(
                MessageTypeKey,
                string.Join(",", ReceivingMessageTypeNames.ReceivingEventMessageTypeNames));
            await receivingQueue.SetMetadataAsync().ConfigureAwait(false);
        }

        public async Task Publish<T>(T message, string messageTypeName, PublishOptions publishOptions = null)
        {
            foreach (var queueName in configuration.PublishAndSendOptions.AvailablePublishingQueues)
            {
                var queue = queueClient.GetQueueReference(queueName.ToLower());
                if (!await queue.ExistsAsync().ConfigureAwait(false)) return;

                await queue.FetchAttributesAsync().ConfigureAwait(false);

                if (!queue.Metadata.ContainsKey(MessageTypeKey)) return;

                if (queue.Metadata[MessageTypeKey].Split(',').Contains(messageTypeName))
                {
                    await queue.AddMessageAsync(message.WrapAndCreateCloudQueueMessage(messageTypeName)).ConfigureAwait(false);
                }

                if(debugQueue != null) await debugQueue.AddMessageAsync(message.WrapAndCreateCloudQueueMessage(messageTypeName)).ConfigureAwait(false);
            }
        }

        public async Task Send<T>(T message, string messageTypeName, SendOptions sendOptions = null)
        {
            var queue = queueClient.GetQueueReference(configuration.PublishAndSendOptions.GetQueue<T>().ToLower());
            if (!await queue.ExistsAsync().ConfigureAwait(false)) return;

            var cloudQueueMessage = message.WrapAndCreateCloudQueueMessage(messageTypeName);
            await queue.AddMessageAsync(cloudQueueMessage).ConfigureAwait(false);
            if (debugQueue != null) await debugQueue.AddMessageAsync(cloudQueueMessage).ConfigureAwait(false);
        }

        public Task StartReceivingMessages(MessageHandlerExecutor messageHandlerExecutor)
        {
            if (receivingQueue == null) return Task.CompletedTask;

            timer.Elapsed += async (sender, e) =>
            {
                var cloudQueueMessage = await receivingQueue.GetMessageAsync(
                    configuration.ReceiveOptions.MaxAutoRenewDuration,
                    new QueueRequestOptions { MaximumExecutionTime = configuration.ReceiveOptions.MaxAutoRenewDuration },
                    new OperationContext()).ConfigureAwait(false);
                if (cloudQueueMessage == null) return;
                var message = cloudQueueMessage.UnwrapAndDeserializeMessage(ReceivingMessageTypeNames);
                try
                {
                    await messageHandlerExecutor.Execute(
                        message,
                        new AzureStorageQueueMessageContext(Bus, cloudQueueMessage),
                        cancellationTokenSource.Token).ConfigureAwait(false);
                    await receivingQueue.DeleteMessageAsync(cloudQueueMessage);
                }
                catch (Exception)
                {
                    if (cloudQueueMessage.DequeueCount < configuration.ReceiveOptions.MaxDequeueCount)
                        await receivingQueue.UpdateMessageAsync(
                            cloudQueueMessage,
                            TimeSpan.Zero,
                            MessageUpdateFields.Visibility).ConfigureAwait(false);
                    else
                    {
                        await receivingQueue.DeleteMessageAsync(cloudQueueMessage);
                        await receivingPoisonQueue.AddMessageAsync(cloudQueueMessage);
                    }
                };
            };
            timer.Start();
            return Task.CompletedTask;
        }

        public Task StopReceivingMessages()
        {
            timer.Stop();
            cancellationTokenSource.Cancel();
            return Task.CompletedTask;
        }
    }
}
