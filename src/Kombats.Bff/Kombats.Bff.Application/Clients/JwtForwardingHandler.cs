using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Net.Http.Headers;

namespace Kombats.Bff.Application.Clients;

public sealed class JwtForwardingHandler(IHttpContextAccessor httpContextAccessor) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        HttpContext? httpContext = httpContextAccessor.HttpContext;

        if (httpContext is not null)
        {
            string? authorization = httpContext.Request.Headers[HeaderNames.Authorization].ToString();

            if (!string.IsNullOrEmpty(authorization))
            {
                request.Headers.TryAddWithoutValidation(HeaderNames.Authorization, authorization);
            }
        }

        // Explicit W3C trace context propagation to downstream services. HttpClient's
        // DiagnosticsHandler normally injects this implicitly, but we set it here so
        // propagation is guaranteed regardless of instrumentation configuration.
        Activity? activity = Activity.Current;
        if (activity is not null && !request.Headers.Contains("traceparent"))
        {
            string? traceparent = activity.Id;
            if (!string.IsNullOrEmpty(traceparent))
            {
                request.Headers.TryAddWithoutValidation("traceparent", traceparent);

                string? tracestate = activity.TraceStateString;
                if (!string.IsNullOrEmpty(tracestate))
                {
                    request.Headers.TryAddWithoutValidation("tracestate", tracestate);
                }
            }
        }

        return base.SendAsync(request, cancellationToken);
    }
}
