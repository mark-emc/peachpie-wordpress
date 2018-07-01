﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.ResponseCaching.Internal;
using Microsoft.Extensions.Primitives;
using Microsoft.Net.Http.Headers;

namespace Peachpie.WordPress.AspNetCore.Internal
{
    /// <summary>
    /// <see cref="IResponseCachingPolicyProvider"/> implementation for WordPress requests.
    /// </summary>
    internal sealed class WpResponseCachingPolicyProvider : IResponseCachingPolicyProvider, IWpPlugin
    {
        /// <summary>
        /// Time of the last content update.
        /// </summary>
        DateTime LastPostUpdate = DateTime.UtcNow;

        public bool AllowCacheLookup(ResponseCachingContext context)
        {
            var req = context.HttpContext.Request;

            // cache-control: nocache ?
            if (HeaderUtilities.ContainsCacheDirective(req.Headers[HeaderNames.CacheControl], CacheControlHeaderValue.NoCacheString))
            {
                return false;
            }

            // wp-admin ?
            if (req.Path.Value.Contains("/wp-admin"))
            {
                return false;
            }

            //
            return true;
        }

        public bool AllowCacheStorage(ResponseCachingContext context)
        {
            // cache-control: no-store ?
            return !HeaderUtilities.ContainsCacheDirective(context.HttpContext.Request.Headers[HeaderNames.CacheControl], CacheControlHeaderValue.NoStoreString);
        }

        public bool AttemptResponseCaching(ResponseCachingContext context)
        {
            var req = context.HttpContext.Request;

            // only GET and HEAD methods are cacheable
            if (HttpMethods.IsGet(req.Method) || HttpMethods.IsHead(req.Method))
            {
                // only if wp user is not logged in
                if (!req.Cookies.Any(cookie => cookie.Key.StartsWith("wordpress_logged_in")))
                {
                    return true;
                }
            }

            return false;
        }

        public bool IsCachedEntryFresh(ResponseCachingContext context)
        {
            return (context.ResponseTime.Value - context.CachedEntryAge.Value) >= LastPostUpdate;
        }

        public bool IsResponseCacheable(ResponseCachingContext context)
        {
            var responseCacheControlHeader = context.HttpContext.Response.Headers[HeaderNames.CacheControl];

            // Check response no-store
            if (HeaderUtilities.ContainsCacheDirective(responseCacheControlHeader, CacheControlHeaderValue.NoStoreString))
            {
                return false;
            }

            // Check no-cache
            if (HeaderUtilities.ContainsCacheDirective(responseCacheControlHeader, CacheControlHeaderValue.NoCacheString))
            {
                return false;
            }

            var response = context.HttpContext.Response;

            // Do not cache responses with Set-Cookie headers
            if (!StringValues.IsNullOrEmpty(response.Headers[HeaderNames.SetCookie]))
            {
                return false;
            }

            // Do not cache responses varying by *
            var varyHeader = response.Headers[HeaderNames.Vary];
            if (varyHeader.Count == 1 && string.Equals(varyHeader, "*", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            // Check private
            if (HeaderUtilities.ContainsCacheDirective(responseCacheControlHeader, CacheControlHeaderValue.PrivateString))
            {
                return false;
            }

            // Check response code
            if (response.StatusCode != StatusCodes.Status200OK)
            {
                return false;
            }

            //
            context.HttpContext.Response.Headers[HeaderNames.CacheControl] = StringValues.Concat(CacheControlHeaderValue.SharedMaxAgeString + "=" + 60*60,  responseCacheControlHeader);

            //
            return true;
        }

        void IWpPlugin.Configure(IWpApp app)
        {
            Action updated = () =>
            {
                // existing cache records get invalidated
                LastPostUpdate = DateTime.UtcNow;
            };

            app.AddFilter("save_post", updated);
        }
    }
}