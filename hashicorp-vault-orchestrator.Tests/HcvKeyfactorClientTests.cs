// Copyright 2025 Keyfactor
// Licensed under the Apache License, Version 2.0

using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace Keyfactor.Extensions.Orchestrator.HashicorpVault.Tests
{
    /// <summary>
    /// A minimal loopback HTTP server built on a raw <see cref="TcpListener"/> rather than
    /// <see cref="System.Net.HttpListener"/>, since HttpListener requires a URL ACL reservation
    /// (or admin rights) on Windows even for loopback addresses. Good enough to capture the
    /// raw request text (including headers) for one request and return a canned JSON response.
    /// </summary>
    internal sealed class SingleRequestHttpServer : IDisposable
    {
        private readonly TcpListener _listener;

        public int Port { get; }

        public string LastRequestRaw { get; private set; } = string.Empty;

        public string LastRequestBody { get; private set; } = string.Empty;

        public SingleRequestHttpServer()
        {
            _listener = new TcpListener(System.Net.IPAddress.Loopback, 0);
            _listener.Start();
            Port = ((System.Net.IPEndPoint)_listener.LocalEndpoint).Port;
        }

        public async Task RespondOnceAsync(string jsonBody, string status = "200 OK")
        {
            using var client = await _listener.AcceptTcpClientAsync();
            using var stream = client.GetStream();
            using var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, leaveOpen: true);

            var lines = new System.Collections.Generic.List<string>();
            string line;
            var contentLength = 0;
            while (!string.IsNullOrEmpty(line = await reader.ReadLineAsync()))
            {
                lines.Add(line);
                if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                {
                    contentLength = int.Parse(line.Substring("Content-Length:".Length).Trim());
                }
            }
            LastRequestRaw = string.Join("\n", lines);

            if (contentLength > 0)
            {
                var buffer = new char[contentLength];
                var totalRead = 0;
                while (totalRead < contentLength)
                {
                    var read = await reader.ReadAsync(buffer, totalRead, contentLength - totalRead);
                    if (read == 0) break;
                    totalRead += read;
                }
                LastRequestBody = new string(buffer, 0, totalRead);
            }

            var bodyBytes = Encoding.UTF8.GetBytes(jsonBody);
            var header = $"HTTP/1.1 {status}\r\nContent-Type: application/json\r\nContent-Length: {bodyBytes.Length}\r\nConnection: close\r\n\r\n";
            var headerBytes = Encoding.ASCII.GetBytes(header);
            await stream.WriteAsync(headerBytes, 0, headerBytes.Length);
            await stream.WriteAsync(bodyBytes, 0, bodyBytes.Length);
            await stream.FlushAsync();
        }

        public void Dispose() => _listener.Stop();
    }

    public class HcvKeyfactorClientTests
    {
        private const string FakeCertJson =
            "{\"data\":{\"certificate\":\"-----BEGIN CERTIFICATE-----\\nFAKECERTDATA\\n-----END CERTIFICATE-----\",\"revocation_time\":\"0\"}}";

        [Fact]
        public async Task GetCertificateFromPemStore_WhenNamespaceConfigured_SendsNamespaceHeader()
        {
            using var server = new SingleRequestHttpServer();
            var responseTask = server.RespondOnceAsync(FakeCertJson);

            var client = new HcvKeyfactorClient("test-token", $"http://127.0.0.1:{server.Port}", "pki", null, "engineering/team-a");

            var itemTask = client.GetCertificateFromPemStore("mykey");

            await Task.WhenAll(responseTask, itemTask);
            var item = await itemTask;

            server.LastRequestRaw.Should().Contain("X-Vault-Namespace: engineering/team-a");
            item.Should().NotBeNull();
            item!.Alias.Should().Be("mykey");
        }

        [Fact]
        public async Task GetCertificateFromPemStore_WhenNamespaceNotConfigured_OmitsNamespaceHeader()
        {
            using var server = new SingleRequestHttpServer();
            var responseTask = server.RespondOnceAsync(FakeCertJson);

            var client = new HcvKeyfactorClient("test-token", $"http://127.0.0.1:{server.Port}", "pki", null);

            var itemTask = client.GetCertificateFromPemStore("mykey");

            await Task.WhenAll(responseTask, itemTask);

            server.LastRequestRaw.Should().NotContain("X-Vault-Namespace");
        }
    }
}
