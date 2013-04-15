﻿namespace Nancy.Hosting.Self
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Net;
    using System.Linq;
    using System.Security.Principal;
    using IO;
    using Nancy.Bootstrapper;
    using Nancy.Extensions;
	using Nancy.Helpers;

    /// <summary>
    /// Allows to host Nancy server inside any application - console or windows service.
    /// </summary>
    /// <remarks>
    /// NancyHost uses <see cref="System.Net.HttpListener"/> internally. Therefore, it requires full .net 4.0 profile (not client profile)
    /// to run. <see cref="Start"/> will launch a thread that will listen for requests and then process them. All processing is done
    /// within a single thread - self hosting is not intended for production use, but rather as a development server.
    /// NancyHost needs <see cref="SerializableAttribute"/> in order to be used from another appdomain under mono. Working with 
    /// AppDomains is necessary if you want to unload the dependencies that come with NancyHost.
    /// </remarks>
    [Serializable]
    public class NancyHost  
    {
        private readonly IList<Uri> baseUriList;
        private HttpListener listener;
        private readonly INancyEngine engine;
        private readonly HostConfiguration configuration;

        /// <summary>
        /// Initializes a new instance of the <see cref="NancyHost"/> class for the specfied <paramref name="baseUris"/>.
        /// Uses the default configuration
        /// </summary>
        /// <param name="baseUris">The <see cref="Uri"/>s that the host will listen to.</param>
        public NancyHost(params Uri[] baseUris)
            : this(NancyBootstrapperLocator.Bootstrapper, new HostConfiguration(), baseUris) { }

        /// <summary>
        /// Initializes a new instance of the <see cref="NancyHost"/> class for the specfied <paramref name="baseUris"/>.
        /// Uses the specified configuration.
        /// </summary>
        /// <param name="baseUris">The <see cref="Uri"/>s that the host will listen to.</param>
        /// <param name="configuration">Configuration to use</param>
        public NancyHost(HostConfiguration configuration, params Uri[] baseUris)
            : this(NancyBootstrapperLocator.Bootstrapper, configuration, baseUris){}

        /// <summary>
        /// Initializes a new instance of the <see cref="NancyHost"/> class for the specfied <paramref name="baseUris"/>, using
        /// the provided <paramref name="bootstrapper"/>.
        /// Uses the default configuration
        /// </summary>
        /// <param name="bootstrapper">The bootstrapper that should be used to handle the request.</param>
        /// <param name="baseUris">The <see cref="Uri"/>s that the host will listen to.</param>
        public NancyHost(INancyBootstrapper bootstrapper, params Uri[] baseUris)
            : this(bootstrapper, new HostConfiguration(), baseUris)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="NancyHost"/> class for the specfied <paramref name="baseUris"/>, using
        /// the provided <paramref name="bootstrapper"/>.
        /// Uses the specified configuration.
        /// </summary>
        /// <param name="bootstrapper">The bootstrapper that should be used to handle the request.</param>
        /// <param name="configuration">Configuration to use</param>
        /// <param name="baseUris">The <see cref="Uri"/>s that the host will listen to.</param>
        public NancyHost(INancyBootstrapper bootstrapper, HostConfiguration configuration, params Uri[] baseUris)
        {
            this.configuration = configuration ?? new HostConfiguration();
            this.baseUriList = baseUris;

            bootstrapper.Initialise();
            this.engine = bootstrapper.GetEngine();
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="NancyHost"/> class for the specfied <paramref name="baseUri"/>, using
        /// the provided <paramref name="bootstrapper"/>.
        /// Uses the default configuration
        /// </summary>
        /// <param name="baseUri">The <see cref="Uri"/> that the host will listen to.</param>
        /// <param name="bootstrapper">The bootstrapper that should be used to handle the request.</param>
        public NancyHost(Uri baseUri, INancyBootstrapper bootstrapper)
            : this(bootstrapper, new HostConfiguration(), baseUri)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="NancyHost"/> class for the specfied <paramref name="baseUri"/>, using
        /// the provided <paramref name="bootstrapper"/>.
        /// Uses the specified configuration.
        /// </summary>
        /// <param name="baseUri">The <see cref="Uri"/> that the host will listen to.</param>
        /// <param name="bootstrapper">The bootstrapper that should be used to handle the request.</param>
        /// <param name="configuration">Configuration to use</param>
        public NancyHost(Uri baseUri, INancyBootstrapper bootstrapper, HostConfiguration configuration)
            : this (bootstrapper, configuration, baseUri)
        {
        }

        /// <summary>
        /// Start listening for incoming requests with the given configuration
        /// </summary>
        public void Start()
        {
            this.StartListener();

            try
            {
                listener.BeginGetContext(GotCallback, null);
            }
            catch (Exception e)
            {
                this.configuration.UnhandledExceptionCallback.Invoke(e);

                throw;
            }
        }

        private void StartListener()
        {
            if (TryStartListener())
            {
                return;
            }

            if (!this.configuration.CreateNamespaceReservations)
            {
                throw new UnableToCreateNamespaceReservationsException(this.GetPrefixes(), GetUser());
            }

            if (!TryAddNamespaceReservation())
            {
                throw new InvalidOperationException("Unable to configure namespace reservation");
            }

            if (!TryStartListener())
            {
                throw new InvalidOperationException("Unable to start listener");
            }
        }

        private bool TryStartListener()
        {
            try
            {
                // if the listener fails to start, it gets disposed; 
                // so we need a new one, each time.
                listener = new HttpListener();
                foreach (var prefix in GetPrefixes())
                {
                    listener.Prefixes.Add(prefix);
                }

                listener.Start();
                return true;
            }
            catch (HttpListenerException e)
            {
                if (e.ErrorCode == 5)
                {
                    // access denied
                    return false;
                }

                throw;
            }
        }

        private bool TryAddNamespaceReservation()
        {
            var user = GetUser();

            foreach (var prefix in GetPrefixes())
            {
                if (!NetSh.AddUrlAcl(prefix, user))
                {
                    return false;
                }
            }

            return true;
        }

        private static string GetUser()
        {
            return WindowsIdentity.GetCurrent().Name;
        }

        /// <summary>
        /// Stop listening for incoming requests.
        /// </summary>
        public void Stop()
        {
            listener.Stop();
        }

        private IEnumerable<string> GetPrefixes()
        {
            foreach (var baseUri in baseUriList)
            {
                var prefix = baseUri.ToString();

                if (this.configuration.RewriteLocalhost && !baseUri.Host.Contains("."))
                {
                    prefix = prefix.Replace("localhost", "+");
                }

                yield return prefix;
            }
        }

        private Request ConvertRequestToNancyRequest(HttpListenerRequest request)
        {
            var asyncResult = request.BeginGetClientCertificate(null, null);

            var baseUri = baseUriList.FirstOrDefault(uri => uri.IsCaseInsensitiveBaseOf(request.Url));

            if (baseUri == null)
            {
                throw new InvalidOperationException(String.Format("Unable to locate base URI for request: {0}",request.Url));
            }

            var expectedRequestLength =
                GetExpectedRequestLength(request.Headers.ToDictionary());

            var relativeUrl = baseUri.MakeAppLocalPath(request.Url);

            var nancyUrl = new Url {
                Scheme = request.Url.Scheme,
                HostName = request.Url.Host,
                Port = request.Url.IsDefaultPort ? null : (int?)request.Url.Port,
                BasePath = baseUri.AbsolutePath.TrimEnd('/'),
                Path = HttpUtility.UrlDecode(relativeUrl),
                Query = request.Url.Query,
                Fragment = request.Url.Fragment,
            };

            byte[] certificate = null;
            var x509Certificate = request.EndGetClientCertificate(asyncResult);

            if (x509Certificate != null)
            {
                certificate = x509Certificate.RawData;
            }

            return new Request(
                request.HttpMethod,
                nancyUrl,
                RequestStream.FromStream(request.InputStream, expectedRequestLength, false),
                request.Headers.ToDictionary(), 
                (request.RemoteEndPoint != null) ? request.RemoteEndPoint.Address.ToString() : null,
                certificate);
        }

        private static void ConvertNancyResponseToResponse(Response nancyResponse, HttpListenerResponse response)
        {
            foreach (var header in nancyResponse.Headers)
            {
                response.AddHeader(header.Key, header.Value);
            }

            foreach (var nancyCookie in nancyResponse.Cookies)
            {
                response.Headers.Add(HttpResponseHeader.SetCookie, nancyCookie.ToString());
            }

            if (nancyResponse.ContentType != null)
            {
                response.ContentType = nancyResponse.ContentType;
            }
            response.StatusCode = (int)nancyResponse.StatusCode;

            using (var output = response.OutputStream)
            {
                nancyResponse.Contents.Invoke(output);
            }
        }

        private static long GetExpectedRequestLength(IDictionary<string, IEnumerable<string>> incomingHeaders)
        {
            if (incomingHeaders == null)
            {
                return 0;
            }

            if (!incomingHeaders.ContainsKey("Content-Length"))
            {
                return 0;
            }

            var headerValue =
                incomingHeaders["Content-Length"].SingleOrDefault();

            if (headerValue == null)
            {
                return 0;
            }

            long contentLength;

            return !long.TryParse(headerValue, NumberStyles.Any, CultureInfo.InvariantCulture, out contentLength) ?
                0 : 
                contentLength;
        }

        private void GotCallback(IAsyncResult ar)
        {
            try
            {
                var ctx = listener.EndGetContext(ar);
                listener.BeginGetContext(GotCallback, null);
                Process(ctx);
            }
            catch (Exception e)
            {
                this.configuration.UnhandledExceptionCallback.Invoke(e);

                try
                {
                    listener.BeginGetContext(GotCallback, null);
                }
                catch
                {
                    this.configuration.UnhandledExceptionCallback.Invoke(e);
                }
            }
        }

        private void Process(HttpListenerContext ctx)
        {
            try
            {
                var nancyRequest = ConvertRequestToNancyRequest(ctx.Request);
                using (var nancyContext = engine.HandleRequest(nancyRequest))
                {
                    try
                    {
                        ConvertNancyResponseToResponse(nancyContext.Response, ctx.Response);
                    }
                    catch (Exception e)
                    {
                        this.configuration.UnhandledExceptionCallback.Invoke(e);
                    }
                }
            }
            catch (Exception e)
            {
                this.configuration.UnhandledExceptionCallback.Invoke(e);
            }
        }
    }
}