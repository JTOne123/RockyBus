﻿using Microsoft.Azure.Management.ServiceBus;
using Microsoft.Azure.Management.ServiceBus.Models;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace RockyBus.Azure.ServiceBus
{
    class ReceivingEndpoint
    {
        readonly AzureServiceBusConfiguration configuration;
        readonly IServiceBusManagementClient sbManagementClient;
        readonly string topicName;
        readonly IEnumerable<string> eventMessageTypeNames;

        public ReceivingEndpoint(AzureServiceBusConfiguration configuration,
                                 IServiceBusManagementClient sbManagementClient,
                                 string topicName,
                                 IEnumerable<string> eventMessageTypeNames)
        {
            this.configuration = configuration;
            this.sbManagementClient = sbManagementClient;
            this.topicName = topicName;
            this.eventMessageTypeNames = eventMessageTypeNames;
        }

        public async Task Initialize()
        {
            await CreateOrUpdateReceivingQueue().ConfigureAwait(false);
            await CreateOrUpdateForwardingSubscription().ConfigureAwait(false);

            var eventMessageFilter = CreateEventMessageFilter();
            var commandMessageFilter = new Rule
            {
                SqlFilter = new SqlFilter(
                    $"user.{UserProperties.DestinationQueueKey}='{configuration.ReceiveOptions.QueueName}'")
            };

            //429 too many requests if running rules at the same time
            await CreateOrUpdateRule(nameof(eventMessageFilter), eventMessageFilter).ConfigureAwait(false);
            await CreateOrUpdateRule(nameof(commandMessageFilter), commandMessageFilter).ConfigureAwait(false);
        }

        Task CreateOrUpdateReceivingQueue()
        {
            var sbQueue = configuration.ReceiveOptions.SBQueue ?? new SBQueue { };
            sbQueue.ForwardTo = null;
            return Retry.Do(() =>
                sbManagementClient.Queues.CreateOrUpdateAsync(
                    configuration.ResourceGroupName,
                    configuration.NamespaceName,
                    configuration.ReceiveOptions.QueueName,
                    sbQueue));
        }

        Task CreateOrUpdateForwardingSubscription()
        {
            var sbSubscription = configuration.ReceiveOptions.SBSubscription ?? new SBSubscription { };
            sbSubscription.ForwardTo = configuration.ReceiveOptions.QueueName.ToLower();
            return Retry.Do(() =>
                sbManagementClient.Subscriptions.CreateOrUpdateAsync(
                    configuration.ResourceGroupName,
                    configuration.NamespaceName,
                    topicName,
                    configuration.ReceiveOptions.QueueName,
                    sbSubscription));
        }

        Rule CreateEventMessageFilter()
        {
            return eventMessageTypeNames.Any() ?
                                        new Rule
                                        {
                                            SqlFilter = new SqlFilter(
                                                $"user.{UserProperties.MessageTypeKey} IN ({string.Join(",", eventMessageTypeNames.Select(n => $"'{n}'"))})")
                                        }
                : new Rule { SqlFilter = new SqlFilter("user.alwaysfalse IS NOT NULL") };
        }

        Task CreateOrUpdateRule(string filterName, Rule rule) =>
            Retry.Do(() =>
                sbManagementClient.Rules.CreateOrUpdateAsync(
                    configuration.ResourceGroupName,
                    configuration.NamespaceName,
                    topicName,
                    configuration.ReceiveOptions.QueueName,
                    filterName,
                    rule)
            );
    }
}
