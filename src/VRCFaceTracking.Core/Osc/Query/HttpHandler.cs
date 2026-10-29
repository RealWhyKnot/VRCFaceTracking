using System.Net;
using Microsoft.Extensions.Logging;
using VRCFaceTracking.Core.Contracts;

namespace VRCFaceTracking.Core.Osc.Query;

public sealed partial class HttpHandler : IDisposable
{
    private readonly IOscTarget _oscTarget;
    private readonly ILogger<HttpHandler> _logger;
    private readonly HttpListener _listener = new();
    private string _appName = "VRCFT";
    private int _oscPort = 9001;

    public event EventHandler? HostInfoQueried;

    public HttpHandler(IOscTarget oscTarget, ILogger<HttpHandler> logger)
    {
        _oscTarget = oscTarget;
        _logger = logger;
    }

    public void BindTo(Uri prefix, int oscPort)
    {
        _oscPort = oscPort;
        _listener.Stop();
        _listener.Prefixes.Clear();
        _listener.Prefixes.Add(prefix.AbsoluteUri);
        _listener.Start();
        _ = _listener.BeginGetContext(OnContextReceived, _listener);
    }

    public void SetAppName(string newAppName) => _appName = newAppName;

    private void OnContextReceived(IAsyncResult result)
    {
        HttpListenerContext context;
        try
        {
            context = _listener.EndGetContext(result);
            _ = _listener.BeginGetContext(OnContextReceived, _listener);
        }
        catch (Exception e) when (e is HttpListenerException or ObjectDisposedException or InvalidOperationException)
        {
            return;
        }

        _ = RespondAsync(context, CancellationToken.None);
    }

    private async Task RespondAsync(HttpListenerContext context, CancellationToken cancellationToken)
    {
        try
        {
            string respStr;
            if ((context.Request.RawUrl ?? string.Empty).Contains("HOST_INFO", StringComparison.Ordinal))
            {
                var hostInfo = new OscQueryHostInfo
                {
                    Name = _appName,
                    OscIP = _oscTarget.DestinationAddress,
                    OscPort = _oscPort,
                };
                respStr = hostInfo.ToString();
                HostInfoQueried?.Invoke(this, EventArgs.Empty);
                LogRespondingOscqueryHostInfoRequest(_logger, respStr);
            }
            else
            {
                if (context.Request.Url != null && !string.Equals(context.Request.Url.LocalPath, "/", StringComparison.Ordinal))
                {
                    context.Response.Close();
                    return;
                }

                respStr = new OscQueryRoot().ToString();
            }

            context.Response.Headers.Add("pragma:no-cache");

            context.Response.ContentType = "application/json";
            byte[] respBytes = System.Text.Encoding.UTF8.GetBytes(respStr);
            context.Response.ContentLength64 = respBytes.Length;
            await context.Response.OutputStream.WriteAsync(respBytes, cancellationToken).ConfigureAwait(false);
            context.Response.Close();
        }
        catch (Exception e) when (e is HttpListenerException or IOException or ObjectDisposedException or InvalidOperationException)
        {
            LogFailedAnswerOSCQueryRequest(_logger, e);
            context.Response.Abort();
        }
    }

    public void Dispose() => _listener.Close();

    [LoggerMessage(Level = LogLevel.Debug, Message = "Responding to oscquery host info request with {RespStr}")]
    private static partial void LogRespondingOscqueryHostInfoRequest(ILogger logger, string respStr);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to answer an OSCQuery request")]
    private static partial void LogFailedAnswerOSCQueryRequest(ILogger logger, Exception exception);
}
