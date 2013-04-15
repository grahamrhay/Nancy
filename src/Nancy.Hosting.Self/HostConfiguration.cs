﻿namespace Nancy.Hosting.Self
{
    using System;
    using System.Diagnostics;

    /// <summary>
    /// Host configuration for the self host
    /// </summary>
    public sealed class HostConfiguration
    {
        /// <summary>
        /// Gets or sets a property that determines if localhost uris are 
        /// rewritten to htp://+:port/ style uris to allow for listening on 
        /// all ports, but requiring either a url reservation, or admin
        /// access
        /// </summary>
        public bool RewriteLocalhost { get; set; }

        /// <summary>
        /// Gets or sets a property that determines if namespace
        /// reservations are created, when necessary
        /// </summary>
        public bool CreateNamespaceReservations { get; set; }

        /// <summary>
        /// Gets or sets a property that provides a callback to be called
        /// if there's an unhandled exception in the self host.
        /// Note: this will *not* be called for normal nancy Exceptions
        /// that are handled by the Nancy handlers.
        /// Defaults to writing to debug out.
        /// </summary>
        public Action<Exception> UnhandledExceptionCallback { get; set; }

        public HostConfiguration()
        {
            this.RewriteLocalhost = true;
            this.UnhandledExceptionCallback = e =>
                {
                    var message = String.Format("---\n{0}\n---\n", e);
                    Debug.Write(message);
                };
        }
    }
}