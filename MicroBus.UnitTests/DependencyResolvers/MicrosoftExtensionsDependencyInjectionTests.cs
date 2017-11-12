﻿using System;
using System.Threading.Tasks;
using FluentAssertions;
using MicroBus.DemoMessages;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace MicroBus.UnitTests.DependencyResolvers
{
    [TestClass]
    public class MicrosoftExtensionsDependencyInjectionTests
    {
        [TestMethod]
        public void executing_message_handler_without_registering_should_throw_an_exception()
        {
            var serviceProvider = new ServiceCollection()
                .BuildServiceProvider();
            var executor = new MessageHandlerExecutor(new MicrosoftDependencyInjectionDependencyResolver(serviceProvider));

            Action action = () => executor.Execute(new AppleCommand(), new System.Threading.CancellationToken());

            action.ShouldThrow<TypeAccessException>();
        }

        [TestMethod]
        public void register_message_handler_as_direct_implementation_should_resolve_and_execute()
        {
            var serviceProvider = new ServiceCollection()
                .AddScoped<IMessageHandler<AppleCommand>, AppleCommandHandler>()
                .BuildServiceProvider();
            var executor = new MessageHandlerExecutor(new MicrosoftDependencyInjectionDependencyResolver(serviceProvider));

            var task = executor.Execute(new AppleCommand(), new System.Threading.CancellationToken());

            task.IsCompleted.Should().BeTrue();
        }

        [TestMethod]
        public void register_message_handler_as_an_indirect_implementation_should_resolve_and_execute()
        {
            var serviceProvider = new ServiceCollection()
                .AddScoped<IMessageHandler<BananaCommand>, BananaCommandHandler>()
                .BuildServiceProvider();
            var executor = new MessageHandlerExecutor(new MicrosoftDependencyInjectionDependencyResolver(serviceProvider));

            var task = executor.Execute(new BananaCommand(), new System.Threading.CancellationToken());

            task.IsCompleted.Should().BeTrue();
        }

        [TestMethod]
        public async Task thrown_exception_in_message_handler_should_call_exception_handler()
        {
            Exception exception = null;
            Func<MessageHandlingExceptionRaisedEventArgs, Task> exceptionHandler = a =>
            {
                exception = a.Exception;
                return Task.CompletedTask;
            };
            var serviceProvider = new ServiceCollection()
                .AddScoped<IMessageHandler<AppleCommand>, RottenAppleCommandHandler>()
                .BuildServiceProvider();
            var executor = new MessageHandlerExecutor(new MicrosoftDependencyInjectionDependencyResolver(serviceProvider), exceptionHandler);

            await executor.Execute(new AppleCommand(), new System.Threading.CancellationToken());

            exception.Message.Should().Be("Rotten Apple");
        }

    }
}
