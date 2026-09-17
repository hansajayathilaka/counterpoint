using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Counterpoint.Integration.Tests.Backup.Targets;

/// <summary>
/// Stands in for the network for <see cref="Counterpoint.Backup.Targets.S3CompatibleTarget"/> and
/// <see cref="Counterpoint.Backup.Targets.GoogleDriveTarget"/> tests - both are built against a
/// plain <see cref="HttpClient"/> specifically so nothing more than this is needed to prove them
/// on Linux, with no real S3 bucket or Google account involved (P4-T01's own guidance).
/// </summary>
internal sealed class FakeHttpMessageHandler : HttpMessageHandler
{
    private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> _responses = new();

    internal List<HttpRequestMessage> Requests { get; } = [];

    internal FakeHttpMessageHandler Enqueue(Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        _responses.Enqueue(respond);
        return this;
    }

    internal FakeHttpMessageHandler Enqueue(HttpResponseMessage response) => Enqueue(_ => response);

    internal FakeHttpMessageHandler EnqueueThrow(Exception exception) =>
        Enqueue(_ => throw exception);

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);

        if (_responses.Count == 0)
        {
            throw new InvalidOperationException(
                "FakeHttpMessageHandler received a request with no queued response left: " + request.RequestUri);
        }

        return Task.FromResult(_responses.Dequeue()(request));
    }

    internal HttpClient ToHttpClient() => new(this);
}
