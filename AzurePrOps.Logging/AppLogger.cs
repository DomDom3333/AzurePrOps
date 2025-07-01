using Microsoft.Extensions.Logging;

namespace AzurePrOps.Logging;

public static class AppLogger
{
    private static ILoggerFactory _loggerFactory = LoggerFactory.Create(builder =>
    {
        builder.SetMinimumLevel(LogLevel.Information);
        builder.AddSimpleConsole(options =>
        {
            options.TimestampFormat = "hh:mm:ss ";
        });
    });

    public static ILoggerFactory Factory => _loggerFactory;

    public static void Configure(Action<ILoggingBuilder> configure)
    {
        _loggerFactory = LoggerFactory.Create(configure);
    }

    public static ILogger CreateLogger(string categoryName) => _loggerFactory.CreateLogger(categoryName);

    public static ILogger<T> CreateLogger<T>() => _loggerFactory.CreateLogger<T>();
}
