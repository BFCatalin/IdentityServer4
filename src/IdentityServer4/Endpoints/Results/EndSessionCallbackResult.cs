﻿// Copyright (c) Brock Allen & Dominick Baier. All rights reserved.
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.


using System.Linq;
using IdentityServer4.Validation;
using System.Threading.Tasks;
using IdentityServer4.Hosting;
using Microsoft.AspNetCore.Http;
using IdentityServer4.Services;
using Microsoft.Extensions.DependencyInjection;
using IdentityServer4.Stores;
using IdentityServer4.Models;
using System.Net;
using System;
using IdentityServer4.Extensions;
using IdentityServer4.Configuration;

namespace IdentityServer4.Endpoints.Results
{
    class EndSessionCallbackResult : IEndpointResult
    {
        private readonly EndSessionCallbackValidationResult _result;

        public EndSessionCallbackResult(EndSessionCallbackValidationResult result)
        {
            if (result == null) throw new ArgumentNullException(nameof(result));

            _result = result;
        }

        internal EndSessionCallbackResult(
            EndSessionCallbackValidationResult result,
            ISessionIdService sessionId,
            IClientSessionService clientList,
            IMessageStore<LogoutMessage> logoutMessageStore,
            IdentityServerOptions options)
            : this(result)
        {
            _sessionId = sessionId;
            _clientList = clientList;
            _logoutMessageStore = logoutMessageStore;
            _options = options;
        }

        private ISessionIdService _sessionId;
        private IClientSessionService _clientList;
        private IMessageStore<LogoutMessage> _logoutMessageStore;
        private IdentityServerOptions _options;

        void Init(HttpContext context)
        {
            _sessionId = _sessionId ?? context.RequestServices.GetRequiredService<ISessionIdService>();
            _clientList = _clientList ?? context.RequestServices.GetRequiredService<IClientSessionService>();
            _logoutMessageStore = _logoutMessageStore ?? context.RequestServices.GetRequiredService<IMessageStore<LogoutMessage>>();
            _options = _options ?? context.RequestServices.GetRequiredService<IdentityServerOptions>();
        }

        public async Task ExecuteAsync(HttpContext context)
        {
            Init(context);

            if (_result.LogoutId != null)
            {
                await _logoutMessageStore.DeleteAsync(_result.LogoutId);
            }

            if (_result.IsError)
            {
                context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
            }
            else
            {
                _sessionId.RemoveCookie();
                _clientList.RemoveCookie();

                context.Response.SetNoCache();
                AddXfoHeaders(context);
                AddCspHeaders(context);

                var html = GetHtml();
                await context.Response.WriteHtmlAsync(html);
            }
        }

        private void AddCspHeaders(HttpContext context)
        {
            var value = "default-src 'none'";
            var origins = _result.ClientLogoutUrls?.Select(x => x.GetOrigin()).Aggregate((x, y) => $"{x} {y}");
            if (origins != null)
            {
                // this hash matches the style being used below for the iframe
                value += $";style-src 'sha256-u+OupXgfekP+x/f6rMdoEAspPCYUtca912isERnoEjY=';frame-src {origins}";
            }

            if (!context.Response.Headers.ContainsKey("Content-Security-Policy"))
            {
                context.Response.Headers.Add("Content-Security-Policy", value);
            }

            if (!context.Response.Headers.ContainsKey("X-Content-Security-Policy"))
            {
                context.Response.Headers.Add("X-Content-Security-Policy", value);
            }
        }

        private void AddXfoHeaders(HttpContext context)
        {
            if (!context.Response.Headers.ContainsKey("X-Frame-Options"))
            {
                var logoutPageUrl = _options.UserInteractionOptions.LogoutUrl;
                if (logoutPageUrl.IsLocalUrl())
                {
                    context.Response.Headers.Add("X-Frame-Options", "SAMEORIGIN");
                }
                else
                {
                    context.Response.Headers.Add("X-Frame-Options", $"ALLOWFROM {logoutPageUrl.GetOrigin()}");
                }
            }
        }

        string GetHtml()
        {
            string framesHtml = null;

            if (_result.ClientLogoutUrls != null && _result.ClientLogoutUrls.Any())
            {
                var frameUrls = _result.ClientLogoutUrls.Select(url => $"<iframe src='{url}'></iframe>");
                framesHtml = frameUrls.Aggregate((x, y) => x + y);
            }

            return $"<!DOCTYPE html><html><style>iframe{{display:none;width:0;height:0;}}</style><body>{framesHtml}</body></html>";
        }
    }
}
