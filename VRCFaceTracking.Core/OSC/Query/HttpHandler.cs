using System.Net;
using Microsoft.Extensions.Logging;
using VRCFaceTracking.Core.Contracts;

namespace VRCFaceTracking.Core.OSC.Query.mDNS;

public class HttpHandler(IOscTarget oscTarget, ILogger<HttpHandler> logger) : IDisposable
{
    private readonly HttpListener _listener = new();
    private IAsyncResult _contextListenerResult;
    private string _appName = "VRCFT";
    private int _oscPort = 9001;

    public Action OnHostInfoQueried = () => { };

    public void BindTo(string uri, int oscPort)
    {
        _oscPort = oscPort;
        try
        {
            _listener.Stop();
        }
        catch (ObjectDisposedException)
        {
        }
        _listener.Prefixes.Clear();
        _listener.Prefixes.Add(uri);
        _listener.Start();
        _contextListenerResult = _listener.BeginGetContext(HttpListenerLoop, _listener);
    }

    public void SetAppName(string newAppName)
    {
        _appName = newAppName;
    }

    private async void HttpListenerLoop(IAsyncResult result)
    {
        HttpListenerContext context;
        try
        {
            context = _listener.EndGetContext(result);
            _contextListenerResult = _listener.BeginGetContext(HttpListenerLoop, _listener);
        }
        catch (Exception)
        {
            return;
        }

        try
        {
            string respStr;
            if ((context.Request.RawUrl ?? string.Empty).Contains("HOST_INFO"))
            {
                var hostInfo = new OscQueryHostInfo
                {
                    name = _appName,
                    oscIP = oscTarget.DestinationAddress,
                    oscPort = _oscPort
                };
                respStr = hostInfo.ToString();
                OnHostInfoQueried();
                logger.LogDebug($"Responding to oscquery host info request with {respStr}");
            }
            else
            {
                if (context.Request.Url != null && context.Request.Url.LocalPath != "/")
                {
                    context.Response.Close();
                    return; // Not properly implementing oscquery protocol because I'm unemployed and not being paid to
                }

                var rootNode = new OscQueryRoot();
                rootNode.AddNode(new OscQueryNode("/avatar/change", AccessValues.WriteOnly, "s"));

                respStr = rootNode.ToString();
            }

            // Send Response
            context.Response.Headers.Add("pragma:no-cache");

            context.Response.ContentType = "application/json";
            var respBytes = System.Text.Encoding.UTF8.GetBytes(respStr);
            context.Response.ContentLength64 = respBytes.Length;
            await context.Response.OutputStream.WriteAsync(respBytes);
            context.Response.Close();
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "Failed to answer an OSCQuery request");
            try
            {
                context.Response.Abort();
            }
            catch (Exception)
            {
            }
        }
    }

    public void Dispose()
    {
        _listener.Stop();
    }
}
