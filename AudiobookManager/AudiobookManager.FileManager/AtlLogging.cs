using ATL.Logging;
using Microsoft.Extensions.Logging;

namespace AudiobookManager.FileManager;
public class AtlLogging : ILogDevice, IAtlLogging
{
    private Log _log = new Log();
    private readonly ILogger _logger;

    public AtlLogging(ILoggerFactory loggerFactory)
    {
        _logger = loggerFactory.CreateLogger("ATL");

        LogDelegator.SetLog(ref _log);
        _log.Register(this);
    }

    public void DoLog(Log.LogItem logItem)
    {
        LogLevel logLevel = logItem.Level switch
        {
            Log.LV_ERROR => LogLevel.Error,
            Log.LV_WARNING => LogLevel.Warning,
            Log.LV_INFO => LogLevel.Information,
            Log.LV_DEBUG => LogLevel.Debug,
            _ => LogLevel.Trace
        };

        _logger.Log(logLevel, message: logItem.Message);
    }
}
