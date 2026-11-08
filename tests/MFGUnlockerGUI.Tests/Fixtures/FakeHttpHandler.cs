using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MFGUnlockerGUI.Tests.Fixtures
{
    /// <summary>Maps URL to bytes/status. Unmapped URLs fail like a dropped connection. Never touches the network.</summary>
    public sealed class FakeHttpHandler : HttpMessageHandler
    {
        private readonly Dictionary<string, KeyValuePair<HttpStatusCode, byte[]>> _map =
            new Dictionary<string, KeyValuePair<HttpStatusCode, byte[]>>();

        public List<string> Requests { get; private set; }
        public List<HttpMethod> Methods { get; private set; }

        public FakeHttpHandler()
        {
            Requests = new List<string>();
            Methods = new List<HttpMethod>();
        }

        public FakeHttpHandler Add(string url, byte[] bytes, HttpStatusCode status = HttpStatusCode.OK)
        {
            _map[url] = new KeyValuePair<HttpStatusCode, byte[]>(status, bytes);
            return this;
        }

        public FakeHttpHandler AddText(string url, string text)
        {
            return Add(url, Encoding.UTF8.GetBytes(text));
        }

        public FakeHttpHandler Remove(string url)
        {
            _map.Remove(url);
            return this;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri.ToString();
            Requests.Add(url);
            Methods.Add(request.Method);
            KeyValuePair<HttpStatusCode, byte[]> hit;
            if (!_map.TryGetValue(url, out hit))
                throw new HttpRequestException("FakeHttpHandler: no connection to " + url);
            return Task.FromResult(new HttpResponseMessage(hit.Key) { Content = new ByteArrayContent(hit.Value ?? new byte[0]) });
        }
    }
}
