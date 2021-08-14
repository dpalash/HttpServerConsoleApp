using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace HttpServerConsoleApp
{
    class Program
    {
        static void Main(string[] args)
        {
            //var assembly = typeof(Program).Assembly;
            //var attribute = (GuidAttribute)assembly.GetCustomAttributes(typeof(GuidAttribute), true)[0];
            //var id = attribute.Value;
            //Console.WriteLine(id);


            var prefixes = new string[] { "http://127.0.0.1:8083/", "https://127.0.0.1:8090/" };

            HttpServer server = new HttpServer(prefixes);
            // Add the HttpRequestHandlers
            server.AddHttpRequestHandler(new MorningHttpRequestHandler());
            server.AddHttpRequestHandler(new AfternoonHttpRequestHandler());
            // Start the server
            server.Start();

            Console.ReadKey();
        }



    }






    public class HttpServer : IDisposable
    {
        private HttpListener _httpListener = null;
        private Thread _connectionThread = null;
        private Boolean _running, _disposed;

        private HttpResourceLocator _resourceLocator = null;

        public HttpServer(string[] prefixes)
        {
            if (!HttpListener.IsSupported)
            {
                // Requires at least a Windows XP with Service Pack 2
                throw new NotSupportedException(
                    "The Http Server cannot run on this operating system.");
            } // end if HttpListener is not supported

            _httpListener = new HttpListener();
            // Add the prefixes to listen to

            foreach (string s in prefixes)
                _httpListener.Prefixes.Add(s);

            _resourceLocator = new HttpResourceLocator();

        } // end WebServer()

        public void AddHttpRequestHandler(HttpRequestHandler requestHandler)
        {
            _resourceLocator.AddHttpRequestHandler(requestHandler);
        }

        public void Start()
        {
            if (!_httpListener.IsListening)
            {
                _httpListener.Start();
                _running = true;
                // Use a thread to listen to the Http requests
                _connectionThread = new Thread(new ThreadStart(this.ConnectionThreadStart));
                _connectionThread.Start();
            } // end if httpListener is not listening

        } // end public void start()

        public void Stop()
        {
            if (_httpListener.IsListening)
            {
                _running = false;
                _httpListener.Stop();
            } // end if httpListener is listening
        } // end public void stop()

        // Action body for _connectionThread
        private void ConnectionThreadStart()
        {
            try
            {
                while (_running)
                {
                    // Grab the context and pass it to the HttpResourceLocator to handle it
                    HttpListenerContext context = _httpListener.GetContext();

                    var clientCertificate = context.Request.GetClientCertificate();
                    X509Chain chain = new X509Chain();
                    chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
                    chain.Build(clientCertificate);
                    if (chain.ChainStatus.Length != 0)
                    {
                        // Invalid certificate
                        context.Response.StatusCode = (int)HttpStatusCode.Unauthorized;
                        context.Response.Close();
                    }
                    else
                    {
                        _resourceLocator.HandleContext(context);
                    }
                } // while running
            }
            catch (HttpListenerException)
            {
                // This will occurs when the listener gets shutdown.
                Console.WriteLine("HTTP server was shut down.");
            } // end try-catch

        } // end private void connectionThreadStart()

        public virtual void Dispose()
        {
            this.Dispose(true);
            GC.SuppressFinalize(this);
        } // end public virtual void Dispose()

        private void Dispose(bool disposing)
        {
            if (this._disposed)
            {
                return;
            }
            if (disposing)
            {
                if (this._running)
                {
                    this.Stop();
                }
                if (this._connectionThread != null)
                {
                    this._connectionThread.Abort();
                    this._connectionThread = null;
                }
            }
            this._disposed = true;
        } // private void Dispose(bool disposing)

    } // end class HttpServer

    public class HttpResourceLocator
    {
        private Dictionary<string, HttpRequestHandler> _httpRequestHandlers;

        public HttpResourceLocator()
        {
            _httpRequestHandlers = new Dictionary<string, HttpRequestHandler>();
            // Add the default handler that will handle invalid web request
            this.AddHttpRequestHandler(new InvalidHttpRequestHandler());

        } // end private HttpRequestController()

        public void AddHttpRequestHandler(HttpRequestHandler httpRequestHandler)
        {
            // If the httpRequestHandler is not yet added
            if (!_httpRequestHandlers.ContainsKey(httpRequestHandler.GetName()))
            {
                // Add a new record
                _httpRequestHandlers.Add(httpRequestHandler.GetName(), httpRequestHandler);
            }
            else
            {
                // Replace it
                _httpRequestHandlers[httpRequestHandler.GetName()] = httpRequestHandler;
            }
        } // end public void AddHttpRequestHandler(HttpRequestHandler httpRequestHandler)

        public void HandleContext(HttpListenerContext listenerContext)
        {

            // Search for the requested handler
            HttpListenerRequest request = listenerContext.Request;
            // Use the absolute path of the url to find the request
            // handler
            string requestHandlerName = request.Url.AbsolutePath;

            // Find the request handler to handle the request

            HttpRequestHandler handler;
            // If request handler is found
            if (_httpRequestHandlers.ContainsKey(requestHandlerName))
            {
                // Get the corresponding request handler
                handler = _httpRequestHandlers[requestHandlerName];
            }
            else
            {
                // Use the InvalidHttpRequestHandler to handle the request
                handler = _httpRequestHandlers[InvalidHttpRequestHandler.NAME];
            } // end if

            this.InvokeHandler(handler, listenerContext);

        } // end public void handleContext(HttpListenerContext listenerContext)

        private void InvokeHandler(HttpRequestHandler handler,
            HttpListenerContext context)
        {
            // Start a new thread to invoke the handler to process the HTTP request
            HandleHttpRequestCommand handleHttpRequestCommand
                = new HandleHttpRequestCommand(handler, context);
            Thread handleHttpRequestThread = new Thread(handleHttpRequestCommand.Execute);
            handleHttpRequestThread.Start();
        } // end private void InvokeHandler(HttpRequestHandler handler,
          //         HttpListenerContext context)

        // Helper class for invoking handler to process
        // HTTP request
        public class HandleHttpRequestCommand
        {
            private HttpRequestHandler _handler;
            private HttpListenerContext _context;

            public HandleHttpRequestCommand(HttpRequestHandler handler,
                HttpListenerContext context)
            {
                this._handler = handler;
                this._context = context;
            }

            public void Execute()
            {
                this._handler.Handle(this._context);
            }
        } // end public class HandleHttpRequestCommand

    } // end public class HttpResourceLocator



    public class AfternoonHttpRequestHandler : HttpRequestHandler
    {
        public const string NAME = "/Afternoon";

        public void Handle(HttpListenerContext context)
        {
            HttpListenerResponse response = context.Response;
            response.StatusCode = (int)HttpStatusCode.OK;

            // Get name from query string
            string name = context.Request.QueryString["name"];
            string message;
            if (name == null)
            {
                message = "Good afternoon stranger!";
            }
            else
            {
                message = "Good afternoon " + name + "!";
            } // end if

            // Fill in response body
            byte[] messageBytes = Encoding.Default.GetBytes(message);
            response.OutputStream.Write(messageBytes, 0, messageBytes.Length);
            // Send the HTTP response to the client
            response.Close();

        } // end public void Handle(HttpListenerContext context)

        public string GetName()
        {
            return NAME;
        } // end public string GetName()

    } // end public class AfternoonHttpRequestHandler



    public class MorningHttpRequestHandler : HttpRequestHandler
    {
        public const string NAME = "/Morning";

        public void Handle(HttpListenerContext context)
        {

            HttpListenerResponse response = context.Response;
            response.StatusCode = (int)HttpStatusCode.OK;

            // Get name from query string
            string name = context.Request.QueryString["name"];
            string message;
            if (name == null)
            {
                message = "Good morning stranger!";
            }
            else
            {
                message = "Good morning " + name + "!";
            } // end if

            // Fill in response body
            byte[] messageBytes = Encoding.Default.GetBytes(message);
            response.OutputStream.Write(messageBytes, 0, messageBytes.Length);
            // Send the HTTP response to the client
            response.Close();

        } // end public void Handle(HttpListenerContext context)

        public string GetName()
        {
            return NAME;
        }
    } // end public class MorningHttpRequestHandler



    public class InvalidHttpRequestHandler : HttpRequestHandler
    {
        public const string NAME = "/InvalidWebRequestHandler";

        public void Handle(HttpListenerContext context)
        {
            HttpListenerResponse serverResponse = context.Response;

            // Indicate the failure as a 404 not found
            serverResponse.StatusCode = (int)HttpStatusCode.NotFound;

            // Fill in the response body
            string message = "Could not find resource.";
            byte[] messageBytes = Encoding.Default.GetBytes(message);
            serverResponse.OutputStream.Write(messageBytes, 0, messageBytes.Length);

            // Send the HTTP response to the client
            serverResponse.Close();

            // Print a message to console indicate invalid request as well
            Console.WriteLine("Invalid request from client. Request string: "
                + context.Request.RawUrl);
        } // end public void handle(HttpListenerContext context)

        public string GetName()
        {
            return NAME;
        } // end public string GetName()

    } // end public class InvalidHttpRequestHandler


    public interface HttpRequestHandler
    {
        void Handle(HttpListenerContext context);

        string GetName();

    } // end public interface HttpRequestHandler
}
