// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Threading;
using Microsoft.Extensions.Logging;

namespace Microsoft.AspNetCore.AzureAppServices.FunctionalTests
{
    internal class TestLogger: ILogger, IDisposable
    {
        private readonly ILoggerFactory _factory;

        private readonly ILogger _logger;

        private static readonly AsyncLocal<TestLogger> _currentLogger = new AsyncLocal<TestLogger>();

        public static TestLogger Current => _currentLogger.Value;

        public TestLogger(ILoggerFactory factory, ILogger logger)
        {
            _currentLogger.Value = this;
            _factory = factory;
            _logger = logger;
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
        {
            _logger.Log(logLevel, eventId, state, exception, formatter);
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return _logger.IsEnabled(logLevel);
        }

        public IDisposable BeginScope<TState>(TState state)
        {
            return _logger.BeginScope(state);
        }

        public void Dispose()
        {
            _currentLogger.Value = null;
            _factory.Dispose();
        }
    }
}